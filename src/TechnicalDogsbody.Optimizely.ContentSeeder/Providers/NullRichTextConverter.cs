using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers;

/// <summary>
/// The default <see cref="IRichTextConverter"/> when no CMS connector is registered. No property is rich text, so rich text
/// rules are skipped and the AI provider writes plain text only.
/// </summary>
public sealed class NullRichTextConverter : IRichTextConverter
{
    /// <inheritdoc/>
    public bool IsRichText(Type propertyType) => false;

    /// <inheritdoc/>
    public object FromHtml(string html)
        => throw new InvalidOperationException("No CMS connector is registered, so there is no rich text value to create.");

    /// <inheritdoc/>
    public string? ReadHtml(object? value) => null;
}
