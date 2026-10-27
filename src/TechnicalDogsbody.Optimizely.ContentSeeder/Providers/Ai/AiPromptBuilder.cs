using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Ai;

/// <summary>
/// Builds the prompts and JSON schemas for AI content requests. Pure: it depends only on its inputs, so each
/// request can be checked without calling a model.
/// </summary>
public static class AiPromptBuilder
{
    // A different angle per item keeps repeated items of one type from reading the same.
    private static readonly string[] Angles =
    [
        "a practical how-to", "a seasonal angle", "a customer story", "a spotlight on one specific product or service",
        "a behind-the-scenes look", "an answer to a common question", "a comparison of two options",
        "a beginner's introduction", "an expert tip", "a local or community angle", "a surprising fact",
        "a short checklist", "a myth worth busting", "a look ahead at what's coming next",
    ];

    // ── prompt ───────────────────────────────────────────────────────────────

    /// <summary>Builds the request for one content item. Exposed for testing and custom providers.</summary>
    public static AiContentRequest BuildRequest(
        string typeName, string language, IReadOnlyList<AiTextField> fields,
        AiWritingBrief brief, ResolvedContentTypeBrief typeBrief)
    {
        // The system prompt only depends on configuration, so it is identical for every request.
        var system = new StringBuilder();
        system.AppendLine("You write realistic content for an Optimizely CMS website so it can be demonstrated and tested.");
        system.AppendLine("Write copy a real editor would publish: specific, concrete and varied. Never use lorem ipsum, and never mention that the content is placeholder, generated or for testing.");
        AppendLine(system, "About the site", brief.Prompt);
        AppendLine(system, "Tone of voice", brief.ToneOfVoice);
        AppendLine(system, "Audience", brief.Audience);
        AppendLine(system, "House style", brief.Instructions);
        if (brief.AvoidPhrases.Count > 0)
            system.Append("Never use these words or phrases: ").AppendJoin(", ", brief.AvoidPhrases.Select(p => $"\"{p}\"")).AppendLine(".");

        int maxParagraphs = Math.Max(1, brief.MaxParagraphs);
        var user = new StringBuilder();
        string languageLabel = LanguageLabel(language);
        user.Append("Write the content for one item of the content type \"").Append(typeName)
            .Append("\", in ").Append(languageLabel).AppendLine(".");
        user.Append("Write every field in ").Append(languageLabel).AppendLine(" only, even if these instructions are in English.");
        foreach (var typePrompt in typeBrief.Prompts)
            user.AppendLine(typePrompt.Trim());
        user.Append("Make this item distinct from others of the same type: pick one specific subject and consider ")
            .Append(Angles[Random.Shared.Next(Angles.Length)]).AppendLine(".");
        user.AppendLine();
        user.AppendLine("Fields:");
        foreach (var field in fields)
        {
            user.Append("- ").Append(field.Name).Append(": ").AppendLine(Describe(field, maxParagraphs));
        }
        user.AppendLine();
        if (fields.Any(f => Requirement(f).Length > 0))
            user.AppendLine("A field's requirement is mandatory: follow it exactly. Where it sets a length or format, it overrides every other instruction.");
        user.Append("Reply with a JSON object containing exactly these fields. ").AppendLine(FormatRule(fields));

        return new AiContentRequest(typeName, language, system.ToString().TrimEnd(), user.ToString().TrimEnd(), fields, BuildSchema(fields, maxParagraphs));
    }

    /// <summary>
    /// Builds the request to translate one content item from the master language. Exposed for testing and
    /// custom providers.
    /// </summary>
    public static AiContentRequest BuildTranslationRequest(
        string typeName, string sourceLanguage, string targetLanguage, IReadOnlyList<AiTextField> fields, AiWritingBrief brief)
    {
        string source = LanguageLabel(sourceLanguage);
        string target = LanguageLabel(targetLanguage);

        var system = new StringBuilder();
        system.AppendLine("You are a professional translator and localiser for an Optimizely CMS website.");
        system.Append("You translate content from ").Append(source).Append(" into ").Append(target)
            .Append(". The result must read as if it had been written natively in ").Append(target)
            .AppendLine(": natural, idiomatic, and in the same tone as the original.");
        system.AppendLine("Translate faithfully: keep the meaning, tone, level of detail, paragraph breaks and sentence count. Do not add, remove, explain or embellish anything. Keep brand names, product names, numbers, units and proper nouns as they are, unless they have an established name in the target language.");
        system.Append("Write only in ").Append(target).AppendLine("; never leave sentences untranslated and never reply in any other language.");
        AppendLine(system, "About the site (for context)", brief.Prompt);
        AppendLine(system, "Tone of voice", brief.ToneOfVoice);
        AppendLine(system, "Audience", brief.Audience);
        AppendLine(system, "House style (apply where it makes sense in the target language)", brief.Instructions);

        var sourceText = new JsonObject();
        foreach (var field in fields)
            sourceText[field.Name] = field.SourceText;

        var user = new StringBuilder();
        user.Append("Translate the following fields of a \"").Append(typeName).Append("\" content item from ")
            .Append(source).Append(" into ").Append(target).AppendLine(".");
        user.AppendLine();
        user.AppendLine(sourceText.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        user.AppendLine();
        if (fields.Any(f => f.Kind == AiTextFieldKind.RichText))
            user.AppendLine("Rich text fields hold HTML. Keep every tag and its structure exactly as it is, and translate only the text between the tags.");
        user.Append("Reply with a JSON object containing exactly these fields, with every value translated into ").Append(target)
            .Append(". ").AppendLine(FormatRule(fields));

        int maxParagraphs = Math.Max(1, brief.MaxParagraphs);
        string schema = BuildSchema(fields, _ => $"The {target} translation of the original text.");
        return new AiContentRequest(typeName, targetLanguage, system.ToString().TrimEnd(), user.ToString().TrimEnd(), fields, schema);
    }

    /// <summary>
    /// A language as the model should read it: "Swedish (sv)". Models follow a language name much more
    /// reliably than a bare IETF tag, which small models often ignore.
    /// </summary>
    public static string LanguageLabel(string tag)
    {
        try
        {
            string name = CultureInfo.GetCultureInfo(tag).EnglishName;
            // Invariant-globalization hosts report the tag itself or a placeholder instead of a real name.
            if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Invariant", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, tag, StringComparison.OrdinalIgnoreCase))
                return $"{name} ({tag})";
        }
        catch (CultureNotFoundException)
        {
        }

        return $"the language with IETF tag \"{tag}\"";
    }

    private static void AppendLine(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.Append(label).Append(": ").AppendLine(value.Trim());
    }

    // Tells the model which formatting each kind of field may use. Short text stays plain; rich text may use the
    // tags the sanitiser keeps (see AiTextFormatting.ToHtml).
    private static string FormatRule(IReadOnlyList<AiTextField> fields)
        => fields.Any(f => f.Kind == AiTextFieldKind.RichText)
            ? "Short text fields use plain text only: no markdown and no HTML tags. Rich text fields use only the HTML tags described for them, and no markdown."
            : "Use plain text only: no markdown, and no HTML tags of any kind.";

    private static string Describe(AiTextField field, int maxParagraphs)
    {
        string requirement = Requirement(field);
        string kind = field.Kind == AiTextFieldKind.RichText
            ? $"rich text, 1 to {maxParagraphs} paragraphs separated by a blank line. Use only these HTML tags: <p>, <strong>, <em>, <u>, <h2>, <h3>, <h4>, <ul> and <ol> with <li>, <blockquote>, and <a href> for links. No attributes other than href, and no markdown."
            // A stated requirement replaces the generic length rule rather than sitting beside it, or a
            // field told "two or three words" is also told "one or two sentences".
            : requirement.Length > 0 ? "short text." : "short text (a phrase, or one or two sentences), suited to the field name.";

        return requirement.Length == 0 ? kind : $"{kind} Requirement (mandatory, overrides any general rule): {requirement}";
    }

    // The author's guidance plus any numeric limits, as one instruction.
    private static string Requirement(AiTextField field)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(field.Guidance))
            parts.Add(field.Guidance.Trim());
        if (field.MaxWords is { } words)
            parts.Add($"At most {words} word{(words == 1 ? string.Empty : "s")}.");
        if (field.MaxCharacters is { } chars)
            parts.Add($"At most {chars} characters.");
        return string.Join(' ', parts);
    }

    private static string BuildSchema(IReadOnlyList<AiTextField> fields, int maxParagraphs)
        => BuildSchema(fields, field => Describe(field, maxParagraphs));

    private static string BuildSchema(IReadOnlyList<AiTextField> fields, Func<AiTextField, string> describe)
    {
        var props = new JsonObject();
        foreach (var field in fields)
            props[field.Name] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = describe(field),
            };

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(fields.Select(f => (JsonNode)JsonValue.Create(f.Name)!).ToArray()),
            ["additionalProperties"] = false,
        };
        return schema.ToJsonString();
    }

    /// <summary>Builds the request that asks the model to shorten texts that broke their limits.</summary>
    public static AiContentRequest BuildShortenRequest(string typeName, string language, IReadOnlyList<AiTextField> fields)
    {
        string label = LanguageLabel(language);

        var system = new StringBuilder();
        system.AppendLine("You are an editor for an Optimizely CMS website. You shorten texts so they meet a length limit, keeping the original meaning, tone and language.");
        system.Append("Write only in ").Append(label).AppendLine(". Do not add new information.");

        var texts = new JsonObject();
        foreach (var field in fields)
            texts[field.Name] = new JsonObject { ["text"] = field.SourceText, ["requirement"] = Requirement(field) };

        var user = new StringBuilder();
        user.Append("These fields of a \"").Append(typeName).AppendLine("\" content item break their requirements. Rewrite each text so it meets its requirement exactly: a length limit is a hard maximum.");
        user.AppendLine();
        user.AppendLine(texts.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        user.AppendLine();
        user.AppendLine("Reply with a JSON object containing exactly these fields, each holding only the rewritten text. Use plain text only: no markdown, and no HTML tags of any kind.");

        return new AiContentRequest(typeName, language, system.ToString().TrimEnd(), user.ToString().TrimEnd(), fields,
            BuildSchema(fields, f => $"The rewritten text. {Requirement(f)}"));
    }
}
