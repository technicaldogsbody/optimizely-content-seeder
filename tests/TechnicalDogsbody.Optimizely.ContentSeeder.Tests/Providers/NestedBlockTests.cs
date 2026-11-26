using System.Reflection;
using System.Text.Json;
using EPiServer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Ai;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

// Local blocks (a single block property) and images and AI text inside local blocks and block list items.
public class NestedBlockTests
{
    public class CardBlock : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual ContentReference? Image { get; set; }
    }

    public class HeroBlock : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual ContentReference? Image { get; set; }
    }

    public class BannerBlock : BlockData
    {
        [EPiServer.DataAnnotations.CultureSpecific]
        public virtual string? Heading { get; set; }

        public virtual string? Note { get; set; }
    }

    public class BannerPage
    {
        public BannerBlock? Banner { get; set; }
        public IList<BannerBlock>? Banners { get; set; }
    }

    public class Page
    {
        public string? Title { get; set; }
        public HeroBlock? Hero { get; set; }
        public IList<CardBlock>? Cards { get; set; }
    }

    private static readonly PropertyInfo[] PageProps = typeof(Page).GetProperties();

    private static BogusContentGenerationProvider Bogus(IImageContentFactory? images = null)
        => new(
            images ?? Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            NullLogger<BogusContentGenerationProvider>.Instance,
            richText: new XhtmlRichTextConverter(),
            contentModel: new OptimizelyContentModelAdapter());

    // ── local blocks ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalBlock_IsFilledInPlace()
    {
        var hero = new HeroBlock();
        var page = new Page { Hero = hero };

        await Bogus().PopulateAsync(page, PageProps, "en");

        Assert.Same(hero, page.Hero);
        Assert.False(string.IsNullOrWhiteSpace(hero.Heading));
    }

    [Fact]
    public async Task LocalBlock_ThatIsMissing_IsCreated()
    {
        var page = new Page();

        await Bogus().PopulateAsync(page, PageProps, "en");

        Assert.NotNull(page.Hero);
        Assert.False(string.IsNullOrWhiteSpace(page.Hero!.Heading));
    }

    [Fact]
    public async Task LocalBlock_InALanguageVersion_GetsOnlyItsCultureSpecificProperties()
    {
        var page = new BannerPage { Banner = new BannerBlock() };

        using (LanguageVersionScope.Enter())
            await Bogus().PopulateAsync(page, typeof(BannerPage).GetProperties(), "sv");

        Assert.False(string.IsNullOrWhiteSpace(page.Banner!.Heading));
        Assert.Null(page.Banner.Note);
        // A block list belongs to the language version as a whole, so its items are filled completely.
        Assert.All(page.Banners!, b => Assert.False(string.IsNullOrWhiteSpace(b.Note)));
    }

    [Fact]
    public async Task LocalBlock_InTheMaster_GetsEveryProperty()
    {
        var page = new BannerPage { Banner = new BannerBlock() };

        await Bogus().PopulateAsync(page, typeof(BannerPage).GetProperties(), "en");

        Assert.False(string.IsNullOrWhiteSpace(page.Banner!.Note));
    }

    // ── images by path ───────────────────────────────────────────────────────

    [Fact]
    public async Task ImageSizes_NamedByPath_ReachLocalBlocksAndListItems()
    {
        int next = 100;
        var images = new Mock<IImageContentFactory>();
        images.Setup(f => f.CreateImageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => new ContentHandle((next++).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var page = new Page();
        var sizes = new Dictionary<string, (int, int)>
        {
            ["Hero.Image"] = (1600, 600),
            ["Cards.Image"] = (400, 300),
        };

        await Bogus(images.Object).PopulateAsync(page, PageProps, "en", typeof(Page), sizes);

        Assert.False(ContentReference.IsNullOrEmpty(page.Hero!.Image));
        Assert.All(page.Cards!, c => Assert.False(ContentReference.IsNullOrEmpty(c.Image)));
        images.Verify(f => f.CreateImageAsync(1600, 600, It.IsAny<CancellationToken>()), Times.Once);
        images.Verify(f => f.CreateImageAsync(400, 300, It.IsAny<CancellationToken>()), Times.Exactly(page.Cards!.Count));
    }

    [Fact]
    public async Task ImageSizes_WithoutAPath_DoNotReachNestedBlocks()
    {
        var images = new Mock<IImageContentFactory>();
        images.Setup(f => f.CreateImageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContentHandle("5"));

        var page = new Page();
        await Bogus(images.Object).PopulateAsync(page, PageProps, "en", typeof(Page),
            new Dictionary<string, (int, int)> { ["Image"] = (10, 10) });

        Assert.Null(page.Hero!.Image);
    }

    [Fact]
    public void ChildMap_StripsThePropertyName()
    {
        var map = new Dictionary<string, int> { ["Hero.Image"] = 1, ["Hero.Card.Image"] = 2, ["HeroImage"] = 3, ["Cards.Image"] = 4 };

        var child = BogusContentGenerationProvider.ChildMap(map, "Hero");

        Assert.Equal(new Dictionary<string, int> { ["Image"] = 1, ["Card.Image"] = 2 }, child);
        Assert.Null(BogusContentGenerationProvider.ChildMap(map, "Other"));
    }

    // ── AI ───────────────────────────────────────────────────────────────────

    private sealed class Accessor : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; }
    }

    private sealed class StubProvider(BogusContentGenerationProvider fallback, Func<AiContentRequest, string> responder)
        : AiContentGenerationProvider(fallback, new Accessor(), new XhtmlRichTextConverter(), NullLogger.Instance, new OptimizelyContentModelAdapter())
    {
        public List<AiContentRequest> Requests { get; } = [];
        protected override string ProviderName => "Stub";

        protected override Task<string?> GenerateJsonAsync(AiContentRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult<string?>(responder(request));
        }
    }

    // Answers every field with its own name, so each value shows which field it was written into.
    private static string EchoFieldNames(AiContentRequest request)
        => JsonSerializer.Serialize(request.Fields.ToDictionary(f => f.Name, f => "AI " + f.Name));

    [Fact]
    public async Task Ai_WritesNestedText_InTheSameRequest()
    {
        var provider = new StubProvider(Bogus(), EchoFieldNames);
        var page = new Page();

        await provider.PopulateAsync(page, PageProps, "en", typeof(Page));

        var request = Assert.Single(provider.Requests);
        Assert.Contains(request.Fields, f => f.Name == "Title");
        Assert.Contains(request.Fields, f => f.Name == "Hero_Heading");
        Assert.Contains(request.Fields, f => f.Name == "Cards_1_Heading");

        Assert.Equal("AI Title", page.Title);
        Assert.Equal("AI Hero_Heading", page.Hero!.Heading);
        for (int i = 0; i < page.Cards!.Count; i++)
            Assert.Equal($"AI Cards_{i + 1}_Heading", page.Cards[i].Heading);
    }

    [Fact]
    public async Task Ai_Translation_KeepsTheMastersItems_AndTranslatesTheirText()
    {
        var master = new Page
        {
            Title = "Trail shop",
            Hero = new HeroBlock { Heading = "Big hero", Image = new ContentReference(42) },
            Cards = [new CardBlock { Heading = "First", Image = new ContentReference(7) }, new CardBlock { Heading = "Second" }],
        };
        var provider = new StubProvider(Bogus(), request => JsonSerializer.Serialize(
            request.Fields.ToDictionary(f => f.Name, f => "sv " + f.SourceText)));
        var target = new Page();

        await provider.TranslateAsync(master, target, PageProps, "en", "sv", typeof(Page));

        var request = Assert.Single(provider.Requests);
        Assert.Contains(request.Fields, f => f.Name == "Cards_2_Heading" && f.SourceText == "Second");

        Assert.Equal("sv Trail shop", target.Title);
        Assert.Equal("sv Big hero", target.Hero!.Heading);
        Assert.Equal(new ContentReference(42), target.Hero.Image);
        Assert.Equal(2, target.Cards!.Count);
        Assert.Equal("sv First", target.Cards[0].Heading);
        Assert.Equal("sv Second", target.Cards[1].Heading);
        Assert.Equal(new ContentReference(7), target.Cards[0].Image);
        // New blocks, not the master's own instances.
        Assert.NotSame(master.Cards[0], target.Cards[0]);
        Assert.NotSame(master.Hero, target.Hero);
    }
}
