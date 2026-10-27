namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Generated HTML for a rich text property. A generator returns it, and the provider turns it into the CMS's own rich text
/// value, or skips it if the property is not rich text.
/// </summary>
/// <param name="Html">The HTML.</param>
internal sealed record RichTextValue(string Html);
