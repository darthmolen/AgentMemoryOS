namespace AgentMemoryOS.Example.IntegrationTests;

using System.Collections.Generic;
using AgentMemoryOS.Example.WebHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

internal sealed class ExampleWebHostFactory : WebApplicationFactory<ExampleApiMarker>
{
    private readonly IReadOnlyDictionary<string, string?> settings;

    public ExampleWebHostFactory(IReadOnlyDictionary<string, string?> settings)
    {
        this.settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(this.settings));
    }
}
