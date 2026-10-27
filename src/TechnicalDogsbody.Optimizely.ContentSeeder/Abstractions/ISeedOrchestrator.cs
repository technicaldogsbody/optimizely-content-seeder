using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Orchestrates seed, reseed, delete, and dry-run operations. Each operation runs either for every definition, or for
/// one definition on its own. An operation on one definition touches only that definition's content, so definitions
/// never override or delete each other's content.
/// Scoped lifetime.
/// </summary>
internal interface ISeedOrchestrator
{
    /// <summary>The ids of every registered seed definition.</summary>
    IReadOnlyList<string> DefinitionIds { get; }

    /// <summary>Seeds all definitions, skipping already-seeded non-partial content.</summary>
    Task SeedAsync(CancellationToken ct = default);

    /// <summary>Seeds one definition, skipping content that already exists. Throws if no definition has this id.</summary>
    Task SeedAsync(string seedDefinitionId, CancellationToken ct = default);

    /// <summary>Deletes all seeded content, then seeds it again.</summary>
    Task ReseedAsync(CancellationToken ct = default);

    /// <summary>Deletes one definition's seeded content, then seeds that definition again. Throws if no definition has this id.</summary>
    Task ReseedAsync(string seedDefinitionId, CancellationToken ct = default);

    /// <summary>Permanently deletes all tracked content and truncates the tracking table.</summary>
    Task DeleteAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes one definition's tracked content, its tracking records and its seeded images. Works for an
    /// id that is no longer registered, so content from a removed definition can still be cleaned up.
    /// </summary>
    Task DeleteAllAsync(string seedDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Calculates what would happen if SeedAsync were called now. Writes no content.
    /// </summary>
    Task<DryRunResult> DryRunAsync(CancellationToken ct = default);

    /// <summary>Calculates what would happen if one definition were seeded now. Writes no content. Throws if no definition has this id.</summary>
    Task<DryRunResult> DryRunAsync(string seedDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// IDs of seed definitions whose shape no longer matches the content they were seeded with.
    /// Seed won't apply such changes to existing content; a Reseed is needed.
    /// </summary>
    Task<IReadOnlyList<string>> GetChangedDefinitionsAsync(CancellationToken ct = default);
}
