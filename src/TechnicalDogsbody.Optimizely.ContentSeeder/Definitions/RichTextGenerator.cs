using System.Text;
using Bogus;


namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Writes the HTML for a rich text property that Bogus fills, from a <see cref="RichTextOptions"/>. Uses the faker it is
/// given, so the text follows the language and a seeded faker gives the same output each time.
/// </summary>
internal static class RichTextGenerator
{
    // A 1x1 transparent GIF, so the kitchen-sink image needs no network access.
    private const string PlaceholderImage = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    /// <summary>
    /// The rich text for one property: plain paragraphs with some words wrapped in the chosen tags, or the kitchen-sink block.
    /// </summary>
    internal static RichTextValue Build(Faker faker, RichTextOptions options)
        => new(options.IsKitchenSink ? KitchenSink(faker) : Paragraphs(faker, options));

    private static string Paragraphs(Faker faker, RichTextOptions options)
    {
        var html = new StringBuilder();
        for (int i = 0; i < options.ParagraphCount; i++)
        {
            var words = faker.Lorem.Paragraph().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tagged = words.Select(word => options.TagNames.Count > 0 && faker.Random.Bool((float)options.WordChance)
                ? Wrap(faker.Random.ArrayElement(options.TagNames.ToArray()), word)
                : word);

            html.Append("<p>").Append(string.Join(' ', tagged)).Append("</p>");
        }
        return html.ToString();
    }

    // Wraps one word in a tag. A link points at "#", since the seeded text has no real target.
    private static string Wrap(string tag, string word)
        => tag == "a"
            ? $"<a href=\"#\">{word}</a>"
            : $"<{tag}>{word}</{tag}>";

    // The block is fixed in its order, and only the words change, so a front end can be reviewed the same way each run.
    private static string KitchenSink(Faker faker)
    {
        string Sentence() => faker.Lorem.Sentence(8);
        string Word() => faker.Lorem.Word();

        var html = new StringBuilder();
        for (int level = 1; level <= 6; level++)
            html.Append($"<h{level}>{Sentence()}</h{level}>");

        html.Append($"<p>{Sentence()} <strong>{Word()}</strong> <em>{Word()}</em> <u>{Word()}</u> <s>{Word()}</s> "
            + $"H<sub>2</sub>O <sup>2</sup> <code>{Word()}</code> <a href=\"#\">{Word()}</a> {Sentence()}</p>");
        html.Append($"<p>{Sentence()}<br />{Sentence()}</p>");
        html.Append($"<pre>{Word()} {Word()}</pre>");
        html.Append($"<blockquote><p>{Sentence()}</p></blockquote>");
        html.Append($"<ul><li>{Sentence()}</li><li>{Sentence()}<ul><li>{Sentence()}</li></ul></li><li>{Sentence()}</li></ul>");
        html.Append($"<ol><li>{Sentence()}</li><li>{Sentence()}</li></ol>");
        html.Append("<hr />");
        html.Append($"<table><thead><tr><th>{Word()}</th><th>{Word()}</th></tr></thead>"
            + $"<tbody><tr><td>{Sentence()}</td><td>{Sentence()}</td></tr></tbody></table>");
        html.Append($"<p><img src=\"{PlaceholderImage}\" alt=\"{Word()}\" /></p>");
        return html.ToString();
    }
}
