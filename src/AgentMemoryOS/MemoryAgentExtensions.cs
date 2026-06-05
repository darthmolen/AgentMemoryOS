using AgentMemoryOS;
using AgentMemoryOS.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// Discovered through the standard Microsoft.Extensions.DependencyInjection using directive.
#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Helpers for attaching tiered memory to a Microsoft Agent Framework agent.
/// </summary>
public static class MemoryAgentExtensions
{
    /// <summary>
    /// Resolves the <see cref="TieredMemoryProvider"/> for a memory partition.
    /// </summary>
    /// <param name="services">The service provider built from a container that called <c>AddTieredMemory</c>.</param>
    /// <param name="scope">The partition the provider reads and writes.</param>
    /// <returns>The provider for the requested scope.</returns>
    public static TieredMemoryProvider GetTieredMemoryProvider(this IServiceProvider services, MemoryScope scope)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetRequiredService<Func<MemoryScope, TieredMemoryProvider>>()(scope);
    }

    /// <summary>
    /// Appends the tiered-memory provider to an agent's context providers.
    /// </summary>
    /// <param name="options">The agent options to augment.</param>
    /// <param name="services">The service provider that has tiered memory registered.</param>
    /// <param name="scope">The memory partition; defaults to <see cref="MemoryScope.Default"/>.</param>
    /// <returns>The same options, for chaining.</returns>
    public static ChatClientAgentOptions AddTieredMemory(this ChatClientAgentOptions options, IServiceProvider services, MemoryScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        var provider = services.GetTieredMemoryProvider(scope ?? MemoryScope.Default);
        List<AIContextProvider> providers = options.AIContextProviders is null ? [] : [.. options.AIContextProviders];
        providers.Add(provider);
        options.AIContextProviders = providers;

        return options;
    }

    /// <summary>
    /// Creates a <see cref="ChatClientAgent"/> with tiered memory already attached. The shortest
    /// path from a chat client to a memory-backed agent.
    /// </summary>
    /// <param name="chatClient">The chat client that backs the agent.</param>
    /// <param name="services">The service provider that has tiered memory registered.</param>
    /// <param name="configure">An optional callback to set the agent name, instructions, and chat options.</param>
    /// <param name="scope">The memory partition; defaults to <see cref="MemoryScope.Default"/>.</param>
    /// <returns>An agent whose context providers include the tiered-memory provider.</returns>
    public static ChatClientAgent CreateMemoryAgent(this IChatClient chatClient, IServiceProvider services, Action<ChatClientAgentOptions>? configure = null, MemoryScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(services);

        var options = new ChatClientAgentOptions();
        configure?.Invoke(options);
        options.AddTieredMemory(services, scope);

        return new ChatClientAgent(chatClient, options, services.GetService<ILoggerFactory>());
    }
}
