using EPiServer.Core;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>
/// Default <see cref="IImageContentFactory"/> that gets the image from the registered <see cref="IImageSource"/>
/// (Picsum placeholder photos by default), and saves it as a media asset in the Optimizely content tree
/// under the seeded media folder (<see cref="Options.ContentSeederOptions.SeededImagesFolder"/>).
/// </summary>
public sealed class OptimizelyImageContentFactory(
    SeededMediaWriter mediaWriter,
    IImageSource imageSource,
    ILogger<OptimizelyImageContentFactory> logger) : IImageContentFactory, IDescribesProvider
{
    /// <inheritdoc/>
    public ProviderInfo Describe()
        => (imageSource as IDescribesProvider)?.Describe() ?? new ProviderInfo(imageSource.GetType().Name);

    /// <inheritdoc/>
    public async Task<ContentHandle> CreateImageAsync(int width, int height, CancellationToken ct = default)
    {
        var imageType = mediaWriter.FindMediaType(typeof(ImageData));
        if (imageType is null)
        {
            logger.LogWarning(
                "ContentSeeder: No ImageData subtype is registered. " +
                "Add a class that extends EPiServer.Core.ImageData with a [ContentType] attribute " +
                "to enable automatic media asset creation.");
            return ContentHandle.Empty;
        }

        var source = await imageSource.GetImageAsync(new ImageRequest(width, height), ct).ConfigureAwait(false);
        if (source is null || source.Bytes.Length == 0)
            return ContentHandle.Empty;

        try
        {
            var asset = await mediaWriter
                .SaveAsync(imageType, $"seed-image-{width}-{height}-{Guid.NewGuid():N}{source.Extension}", source.Bytes, source.Extension, ct)
                .ConfigureAwait(false);
            return asset.ToHandle();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ContentSeeder: Failed to create media asset. Image property will be skipped.");
            return ContentHandle.Empty;
        }
    }

    /// <inheritdoc/>
    public Task DeleteSeededImagesAsync(string? seedDefinitionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        mediaWriter.DeleteFolder(seedDefinitionId);
        return Task.CompletedTask;
    }
}
