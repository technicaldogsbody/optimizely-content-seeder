using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo;

/// <summary>
/// Makes sure the demo site exists when the app starts: an application whose entry point is the site's own home page.
/// Seeding does not change it, so nothing needs to run after a seed.
/// </summary>
public sealed class DemoSiteStartup(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DemoSiteInitializer>().EnsureSiteAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
