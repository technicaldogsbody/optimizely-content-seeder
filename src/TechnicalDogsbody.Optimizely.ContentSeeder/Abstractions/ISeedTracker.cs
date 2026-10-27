using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Reads and writes ContentSeederRecord tracking rows.
/// Scoped lifetime.
/// </summary>
public interface ISeedTracker
{
    /// <summary>Inserts a tracking record before its content is created.</summary>
    /// <param name="record">The record to insert.</param>
    /// <param name="ct">Cancels the operation.</param>
    Task InsertAsync(ContentSeederRecord record, CancellationToken ct = default);

    /// <summary>Records the CMS content id of a saved item and clears its partial flag.</summary>
    /// <param name="id">The tracking record to complete.</param>
    /// <param name="contentId">The EPiServer content ID returned by IContentRepository.Save — persisted to the tracking row.</param>
    /// <param name="ct">Cancels the operation.</param>
    Task MarkCompleteAsync(Guid id, int contentId, CancellationToken ct = default);

    /// <summary>Refreshes the timestamp of a seed run's unfinished records, so an interrupted run shows as partial.</summary>
    /// <param name="seedRunId">The seed run whose partial records to refresh.</param>
    /// <param name="ct">Cancels the operation.</param>
    Task MarkRunPartialAsync(Guid seedRunId, CancellationToken ct = default);

    /// <summary>Returns every tracking record created by a seed definition.</summary>
    /// <param name="seedDefinitionId">The seed definition whose records to return.</param>
    /// <param name="ct">Cancels the operation.</param>
    Task<IReadOnlyList<ContentSeederRecord>> GetByDefinitionAsync(string seedDefinitionId, CancellationToken ct = default);

    /// <summary>Returns the tracking record for one block of a definition in a language, or null if there is none.</summary>
    /// <param name="blockDefinitionId">The block's id within the seed definition.</param>
    /// <param name="language">The language of the version to find.</param>
    /// <param name="ct">Cancels the operation.</param>
    Task<ContentSeederRecord?> GetBlockByDefinitionIdAsync(string blockDefinitionId, string language, CancellationToken ct = default);

    /// <summary>Summarises what has been seeded: counts, when content was last seeded, and whether a run is partial.</summary>
    /// <param name="ct">Cancels the operation.</param>
    Task<ContentSeederStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Truncates all tracking rows and recorded definition fingerprints.</summary>
    Task TruncateAsync(CancellationToken ct = default);

    /// <summary>Deletes the tracking rows and the recorded fingerprint of one seed definition. Other definitions are untouched.</summary>
    /// <param name="seedDefinitionId">The seed definition whose tracking to delete.</param>
    /// <param name="ct">Cancels the operation.</param>
    Task DeleteDefinitionAsync(string seedDefinitionId, CancellationToken ct = default);

    /// <summary>Summarises what has been seeded for each seed definition that has tracking rows.</summary>
    /// <param name="ct">Cancels the operation.</param>
    Task<IReadOnlyList<SeedDefinitionStatus>> GetDefinitionStatusesAsync(CancellationToken ct = default);

    /// <summary>Returns the shape fingerprint recorded when the definition was last seeded fresh, if any.</summary>
    Task<string?> GetDefinitionFingerprintAsync(string seedDefinitionId, CancellationToken ct = default);

    /// <summary>Records (or replaces) the shape fingerprint for a seed definition.</summary>
    Task SetDefinitionFingerprintAsync(string seedDefinitionId, string fingerprint, CancellationToken ct = default);
}
