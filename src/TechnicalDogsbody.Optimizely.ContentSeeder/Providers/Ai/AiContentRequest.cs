namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Ai;

/// <summary>How a field should be written.</summary>
public enum AiTextFieldKind
{
    /// <summary>A plain-text <c>string</c> property: a phrase or a sentence or two.</summary>
    ShortText,

    /// <summary>An <c>XhtmlString</c> property: one or more paragraphs separated by blank lines.</summary>
    RichText,
}

/// <summary>A property the AI provider should write.</summary>
/// <param name="Name">The property name, which is also the key of the field in the model's JSON reply.</param>
/// <param name="Kind">Whether the text is short plain text or rich text.</param>
/// <param name="Guidance">What the text should say, from the writing brief. Optional.</param>
/// <param name="SourceText">For translation requests, the master-language text to translate.</param>
/// <param name="MaxWords">Hard limit on words, enforced for plain-text fields.</param>
/// <param name="MaxCharacters">Hard limit on characters, enforced for plain-text fields.</param>
public sealed record AiTextField(
    string Name,
    AiTextFieldKind Kind,
    string? Guidance,
    string? SourceText = null,
    int? MaxWords = null,
    int? MaxCharacters = null);

/// <summary>
/// One structured-generation request for a single content item. Providers send
/// <see cref="SystemPrompt"/> and <see cref="UserPrompt"/> and constrain the reply to
/// <see cref="JsonSchema"/>: an object with one string property per field in <see cref="Fields"/>.
/// </summary>
public sealed record AiContentRequest(
    string ContentTypeName,
    string Language,
    string SystemPrompt,
    string UserPrompt,
    IReadOnlyList<AiTextField> Fields,
    string JsonSchema);
