using System.Reflection;

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
}
