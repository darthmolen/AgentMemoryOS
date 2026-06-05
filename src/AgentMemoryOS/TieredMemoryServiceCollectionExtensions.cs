using AgentMemoryOS;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Embeddings;
using AgentMemoryOS.Pipeline;
using AgentMemoryOS.Stores;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

// DI registration extensions live in the Microsoft.Extensions.DependencyInjection namespace by
// convention so callers discover them through the standard using directive. This is the accepted
// exception to the namespace-matches-folder rule.
#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Registration helpers for the tiered-memory provider and its supporting services.
/// </summary>
public static class TieredMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tiered-memory provider, its background pipeline, and a default-scope
    /// <see cref="TieredMemoryProvider"/>. When no durable store is selected in
    /// <paramref name="configure"/>, the zero-dependency in-memory stores are used. The extractor
    /// defaults to the <see cref="IChatClient"/> already registered in the container.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">An optional callback to choose stores, caching, and options.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTieredMemory(this IServiceCollection services, Action<ITieredMemoryBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new TieredMemoryBuilder(services);
        configure?.Invoke(builder);

        // In-memory fallback fills any stores not already registered (workspace + embedder always;
        // facts + vectors only when a durable store was not chosen above).
        services.AddTieredMemoryInMemoryStores();

        // Deferred decorations (for example Redis cache-aside) run now that the stores exist, so
        // they wrap the final store registrations regardless of the caller's order.
        foreach (var postConfigure in builder.PostConfigurations)
        {
            postConfigure(services);
        }

        services.TryAddSingleton<ObservationChannel>();
        services.TryAddSingleton<IObservationSink, ChannelObservationSink>();
        services.AddHostedService<MemoryReconciler>();

        var optionsConfigurator = builder.OptionsConfigurator;
        services.TryAddSingleton(_ =>
        {
            var options = new TieredMemoryOptions();
            optionsConfigurator?.Invoke(options);
            return options;
        });

        var extractorResolver = builder.ExtractorResolver;
        services.TryAddSingleton<Func<MemoryScope, TieredMemoryProvider>>(provider => scope =>
            extractorResolver is null
                ? ActivatorUtilities.CreateInstance<TieredMemoryProvider>(provider, scope)
                : ActivatorUtilities.CreateInstance<TieredMemoryProvider>(provider, scope, extractorResolver(provider)));

        services.TryAddSingleton(provider => provider.GetRequiredService<Func<MemoryScope, TieredMemoryProvider>>()(MemoryScope.Default));

        return services;
    }

    /// <summary>
    /// Registers the tiered-memory provider, binding the recall and extraction options
    /// (<see cref="TieredMemoryOptions"/>) from a configuration section. Store and cache selection
    /// is performed in <paramref name="configure"/> because those are gated by optional packages.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="section">The configuration section bound onto the memory options.</param>
    /// <param name="configure">An optional callback to choose stores and caching.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTieredMemory(this IServiceCollection services, IConfiguration section, Action<ITieredMemoryBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        return services.AddTieredMemory(builder =>
        {
            builder.Configure(options => section.Bind(options));
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Registers the zero-dependency in-memory stores and the default deterministic embedder.
    /// Most callers use <see cref="AddTieredMemory(IServiceCollection, Action{ITieredMemoryBuilder})"/>
    /// instead, which applies these as a fallback automatically.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTieredMemoryInMemoryStores(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InMemoryWorkspaceStore>();
        services.TryAddSingleton<IWorkspaceStore>(provider => provider.GetRequiredService<InMemoryWorkspaceStore>());
        services.TryAddSingleton<IFactStore, InMemoryFactStore>();
        services.TryAddSingleton<IVectorRecall, InMemoryVectorRecall>();
        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>, HashingEmbeddingGenerator>();

        return services;
    }
}
