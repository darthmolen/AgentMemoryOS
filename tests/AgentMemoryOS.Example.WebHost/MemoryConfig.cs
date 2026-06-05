namespace AgentMemoryOS.Example.WebHost;

/// <summary>
/// Strongly-typed view of the "Memory" configuration section. Every knob (chat backend,
/// store, redis) is configurable via appsettings.json + user secrets, so this same host
/// represents how a customer would consume the future NuGet package.
/// </summary>
public sealed class MemoryConfig
{
    public string Backend { get; set; } = "Local";

    public LocalBackendConfig Local { get; set; } = new();

    public FoundryBackendConfig Foundry { get; set; } = new();

    public string Store { get; set; } = "InMemory";

    public PostgresConfig Postgres { get; set; } = new();

    public RedisConfig Redis { get; set; } = new();

    public string Workspace { get; set; } = string.Empty;
}

public sealed class LocalBackendConfig
{
    public string Endpoint { get; set; } = "http://localhost:8000/v1";

    public string Model { get; set; } = "Qwen/Qwen2.5-7B-Instruct";

    public string ApiKey { get; set; } = "local-dev-key";
}

public sealed class FoundryBackendConfig
{
    public string Endpoint { get; set; } = string.Empty;

    public string Deployment { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class PostgresConfig
{
    public string ConnectionString { get; set; } = string.Empty;

    public int EmbeddingDimensions { get; set; } = 256;
}

public sealed class RedisConfig
{
    public bool Enabled { get; set; }

    public string ConnectionString { get; set; } = "localhost:6379";
}
