using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using System.Reflection;
using Bogus;
using EPiServer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Definitions;

public class RichTextRulesTests
{
    public class RichHero : BlockData
    {
        public virtual string? Title { get; set; }
        public virtual XhtmlString? Body { get; set; }
    }

    private static Faker SeededFaker(int seed) => new("en") { Random = new Randomizer(seed) };

    private static string Generate(Action<RichTextOptions> configure, int seed = 1)
    {
        var generators = new ContentGenerators()
            .ForContentType<RichHero>(t => t.RichText(x => x.Body, configure));

        var rule = generators.Resolve(typeof(RichHero), nameof(RichHero.Body))!;
        return ((RichTextValue)rule(SeededFaker(seed))).Html;
    }

    [Fact]
    public void Default_IsOneParagraphWithNoTags()
    {
        string html = Generate(_ => { });

        Assert.StartsWith("<p>", html);
        Assert.EndsWith("</p>", html);
        Assert.Equal(1, CountOccurrences(html, "<p>"));
        Assert.DoesNotContain("<strong>", html);
    }

    [Fact]
    public void Paragraphs_WritesThatManyParagraphs()
    {
        string html = Generate(o => o.Paragraphs(3));

        Assert.Equal(3, CountOccurrences(html, "<p>"));
    }

    [Fact]
    public void Tags_WithZeroProbability_AddsNoMarkup()
    {
        string html = Generate(o => o.Tags("strong", "em").Probability(0));

        Assert.DoesNotContain("<strong>", html);
        Assert.DoesNotContain("<em>", html);
    }

    [Fact]
    public void Tags_WithFullProbability_WrapsEveryWordInAChosenTag()
    {
        string html = Generate(o => o.Tags("strong").Probability(1));

        // Every word sits inside its own <strong>, so the paragraph starts and ends with one.
        Assert.StartsWith("<p><strong>", html);
        Assert.EndsWith("</strong></p>", html);
    }

    [Fact]
    public void Tags_OnlyUsesTheTagsChosen()
    {
        string html = Generate(o => o.Tags("em", "code").Probability(1), seed: 7);

        Assert.DoesNotContain("<strong>", html);
        Assert.DoesNotContain("<a ", html);
        Assert.True(html.Contains("<em>") || html.Contains("<code>"));
    }

    [Fact]
    public void Tags_LinkTagGetsAPlaceholderHref()
    {
        string html = Generate(o => o.Tags("a").Probability(1));

        Assert.Contains("<a href=\"#\">", html);
    }

    [Fact]
    public void Tags_SameSeedGivesTheSameOutput()
    {
        Assert.Equal(
            Generate(o => o.Tags("strong", "em").Probability(0.3), seed: 99),
            Generate(o => o.Tags("strong", "em").Probability(0.3), seed: 99));
    }

    [Theory]
    [InlineData("script")]
    [InlineData("div")]
    [InlineData("p")]
    [InlineData("img")]
    public void Tags_RejectsATagThatCannotWrapAWord(string tag)
    {
        Assert.Throws<ArgumentException>(() => new RichTextOptions().Tags(tag));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Probability_OutsideZeroToOne_Throws(double chance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RichTextOptions().Probability(chance));
    }

    [Fact]
    public void KitchenSink_WritesEveryCommonTagInAFixedOrder()
    {
        string html = Generate(o => o.KitchenSink());

        string[] inOrder =
        [
            "<h1>", "<h6>", "<strong>", "<em>", "<u>", "<s>", "<sub>", "<sup>", "<code>", "<a href=\"#\">",
            "<br />", "<pre>", "<blockquote>", "<ul>", "<ol>", "<hr />", "<table>", "<th>", "<td>", "<img ",
        ];

        int position = -1;
        foreach (string tag in inOrder)
        {
            int next = html.IndexOf(tag, position + 1, StringComparison.Ordinal);
            Assert.True(next > position, $"'{tag}' is missing or out of order.");
            position = next;
        }
    }

    [Fact]
    public void KitchenSink_HasTheSameStructureOnEveryRun()
    {
        // Only the words change between seeds, so the tags are the same.
        string tagsA = TagSequence(Generate(o => o.KitchenSink(), seed: 1));
        string tagsB = TagSequence(Generate(o => o.KitchenSink(), seed: 2));

        Assert.Equal(tagsA, tagsB);
    }

    [Fact]
    public void KitchenSink_IsMarkedSoTheAiProviderLeavesItAlone()
    {
        var generators = new ContentGenerators().ForContentType<RichHero>(t => t.KitchenSink(x => x.Body));

        Assert.True(generators.IsKitchenSink(typeof(RichHero), nameof(RichHero.Body)));
    }

    [Fact]
    public void APlainRuleAfterKitchenSink_ClearsTheMark()
    {
        var generators = new ContentGenerators().ForContentType<RichHero>(t => t
            .KitchenSink(x => x.Body)
            .Property(x => x.Body, f => new XhtmlString("<p>plain</p>")));

        Assert.False(generators.IsKitchenSink(typeof(RichHero), nameof(RichHero.Body)));
    }

    [Fact]
    public async Task RichText_OnAStringProperty_IsSkippedWhenSeeded()
    {
        // The definition is accepted; the provider checks the property when it seeds, and leaves it unset.
        var generators = new ContentGenerators().ForContentType<RichHero>(t => t.RichText(x => x.Title, o => o.Paragraphs(1)));
        var provider = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions()),
            NullLogger<BogusContentGenerationProvider>.Instance,
            new RuleAccessor(generators), richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
        var hero = new RichHero();

        await provider.PopulateAsync(hero, typeof(RichHero).GetProperties(), "en", typeof(RichHero));

        Assert.Null(hero.Title);
    }

    private sealed class RuleAccessor(ContentGenerators generators) : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; } = new RuleDefinition(generators);
    }

    private sealed class RuleDefinition(ContentGenerators generators) : ISeedDefinition
    {
        public string Id => "rich-text-rules";
        public ContentGenerators? Generators => generators;
        public SeedTree Build() => new();
    }

    private static string TagSequence(string html)
        => string.Concat(System.Text.RegularExpressions.Regex.Matches(html, "<[a-z0-9]+").Select(m => m.Value));

    private static int CountOccurrences(string text, string value)
        => text.Split(value).Length - 1;
}
