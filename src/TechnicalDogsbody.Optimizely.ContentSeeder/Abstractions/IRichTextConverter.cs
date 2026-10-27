namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Reads and writes the CMS's rich text property values, so the AI and seeding code can work with HTML without
/// referring to the CMS's own rich text type.
/// </summary>
public interface IRichTextConverter
{
    /// <summary>True when a property of this CLR type holds rich text.</summary>
    bool IsRichText(Type propertyType);

    /// <summary>Creates a value of the CMS's rich text type holding the given HTML.</summary>
    object FromHtml(string html);

    /// <summary>
    /// The HTML of a rich text value, or null when <paramref name="value"/> is not rich text.
    /// </summary>
    string? ReadHtml(object? value);
}
