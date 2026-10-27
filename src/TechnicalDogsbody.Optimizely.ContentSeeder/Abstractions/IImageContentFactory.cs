namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Creates media assets for image-typed content properties.
/// Scoped lifetime — resolved fresh per seed operation.
/// </summary>
public interface IImageContentFactory
{
    /// <summary>
    /// Downloads a placeholder image and saves it as a media asset in the CMS content tree.
    /// Returns <see cref="ContentHandle.Empty"/> when no image media type is registered or the download fails.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ContentHandle> CreateImageAsync(int width, int height, CancellationToken ct = default);

    /// <summary>
    /// Deletes the seeded images of one definition, or of every definition, from the seeded images folder
    /// (<see cref="Options.ContentSeederOptions.SeededImagesFolder"/>), with every media asset under it. A no-op when it was never created.
    /// Called by <c>DeleteAllAsync</c>/<c>ReseedAsync</c> so re-seeding never leaves orphaned images
    /// behind or accumulates duplicates. Never throws — failures are logged as warnings.
    /// </summary>
    /// <param name="seedDefinitionId">The definition whose images to delete, or null for every definition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteSeededImagesAsync(string? seedDefinitionId, CancellationToken ct = default);
}
