namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// How Bogus fills a rich text (<c>XhtmlString</c>) property. Set through <see cref="ContentTypeGenerators{T}.RichText"/>.
/// <para>
/// By default a property gets one plain paragraph. <see cref="Tags"/> adds markup at random: some words of the text are
/// wrapped in one of the tags named, so a page shows bold, links and so on. <see cref="KitchenSink"/> instead writes a
/// fixed block that uses every common editor tag, in a set order, for checking how a front end renders them.
/// </para>
/// <para>
/// These options apply only to Bogus. The AI provider writes its own formatting, and it leaves a property marked
/// <see cref="KitchenSink"/> alone.
/// </para>
/// </summary>
public sealed class RichTextOptions
{
    // Inline tags that can wrap a single word without breaking the paragraph around it.
    private static readonly string[] InlineTags = ["strong", "b", "em", "i", "u", "s", "sub", "sup", "code", "mark", "a"];

    private readonly List<string> _tags = [];

    /// <summary>The inline tags that may wrap words, in lower case.</summary>
    internal IReadOnlyList<string> TagNames => _tags;

    /// <summary>The chance that each word is wrapped in a tag, from 0 to 1.</summary>
    internal double WordChance { get; private set; } = 0.15;

    /// <summary>The number of paragraphs written when not in kitchen-sink mode.</summary>
    internal int ParagraphCount { get; private set; } = 1;

    /// <summary>True when the fixed kitchen-sink block is written instead of plain text.</summary>
    internal bool IsKitchenSink { get; private set; }

    /// <summary>
    /// The tags that may be added at random to words. Allowed: strong, b, em, i, u, s, sub, sup, code, mark and a.
    /// Each word that is picked gets one of them, chosen at random. Calls add to the list.
    /// </summary>
    /// <exception cref="ArgumentException">A tag is empty or not one of the inline tags above.</exception>
    public RichTextOptions Tags(params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        foreach (string tag in tags)
        {
            string name = tag?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!InlineTags.Contains(name, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"'{tag}' cannot wrap a word. Use one of: {string.Join(", ", InlineTags)}.", nameof(tags));

            if (!_tags.Contains(name, StringComparer.Ordinal))
                _tags.Add(name);
        }
        return this;
    }

    /// <summary>
    /// The chance that each word is wrapped in a tag, from 0 (never) to 1 (every word). Default: 0.15.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The chance is outside 0 to 1.</exception>
    public RichTextOptions Probability(double chance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chance, 0d);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chance, 1d);
        WordChance = chance;
        return this;
    }

    /// <summary>
    /// The number of paragraphs, each a sentence-based paragraph of lorem text. Default: 1.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The count is less than 1.</exception>
    public RichTextOptions Paragraphs(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ParagraphCount = count;
        return this;
    }

    /// <summary>
    /// Writes a fixed block that uses every common editor tag, in a set order: headings h1 to h6, paragraphs with the
    /// inline tags, a line break, pre, blockquote, bulleted and numbered lists (one nested), a rule, a table and an image.
    /// The text is lorem, so it follows the language, but the structure is the same on every run.
    /// </summary>
    public RichTextOptions KitchenSink()
    {
        IsKitchenSink = true;
        return this;
    }
}
