namespace AgentMemoryOS.Stores;

internal sealed record FactEntry(string Content, int Count, DateTimeOffset UpdatedAt);
