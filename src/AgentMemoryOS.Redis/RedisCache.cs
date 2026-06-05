using System.Text.Json;
using AgentMemoryOS.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AgentMemoryOS.Redis;

/// <summary>
/// Internal cache-aside helper shared by the store decorators. Encapsulates key building,
/// JSON serialization, per-scope tag invalidation, and the fail-open behaviour that lets a
/// Redis outage fall through to the inner store instead of breaking recall or capture.
/// </summary>
internal sealed class RedisCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer connection;
    private readonly ILogger logger;
    private readonly string keyPrefix;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisCache"/> class.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer used to reach the cache.</param>
    /// <param name="logger">The logger used to record fail-open fallbacks.</param>
    /// <param name="keyPrefix">The prefix prepended to every Redis key.</param>
    public RedisCache(IConnectionMultiplexer connection, ILogger logger, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        this.connection = connection;
        this.logger = logger;
        this.keyPrefix = keyPrefix;
    }

    /// <summary>
    /// Builds the canonical scope key used to partition cached entries.
    /// </summary>
    /// <param name="scope">The scope to derive the key from.</param>
    /// <returns>The scope key in the form <c>templateId/instanceId</c>.</returns>
    public static string ScopeKey(MemoryScope scope)
    {
        return $"{scope.TemplateId}/{scope.InstanceId}";
    }

    /// <summary>
    /// Builds a fully qualified entry key from a layer name, scope key, and discriminator.
    /// </summary>
    /// <param name="layer">The cache layer name, for example <c>fact</c> or <c>vector</c>.</param>
    /// <param name="scopeKey">The scope key the entry belongs to.</param>
    /// <param name="discriminator">The per-query discriminator that distinguishes entries.</param>
    /// <returns>The fully qualified Redis key.</returns>
    public string EntryKey(string layer, string scopeKey, string discriminator)
    {
        return $"{this.keyPrefix}:{layer}:{scopeKey}:{discriminator}";
    }

    /// <summary>
    /// Reads and deserializes a cached value, returning <see langword="null"/> on a miss and
    /// failing open to <see langword="null"/> when Redis is unreachable.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the cached payload into.</typeparam>
    /// <param name="key">The Redis key to read.</param>
    /// <returns>A tuple stating whether the read succeeded and the deserialized value when it did.</returns>
    public async Task<(bool Found, T? Value)> TryGetAsync<T>(string key)
    {
        try
        {
            var database = this.connection.GetDatabase();
            var raw = await database.StringGetAsync(key);
            if (raw.IsNullOrEmpty)
            {
                return (false, default);
            }

            var value = JsonSerializer.Deserialize<T>((string)raw!, SerializerOptions);
            return (true, value);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            RedisCacheLog.CacheFailure(this.logger, "get", exception);
            return (false, default);
        }
    }

    /// <summary>
    /// Serializes and stores a value under the given key with a time-to-live, registering the
    /// key against the scope tag so it can be invalidated later. Failures are swallowed so a
    /// caching error never propagates to the caller.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="layer">The cache layer name the entry belongs to.</param>
    /// <param name="scopeKey">The scope key the entry belongs to.</param>
    /// <param name="key">The Redis key to write.</param>
    /// <param name="value">The value to serialize and store.</param>
    /// <param name="ttl">The time-to-live for the entry.</param>
    /// <returns>A task that represents the asynchronous store operation.</returns>
    public async Task SetTaggedAsync<T>(string layer, string scopeKey, string key, T value, TimeSpan ttl)
    {
        try
        {
            var database = this.connection.GetDatabase();
            var payload = JsonSerializer.Serialize(value, SerializerOptions);
            await database.StringSetAsync(key, payload, ttl);

            var tagKey = this.TagKey(layer, scopeKey);
            await database.SetAddAsync(tagKey, key);
            await database.KeyExpireAsync(tagKey, ttl);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            RedisCacheLog.CacheFailure(this.logger, "set", exception);
        }
    }

    /// <summary>
    /// Serializes and stores a value under the given key with a time-to-live, without tagging.
    /// Used for read-only layers that have no write API and therefore need no invalidation.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The Redis key to write.</param>
    /// <param name="value">The value to serialize and store.</param>
    /// <param name="ttl">The time-to-live for the entry.</param>
    /// <returns>A task that represents the asynchronous store operation.</returns>
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl)
    {
        try
        {
            var database = this.connection.GetDatabase();
            var payload = JsonSerializer.Serialize(value, SerializerOptions);
            await database.StringSetAsync(key, payload, ttl);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            RedisCacheLog.CacheFailure(this.logger, "set", exception);
        }
    }

    /// <summary>
    /// Deletes every cached entry registered under a scope tag, then the tag itself. Used to
    /// invalidate a scope's cached recalls after a write. Failures are swallowed.
    /// </summary>
    /// <param name="layer">The cache layer name to invalidate.</param>
    /// <param name="scopeKey">The scope key to invalidate.</param>
    /// <returns>A task that represents the asynchronous invalidation operation.</returns>
    public async Task InvalidateScopeAsync(string layer, string scopeKey)
    {
        try
        {
            var database = this.connection.GetDatabase();
            var tagKey = this.TagKey(layer, scopeKey);
            var members = await database.SetMembersAsync(tagKey);
            if (members.Length > 0)
            {
                var keys = Array.ConvertAll(members, member => (RedisKey)(string)member!);
                await database.KeyDeleteAsync(keys);
            }

            await database.KeyDeleteAsync(tagKey);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            RedisCacheLog.CacheFailure(this.logger, "invalidate", exception);
        }
    }

    private static bool IsRedisFailure(Exception exception)
    {
        return exception is RedisConnectionException
            or RedisTimeoutException
            or RedisServerException
            or TimeoutException;
    }

    private string TagKey(string layer, string scopeKey)
    {
        return $"{this.keyPrefix}:{layer}:tag:{scopeKey}";
    }
}
