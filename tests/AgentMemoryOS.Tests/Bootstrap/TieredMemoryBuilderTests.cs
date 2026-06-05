namespace AgentMemoryOS.Tests.Bootstrap;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentMemoryOS;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Stores;
using AgentMemoryOS.Tests.Provider;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class TieredMemoryBuilderTests
{
    [TestMethod]
    public void AddTieredMemoryRegistersResolvableProviderAndInMemoryStores()
    {
        using var provider = BuildProvider(services => services.AddTieredMemory());

        Assert.IsNotNull(provider.GetService<TieredMemoryProvider>());
        Assert.IsInstanceOfType<InMemoryFactStore>(provider.GetRequiredService<IFactStore>());
    }

    [TestMethod]
    public void ConfigureAppliesRecallOptions()
    {
        using var provider = BuildProvider(services =>
            services.AddTieredMemory(memory => memory.Configure(options => options.MinTrust = 0.42)));

        Assert.AreEqual(0.42, provider.GetRequiredService<TieredMemoryOptions>().MinTrust, 1e-9);
    }

    [TestMethod]
    public void InMemoryFallbackDoesNotClobberAnAlreadyRegisteredStore()
    {
        var registered = new CountingFactStore();
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IFactStore>(registered);
            services.AddTieredMemory();
        });

        Assert.AreSame(registered, provider.GetRequiredService<IFactStore>());
    }

    [TestMethod]
    public void CreateMemoryAgentAttachesTheProvider()
    {
        using var provider = BuildProvider(services => services.AddTieredMemory());

        var options = new ChatClientAgentOptions();
        options.AddTieredMemory(provider);

        Assert.IsNotNull(options.AIContextProviders);
        Assert.AreEqual(1, options.AIContextProviders.Count());
        Assert.IsInstanceOfType<TieredMemoryProvider>(options.AIContextProviders.Single());
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IChatClient>(_ => new FakeChatClient("NONE"));
        configure(services);
        return services.BuildServiceProvider();
    }

    private sealed class CountingFactStore : IFactStore
    {
        public Task<IReadOnlyList<Fact>> RecallAsync(MemoryScope scope, string query, double minTrust, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Fact>>([]);
        }

        public Task UpsertObservationAsync(MemoryScope scope, Observation observation, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
