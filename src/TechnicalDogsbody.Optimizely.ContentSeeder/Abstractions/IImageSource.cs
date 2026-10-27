namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Supplies the bytes for a seeded image; the default <see cref="IImageContentFactory"/> saves them as a
/// media asset. The default source downloads placeholder photos from Picsum; register your own to use
/// another image provider.
/// Scoped lifetime — resolved fresh per seed operation.
/// </summary>
public interface IImageSource
{
    /// <summary>Returns the image, or null if none could be produced (the image property is then left empty).</summary>
    Task<GeneratedImage?> GetImageAsync(ImageRequest request, CancellationToken ct = default);
}
