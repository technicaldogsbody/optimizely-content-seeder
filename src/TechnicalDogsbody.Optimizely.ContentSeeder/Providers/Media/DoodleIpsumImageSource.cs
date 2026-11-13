using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>Random doodle illustrations (PNG) from <see href="https://doodleipsum.com/">Doodle Ipsum</see>.</summary>
public sealed class DoodleIpsumImageSource(IHttpClientFactory httpClientFactory, ILogger<DoodleIpsumImageSource> logger)
    : IImageSource, IDescribesProvider
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private int _counter;

    /// <inheritdoc/>
    public ProviderInfo Describe() => new("Doodle Ipsum", "Random doodle illustrations");

    /// <inheritdoc/>
    public async Task<GeneratedImage?> GetImageAsync(ImageRequest request, CancellationToken ct = default)
    {
        // n makes every URL distinct, so caches in between cannot hand back the same doodle twice.
        string url = $"https://doodleipsum.com/{request.Width}x{request.Height}/flat?bg=random&n={Interlocked.Increment(ref _counter)}";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            var client = httpClientFactory.CreateClient(ContentSeederNames.HttpClient);
            var bytes = await client.GetByteArrayAsync(url, timeout.Token).ConfigureAwait(false);
            return new GeneratedImage(bytes, ".png");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ContentSeeder: Failed to download placeholder image from {Url}.", url);
            return null;
        }
    }
}
