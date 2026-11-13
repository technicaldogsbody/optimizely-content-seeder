using System.Collections.Concurrent;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>
/// Default <see cref="IImageSource"/>: uses the source the request asks for (or the configured default) and, when it
/// cannot supply an image, a fallback (images drawn in code by default). Once a source has failed, the rest of the seed
/// operation skips it rather than waiting on every image.
/// </summary>
public sealed class FallbackImageSource : IImageSource, IDescribesProvider
{
    private readonly Func<ImageProvider, IImageSource> _resolve;
    private readonly ImageProvider _defaultProvider;
    private readonly IImageSource _fallback;
    private readonly ConcurrentDictionary<ImageProvider, bool> _unavailable = new();

    /// <summary>Creates a source that always uses <paramref name="primary"/>, then <paramref name="fallback"/>.</summary>
    public FallbackImageSource(IImageSource primary, IImageSource fallback)
        : this(_ => primary, default, fallback)
    {
    }

    /// <summary>
    /// Creates a source that picks the image source from each request's <see cref="ImageRequest.Provider"/>, or
    /// <paramref name="defaultProvider"/> when the request names none.
    /// </summary>
    public FallbackImageSource(Func<ImageProvider, IImageSource> resolve, ImageProvider defaultProvider, IImageSource fallback)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(fallback);
        _resolve = resolve;
        _defaultProvider = defaultProvider;
        _fallback = fallback;
    }

    /// <inheritdoc/>
    public ProviderInfo Describe()
    {
        var primary = _resolve(_defaultProvider);
        var info = (primary as IDescribesProvider)?.Describe() ?? new ProviderInfo(primary.GetType().Name);
        return ReferenceEquals(primary, _fallback)
            ? info
            : info with { Detail = $"{info.Detail} (drawn in code if unreachable)".Trim() };
    }

    /// <inheritdoc/>
    public async Task<GeneratedImage?> GetImageAsync(ImageRequest request, CancellationToken ct = default)
    {
        var provider = request.Provider ?? _defaultProvider;
        var primary = _resolve(provider);

        if (ReferenceEquals(primary, _fallback))
            return await primary.GetImageAsync(request, ct).ConfigureAwait(false);

        if (!_unavailable.ContainsKey(provider))
        {
            var image = await primary.GetImageAsync(request, ct).ConfigureAwait(false);
            if (image is not null)
                return image;
            _unavailable[provider] = true;
        }

        return await _fallback.GetImageAsync(request, ct).ConfigureAwait(false);
    }
}
