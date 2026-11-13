using System.Reflection;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Generates fake content data for Optimizely content type properties.
/// </summary>
public interface IContentGenerationProvider
{
    /// <summary>
    /// Populates the given content object's properties with generated data.
    /// </summary>
    /// <param name="content">The Optimizely content instance to populate (already a writable clone).</param>
    /// <param name="properties">The reflected properties to populate.</param>
    /// <param name="language">The IETF language tag for the language branch being seeded.</param>
    /// <param name="contentType">
    /// The declared CLR type of the content being populated. Used by providers that support
    /// qualified property-name generators (e.g. <c>"HeroBlock.Title"</c>). Pass the concrete
    /// content type, not the Castle proxy type, so name lookups resolve correctly.
    /// </param>
    /// <param name="imageSizes">
    /// Explicit per-property image dimensions. Only <see cref="EPiServer.Core.ContentReference"/>
    /// properties whose name appears in this dictionary will have a media asset created.
    /// All other <c>ContentReference</c> properties are left null.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task PopulateAsync(
        object content,
        IEnumerable<PropertyInfo> properties,
        string language,
        Type? contentType = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        CancellationToken ct = default);

    /// <summary>
    /// As the overload without <paramref name="imageProviders"/>, with a choice of image source per property.
    /// A custom provider that does not override this ignores the choices.
    /// </summary>
    /// <param name="content">The Optimizely content instance to populate.</param>
    /// <param name="properties">The reflected properties to populate.</param>
    /// <param name="language">The IETF language tag for the language branch being seeded.</param>
    /// <param name="contentType">The declared CLR type of the content being populated.</param>
    /// <param name="imageSizes">Explicit per-property image dimensions.</param>
    /// <param name="imageProviders">
    /// Per-property image source, keyed by property name. A property in <paramref name="imageSizes"/> with no entry here
    /// uses the configured default (<c>ContentSeeder:ImageProvider</c>).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task PopulateAsync(
        object content,
        IEnumerable<PropertyInfo> properties,
        string language,
        Type? contentType,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes,
        IReadOnlyDictionary<string, ImageProvider>? imageProviders,
        CancellationToken ct = default)
        => PopulateAsync(content, properties, language, contentType, imageSizes, ct);
}

/// <summary>Helpers for calling an <see cref="IContentGenerationProvider"/>.</summary>
internal static class ContentGenerationProviderExtensions
{
    /// <summary>
    /// Populates <paramref name="content"/>, passing per-property image sources only when some are declared, so a provider
    /// that knows nothing about them is called exactly as before.
    /// </summary>
    internal static Task PopulateWithImageProvidersAsync(
        this IContentGenerationProvider provider,
        object content,
        IEnumerable<PropertyInfo> properties,
        string language,
        Type? contentType,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes,
        IReadOnlyDictionary<string, ImageProvider>? imageProviders,
        CancellationToken ct)
        => imageProviders is { Count: > 0 }
            ? provider.PopulateAsync(content, properties, language, contentType, imageSizes, imageProviders, ct)
            : provider.PopulateAsync(content, properties, language, contentType, imageSizes, ct);
}
