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
/// Converts and checks AI-written text: word and character limits, the HTML allowed in rich text, and markdown
/// cleanup. Pure, so each rule can be unit tested directly.
/// </summary>
public static partial class AiTextFormatting
{
    // The tags a rich text field may keep. Anything else is removed, but its text is kept.
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "b", "em", "i", "u", "ul", "ol", "li", "h2", "h3", "h4", "blockquote", "a",
    };

    // Tags that start a block of their own, so they are not wrapped in a paragraph.
    private static readonly string[] BlockStarts = ["<ul", "<ol", "<h2", "<h3", "<h4", "<blockquote"];

    /// <summary>True when <paramref name="text"/> is longer than the field's word or character limit.</summary>
    public static bool Exceeds(AiTextField field, string text)
        => (field.MaxWords is { } words && CountWords(text) > words)
           || (field.MaxCharacters is { } chars && text.Length > chars);

    private static int CountWords(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Cuts <paramref name="text"/> to the field's limits at a word boundary, without dangling punctuation.</summary>
    public static string TrimToLimit(string text, AiTextField field)
    {
        if (field.MaxWords is { } maxWords)
        {
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > maxWords)
                text = string.Join(' ', words.Take(maxWords));
        }

        if (field.MaxCharacters is { } maxChars && text.Length > maxChars)
        {
            text = text[..maxChars];
            int lastSpace = text.LastIndexOf(' ');
            if (lastSpace > maxChars / 2)
                text = text[..lastSpace];
        }

        return text.TrimEnd(' ', ',', ';', ':', '-', '–', '—');
    }

    /// <summary>
    /// Turns a model's reply into the HTML of a rich text field: paragraphs in <c>&lt;p&gt;</c>, and only the tags in
    /// the allow-list kept. Any other tag is removed with its text left in, so a reply can never add script, styles or
    /// event handlers. Text is escaped, and an <c>href</c> is kept only for http, https, mailto or a relative link.
    /// </summary>
    public static string ToHtml(string text, int maxParagraphs)
    {
        // Models sometimes wrap their paragraphs in <p> or <br> tags. Those are structure, so they become
        // paragraph breaks. Other allowed tags are kept.
        text = text.Replace("\r\n", "\n");
        text = LineBreakTag().Replace(text, "\n");
        text = ParagraphEndTag().Replace(text, "\n\n");
        text = ParagraphStartTag().Replace(text, string.Empty);
        text = StripMarkdown(text);
        text = Sanitise(text);

        var paragraphs = ParagraphBreak().Split(text)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Take(Math.Max(1, maxParagraphs));

        return string.Concat(paragraphs.Select(p => IsBlock(p)
            ? p
            : $"<p>{p.Replace("\n", "<br />", StringComparison.Ordinal)}</p>"));
    }

    /// <summary>
    /// The plain text of a model's reply to a short text field: markup removed, entities decoded, and no markdown.
    /// A string property never holds HTML.
    /// </summary>
    public static string StripTags(string text)
        => WebUtility.HtmlDecode(AnyTag().Replace(text, string.Empty)).Trim();

    private static bool IsBlock(string paragraph)
        => BlockStarts.Any(start => paragraph.StartsWith(start, StringComparison.OrdinalIgnoreCase));

    // Keeps only the allowed tags, and escapes all text between them. The text is escaped rather than trusted,
    // so markup in the reply can only ever be one of the tags written below.
    private static string Sanitise(string text)
    {
        var sb = new StringBuilder();
        int last = 0;
        foreach (Match match in TagPattern().Matches(text))
        {
            sb.Append(EscapeText(text[last..match.Index]));
            sb.Append(SanitiseTag(match));
            last = match.Index + match.Length;
        }
        sb.Append(EscapeText(text[last..]));
        return sb.ToString();
    }

    private static string SanitiseTag(Match match)
    {
        string name = match.Groups["name"].Value.ToLowerInvariant();
        if (!AllowedTags.Contains(name))
            return string.Empty;

        // The close group always matches, empty for an opening tag, so test its length rather than whether it matched.
        if (match.Groups["close"].Length > 0)
            return $"</{name}>";

        if (name == "br")
            return "<br />";

        if (name == "a")
        {
            string? href = HrefOf(match.Groups["attributes"].Value);
            return href is null ? "<a>" : $"<a href=\"{WebUtility.HtmlEncode(href)}\">";
        }

        return $"<{name}>";
    }

    // The href of an anchor if it is safe to link to, otherwise null. A value with a colon must be http, https
    // or mailto, which rules out javascript:, data: and the other schemes that run or embed code.
    private static string? HrefOf(string attributes)
    {
        var match = HrefAttribute().Match(attributes);
        if (!match.Success)
            return null;

        string href = (match.Groups["double"].Success ? match.Groups["double"].Value
                       : match.Groups["single"].Success ? match.Groups["single"].Value
                       : match.Groups["bare"].Value).Trim();

        if (href.Length == 0)
            return null;

        if (!href.Contains(':', StringComparison.Ordinal))
            return href;

        return SafeScheme().IsMatch(href) ? href : null;
    }

    // Escapes markup characters in plain text, but leaves an existing entity such as &amp; alone so it isn't doubled.
    private static string EscapeText(string text)
        => LooseAmpersand().Replace(text, "&amp;")
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    // Models occasionally add light markdown despite the instruction; drop the common markers.
    /// <summary>
    /// Removes the markdown markers a model sometimes adds, despite being told not to.
    /// </summary>
    internal static string StripMarkdown(string text)
        => MarkdownMarkers().Replace(text, string.Empty).Trim();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex(@"</p\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEndTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"<p(\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphStartTag();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(\*\*|__|^#{1,6}\s+)", RegexOptions.Multiline)]
    private static partial Regex MarkdownMarkers();

    [GeneratedRegex(@"<(?<close>/?)(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attributes>\s[^<>]*)?/?>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"&(?!(#\d+|#x[0-9a-fA-F]+|[a-zA-Z][a-zA-Z0-9]*);)")]
    private static partial Regex LooseAmpersand();

    [GeneratedRegex(@"\bhref\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s""'>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex HrefAttribute();

    [GeneratedRegex(@"^(https?://|mailto:)", RegexOptions.IgnoreCase)]
    private static partial Regex SafeScheme();
}
