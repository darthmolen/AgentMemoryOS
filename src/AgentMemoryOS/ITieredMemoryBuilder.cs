using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentMemoryOS;

/// <summary>
/// Configures the tiered-memory registration. The builder owns registration ordering so callers
/// can compose stores, caching, and options in any order without the <c>TryAdd</c> sequencing
/// that the lower-level extensions require.
/// </summary>
public interface ITieredMemoryBuilder
{
    /// <summary>
    /// Gets the underlying service collection, for advanced composition.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Customizes the recall and extraction options. Multiple calls compose in order.
    /// </summary>
    /// <param name="configure">The callback that mutates the options.</param>
    /// <returns>The same builder, for chaining.</returns>
    public ITieredMemoryBuilder Configure(Action<TieredMemoryOptions> configure);

    /// <summary>
    /// Overrides the chat client used to extract observations. Defaults to the
    /// <see cref="IChatClient"/> already registered in the container.
    /// </summary>
    /// <param name="extractor">A factory that resolves the extractor from the service provider.</param>
    /// <returns>The same builder, for chaining.</returns>
    public ITieredMemoryBuilder UseExtractor(Func<IServiceProvider, IChatClient> extractor);

    /// <summary>
    /// Queues a registration that runs after the backing stores have been registered. Optional
    /// integration packages (for example Redis cache-aside) use this so their decorators wrap the
    /// final store registrations regardless of call order.
    /// </summary>
    /// <param name="configure">The deferred registration callback.</param>
    /// <returns>The same builder, for chaining.</returns>
    public ITieredMemoryBuilder AddPostConfiguration(Action<IServiceCollection> configure);
}
