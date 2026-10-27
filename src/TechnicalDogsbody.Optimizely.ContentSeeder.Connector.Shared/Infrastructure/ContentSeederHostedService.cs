using EPiServer.Shell.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.MicroMediator.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Application;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;

/// <summary>
/// Startup <see cref="IHostedService"/> that:
/// 1. Runs the idempotent <see cref="ContentSeederMigration"/> to ensure the tracking table exists.
/// 2. Sends a <see cref="SeedCommand"/> when <see cref="ContentSeederOptions.SeedOnStartup"/> is <c>true</c>.
///
/// Uses <see cref="IServiceScopeFactory"/> to resolve scoped services safely inside a singleton hosted service.
/// </summary>
public sealed class ContentSeederHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ContentSeederOptions> options,
    IOptions<ProtectedModuleOptions> moduleOptions,
    ILogger<ContentSeederHostedService> logger) : IHostedService
{
    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        logger.LogInformation("Running ContentSeeder startup checks.");

        // Verify the shell module is registered — if missing, [MenuProvider] will never fire.
        var registeredNames = moduleOptions.Value.Items.Select(m => m.Name).ToList();
        if (registeredNames.Any(n => string.Equals(n, ContentSeederNames.ShellModule, StringComparison.OrdinalIgnoreCase)))
            logger.LogInformation("ContentSeeder: Shell module '{Module}' IS registered in ProtectedModuleOptions. Registered modules: {All}",
                ContentSeederNames.ShellModule, string.Join(", ", registeredNames));
        else
            logger.LogWarning("ContentSeeder: Shell module '{Module}' is NOT in ProtectedModuleOptions. Menu provider will not appear. Registered modules: {All}",
                ContentSeederNames.ShellModule, string.Join(", ", registeredNames));

        var migration = scope.ServiceProvider.GetRequiredService<ContentSeederMigration>();
        await migration.RunAsync(cancellationToken).ConfigureAwait(false);

        if (!options.Value.SeedOnStartup)
        {
            logger.LogDebug("ContentSeeder: SeedOnStartup is false — skipping automatic seeding.");
            return;
        }

        logger.LogInformation("ContentSeeder: SeedOnStartup is true — beginning seed.");

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            await mediator.SendAsync(new SeedCommand(), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("ContentSeeder: Startup seeding complete.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("ContentSeeder: Startup seeding was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ContentSeeder: Startup seeding failed.");
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
