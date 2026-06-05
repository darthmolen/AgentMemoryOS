using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentMemoryOS;

/// <summary>
/// The default <see cref="ITieredMemoryBuilder"/> implementation. It records the caller's choices
/// so <c>AddTieredMemory</c> can finalize registrations in the correct order.
/// </summary>
internal sealed class TieredMemoryBuilder : ITieredMemoryBuilder
{
    private readonly List<Action<IServiceCollection>> postConfigurations = [];

    public TieredMemoryBuilder(IServiceCollection services)
    {
        this.Services = services;
    }

    public IServiceCollection Services { get; }

    public Action<TieredMemoryOptions>? OptionsConfigurator { get; private set; }

    public Func<IServiceProvider, IChatClient>? ExtractorResolver { get; private set; }

    public IReadOnlyList<Action<IServiceCollection>> PostConfigurations => this.postConfigurations;

    public ITieredMemoryBuilder Configure(Action<TieredMemoryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var existing = this.OptionsConfigurator;
        this.OptionsConfigurator = existing is null
            ? configure
            : options =>
            {
                existing(options);
                configure(options);
            };

        return this;
    }

    public ITieredMemoryBuilder UseExtractor(Func<IServiceProvider, IChatClient> extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);

        this.ExtractorResolver = extractor;
        return this;
    }

    public ITieredMemoryBuilder AddPostConfiguration(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        this.postConfigurations.Add(configure);
        return this;
    }
}
