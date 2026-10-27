using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>Default <see cref="IImageSource"/>: random placeholder photos from Picsum.</summary>
public sealed class PicsumImageSource(IHttpClientFactory httpClientFactory, ILogger<PicsumImageSource> logger)
    : IImageSource, IDescribesProvider
{
    /// <inheritdoc/>
    public ProviderInfo Describe() => new("Picsum Photos", "Random placeholder photos");

    /// <inheritdoc/>
    public async Task<GeneratedImage?> GetImageAsync(ImageRequest request, CancellationToken ct = default)
    {
        string url = $"https://picsum.photos/{request.Width}/{request.Height}";
        try
        {
            var client = httpClientFactory.CreateClient(ContentSeederNames.HttpClient);
            var bytes = await client.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            return new GeneratedImage(bytes, ".jpg");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ContentSeeder: Failed to download placeholder image from {Url}. Image property will be skipped.", url);
            return null;
        }
    }
}
