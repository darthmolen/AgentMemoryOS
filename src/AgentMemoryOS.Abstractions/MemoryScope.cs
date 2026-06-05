namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Identifies the memory partition that a read or write applies to.
/// </summary>
/// <remarks>
/// In single-agent mode every call uses <see cref="Default"/>. In a swarm, the
/// <see cref="TemplateId"/> partitions shared memory so that all instances of one swarm
/// template share a namespace while other templates stay isolated.
/// </remarks>
public readonly record struct MemoryScope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryScope"/> struct.
    /// </summary>
    /// <param name="templateId">The swarm template identifier that owns the partition.</param>
    /// <param name="instanceId">The optional instance identifier, for instance-private state.</param>
    public MemoryScope(string templateId, string? instanceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        this.TemplateId = templateId;
        this.InstanceId = instanceId;
    }

    /// <summary>
    /// Gets the default single-agent scope.
    /// </summary>
    public static MemoryScope Default { get; } = new("default");

    /// <summary>
    /// Gets the swarm template identifier that owns the partition.
    /// </summary>
    public string TemplateId { get; }

    /// <summary>
    /// Gets the optional instance identifier used for instance-private state.
    /// </summary>
    public string? InstanceId { get; }
}
