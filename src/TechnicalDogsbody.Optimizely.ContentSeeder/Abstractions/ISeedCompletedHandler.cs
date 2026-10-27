namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Runs after a seed or reseed completes successfully, whether it was started from the admin UI or at startup.
/// Use it for work that depends on seeded content, such as pointing a site at the seeded start page.
/// Scoped lifetime. Register with <c>AddSeedCompletedHandler</c> on the <c>AddContentSeeder()</c> builder.
/// </summary>
public interface ISeedCompletedHandler
{
    /// <summary>Called once per successful seed or reseed.</summary>
    Task HandleAsync(CancellationToken ct = default);
}
