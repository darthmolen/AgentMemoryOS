using AgentMemoryOS.Abstractions;

namespace AgentMemoryOS.Postgres;

/// <summary>
/// Computes the partition key that tags every row, mirroring the in-memory store's use of the
/// whole <see cref="MemoryScope"/> as a dictionary key.
/// </summary>
internal static class PostgresScope
{
    /// <summary>
    /// Computes the scope key for a <see cref="MemoryScope"/> as
    /// <c>"{TemplateId} {InstanceId}"</c>, with a null instance id treated as empty.
    /// </summary>
    /// <param name="scope">The scope to key.</param>
    /// <returns>The partition key string.</returns>
    public static string Key(MemoryScope scope)
    {
        return $"{scope.TemplateId} {scope.InstanceId}";
    }
}
