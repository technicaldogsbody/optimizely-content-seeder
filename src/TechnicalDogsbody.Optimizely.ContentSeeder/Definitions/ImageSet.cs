using System.Collections;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Image properties for a definition: each one's size and, optionally, which image source it uses. Build one with
/// <c>SeedFactory.Image(...)</c> and pass it as <c>imageSizes</c>; chain <see cref="Image"/> for more than one property.
/// <code>
/// imageSizes: Image(nameof(ArticlePage.HeroImage), 1200, 630, ImageProvider.Generated)
///     .Image(nameof(ArticlePage.CardImage), 400, 300)
/// </code>
/// It is also the plain <c>imageSizes</c> dictionary, so it can be passed anywhere one is accepted. A property given
/// no provider uses the configured default (<c>ContentSeeder:ImageProvider</c>).
/// </summary>
public sealed class ImageSet : IReadOnlyDictionary<string, (int Width, int Height)>
{
    private readonly Dictionary<string, (int Width, int Height)> _sizes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageProvider> _providers = new(StringComparer.Ordinal);

    /// <summary>The image source chosen for each property that named one.</summary>
    public IReadOnlyDictionary<string, ImageProvider> Providers => _providers;

    /// <summary>Adds (or replaces) an image property, and returns this set so more can be chained.</summary>
    /// <param name="property">The property name, e.g. <c>nameof(ArticlePage.HeroImage)</c>.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="provider">The image source for this property, or null to use the configured default.</param>
    public ImageSet Image(string property, int width, int height, ImageProvider? provider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        _sizes[property] = (width, height);
        if (provider is { } chosen)
            _providers[property] = chosen;
        else
            _providers.Remove(property);
        return this;
    }

    /// <summary>
    /// Combines the providers carried by an <see cref="ImageSet"/> passed as <paramref name="sizes"/> with any passed
    /// explicitly as <paramref name="providers"/>; an explicit entry wins for the same property.
    /// </summary>
    internal static IReadOnlyDictionary<string, ImageProvider>? Merge(
        IReadOnlyDictionary<string, (int Width, int Height)>? sizes,
        IReadOnlyDictionary<string, ImageProvider>? providers)
    {
        if (sizes is not ImageSet { Providers.Count: > 0 } set)
            return providers;
        if (providers is not { Count: > 0 })
            return set.Providers;

        var merged = new Dictionary<string, ImageProvider>(set.Providers, StringComparer.Ordinal);
        foreach (var (property, provider) in providers)
            merged[property] = provider;
        return merged;
    }

    // ── IReadOnlyDictionary<string, (int Width, int Height)> ─────────────────

    /// <inheritdoc/>
    public (int Width, int Height) this[string key] => _sizes[key];

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _sizes.Keys;

    /// <inheritdoc/>
    public IEnumerable<(int Width, int Height)> Values => _sizes.Values;

    /// <inheritdoc/>
    public int Count => _sizes.Count;

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _sizes.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out (int Width, int Height) value) => _sizes.TryGetValue(key, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, (int Width, int Height)>> GetEnumerator() => _sizes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
