using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using System.Reflection;
using Bogus;
using EPiServer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class BogusContentGenerationProviderTests
{
    // ── test content type ────────────────────────────────────────────────────

    private sealed class ArticlePage
    {
        public string? Title { get; set; }
        public string? Introduction { get; set; }
        public string? HeroImage { get; set; }
        public ContentArea? MainContentArea { get; set; }
        public int? WordCount { get; set; }
        public bool? IsPublished { get; set; }
        public DateTime? PublishDate { get; set; }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static BogusContentGenerationProvider CreateSut(
        IImageContentFactory? imageFactory = null,
        ContentSeederOptions? options = null,
        IBogusPropertyMap? propertyMap = null)
    {
        // Default: factory returns an empty handle so ContentReference image properties are skipped.
        var factory = imageFactory ?? Mock.Of<IImageContentFactory>(
            f => f.CreateImageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
                 == Task.FromResult(ContentHandle.Empty));

        var opts = OptionsFactory.Create(options ?? new ContentSeederOptions { MasterLanguage = "en" });
        var map = propertyMap ?? new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions()));

        return new BogusContentGenerationProvider(factory, map, opts, NullLogger<BogusContentGenerationProvider>.Instance, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
    }

    private static IEnumerable<PropertyInfo> GetProps<T>() => typeof(T).GetProperties();

    // ── string properties are populated ─────────────────────────────────────

    [Fact]
    public async Task PopulateAsync_StringProperty_IsNotNullOrWhiteSpaceAfterPopulate()
    {
        var content = new ArticlePage();
        var sut = CreateSut();

        await sut.PopulateAsync(content, GetProps<ArticlePage>(), "en");

        Assert.False(string.IsNullOrWhiteSpace(content.Title));
    }

    [Fact]
    public async Task PopulateAsync_IntroductionProperty_IsPopulated()
    {
        var content = new ArticlePage();
        var sut = CreateSut();

        await sut.PopulateAsync(content, GetProps<ArticlePage>(), "en");

        Assert.False(string.IsNullOrWhiteSpace(content.Introduction));
    }

    // ── ContentArea properties are skipped ───────────────────────────────────

    [Fact]
    public async Task PopulateAsync_ContentAreaProperty_IsLeftNull()
    {
        var content = new ArticlePage();
        var sut = CreateSut();

        await sut.PopulateAsync(content, GetProps<ArticlePage>(), "en");

        Assert.Null(content.MainContentArea);
    }

    // ── value type properties ─────────────────────────────────────────────────

    [Fact]
    public async Task PopulateAsync_IntProperty_IsPopulated()
    {
        var content = new ArticlePage();
        var sut = CreateSut();

        await sut.PopulateAsync(content, GetProps<ArticlePage>(), "en");

        Assert.NotNull(content.WordCount);
    }

    [Fact]
    public async Task PopulateAsync_BoolProperty_IsPopulated()
    {
        var content = new ArticlePage();
        var sut = CreateSut();

        await sut.PopulateAsync(content, GetProps<ArticlePage>(), "en");

        Assert.NotNull(content.IsPublished);
    }

    // ── unknown language fallback ─────────────────────────────────────────────

    [Fact]
    public async Task PopulateAsync_UnknownLanguage_FallsBackToEnglishWithoutThrowing()
    {
        var content = new ArticlePage();
        var sut = CreateSut();

        var ex = await Record.ExceptionAsync(
            () => sut.PopulateAsync(content, GetProps<ArticlePage>(), "xx-UNKNOWN"));

        Assert.Null(ex);
        Assert.False(string.IsNullOrWhiteSpace(content.Title));
    }

    // ── cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PopulateAsync_CancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.PopulateAsync(new ArticlePage(), GetProps<ArticlePage>(), "en", ct: cts.Token));
    }

    // ── empty property list ───────────────────────────────────────────────────

    [Fact]
    public async Task PopulateAsync_EmptyPropertyList_DoesNotThrow()
    {
        var sut = CreateSut();

        var ex = await Record.ExceptionAsync(
            () => sut.PopulateAsync(new ArticlePage(), [], "en"));

        Assert.Null(ex);
    }

    // ── heuristic map coverage ────────────────────────────────────────────────

    [Theory]
    [InlineData("Title")]
    [InlineData("Heading")]
    [InlineData("Summary")]
    [InlineData("Description")]
    [InlineData("Email")]
    [InlineData("Slug")]
    public void BogusHeuristicMap_NamedKeys_ProduceNonNullValue(string propertyName)
    {
        var faker = new Faker("en");

        var generator = BogusHeuristicMap.Map[propertyName];
        object result = generator(faker);

        Assert.NotNull(result);
    }

    [Fact]
    public void BogusHeuristicMap_Resolve_SuffixMatch_ReturnsGenerator()
    {
        // PageTitle has suffix "Title" which maps to Sentence(4)
        var faker = new Faker("en");

        var generator = BogusHeuristicMap.Resolve("PageTitle");

        Assert.NotNull(generator);
        Assert.NotNull(generator!(faker));
    }

    [Fact]
    public void BogusHeuristicMap_Resolve_UnknownName_ReturnsNull()
    {
        var result = BogusHeuristicMap.Resolve("ZzzzUnknownProperty99");

        Assert.Null(result);
    }
}
