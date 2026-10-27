using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers;

/// <summary>
/// Default <see cref="IRichTextConverter"/>: rich text properties are Optimizely <see cref="XhtmlString"/> values.
/// </summary>
public sealed class XhtmlRichTextConverter : IRichTextConverter
{
    /// <inheritdoc/>
    public bool IsRichText(Type propertyType) => propertyType == typeof(XhtmlString);

    /// <inheritdoc/>
    public object FromHtml(string html) => new XhtmlString(html);

    /// <inheritdoc/>
    public string? ReadHtml(object? value) => value is XhtmlString html ? html.ToString() : null;
}
