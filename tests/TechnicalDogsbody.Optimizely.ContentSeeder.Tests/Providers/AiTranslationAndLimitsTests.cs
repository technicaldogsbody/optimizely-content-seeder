using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using System.Net;
using System.Reflection;
using System.Text.Json;
using EPiServer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Ai;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class AiTranslationAndLimitsTests
{
    public class Hero : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual string? ButtonText { get; set; }
        public virtual string? CtaUrl { get; set; }
        public virtual XhtmlString? Body { get; set; }
        public virtual int Rank { get; set; }
        public virtual DateTime Published { get; set; }
    }

    private sealed class Accessor : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; }
    }

    private sealed class AiDefinition(AiWritingBrief brief) : IAiSeedDefinition
    {
        public string Id => "ai-def";
        public SeedTree Build() => new();
        public AiWritingBrief WritingBrief => brief;
    }

    private sealed class StubProvider(
        BogusContentGenerationProvider fallback,
        ISeedDefinitionAccessor accessor,
        Func<AiContentRequest, Task<string?>> responder)
        : AiContentGenerationProvider(fallback, accessor, new XhtmlRichTextConverter(), NullLogger.Instance, new OptimizelyContentModelAdapter())
    {
        public List<AiContentRequest> Requests { get; } = [];
        protected override string ProviderName => "Stub";
        protected override string? ModelName => "stub-1";

        protected override Task<string?> GenerateJsonAsync(AiContentRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return responder(request);
        }

        protected override bool IsConfigurationError(Exception exception) => exception is UnauthorizedAccessException;
    }

    private static StubProvider Provider(Func<AiContentRequest, Task<string?>> responder, AiWritingBrief? brief = null)
    {
        var bogus = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions()),
            NullLogger<BogusContentGenerationProvider>.Instance, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
        return new StubProvider(bogus, new Accessor { Current = brief is null ? null : new AiDefinition(brief) }, responder);
    }

    private static IEnumerable<PropertyInfo> Props(params string[] names) => typeof(Hero).GetProperties().Where(p => names.Contains(p.Name));

    private static Task<string?> Reply(object value) => Task.FromResult<string?>(JsonSerializer.Serialize(value));

    private static Hero Master() => new()
    {
        Heading = "Find your next adventure",
        ButtonText = "Shop boots",
        CtaUrl = "https://example.com/boots",
        Body = new XhtmlString("<p>Great boots for every trail.</p><p>Built to last.</p>"),
        Rank = 7,
        Published = new DateTime(2026, 3, 1),
    };

    private static readonly string[] AllProps = [nameof(Hero.Heading), nameof(Hero.ButtonText), nameof(Hero.CtaUrl), nameof(Hero.Body), nameof(Hero.Rank), nameof(Hero.Published)];

    // ── translation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TranslateAsync_SendsTheMasterTextAndBothLanguages()
    {
        var provider = Provider(_ => Reply(new { Heading = "Hitta ditt nästa äventyr", ButtonText = "Köp kängor", Body = "Bra kängor för varje led.\n\nByggda för att hålla." }));

        await provider.TranslateAsync(Master(), new Hero(), Props(AllProps), "en", "sv", typeof(Hero));

        var request = Assert.Single(provider.Requests);
        Assert.Contains("translator", request.SystemPrompt);
        Assert.Contains("English (en)", request.SystemPrompt);
        Assert.Contains("Swedish (sv)", request.SystemPrompt);
        Assert.Contains("Find your next adventure", request.UserPrompt);
        Assert.Contains("Shop boots", request.UserPrompt);
        // Rich text is sent as its HTML, so the translation keeps the formatting.
        Assert.Contains("<p>Great boots for every trail.</p>", request.UserPrompt);
        Assert.Contains("Rich text fields hold HTML", request.UserPrompt);
        Assert.DoesNotContain(request.Fields, f => f.Name == nameof(Hero.CtaUrl));
    }

    [Fact]
    public async Task TranslateAsync_AppliesTheTranslation_AndSharesNonProseValuesWithTheMaster()
    {
        var provider = Provider(_ => Reply(new { Heading = "Hitta ditt nästa äventyr", ButtonText = "Köp kängor", Body = "Bra kängor för varje led.\n\nByggda för att hålla." }));
        var target = new Hero();

        await provider.TranslateAsync(Master(), target, Props(AllProps), "en", "sv", typeof(Hero));

        Assert.Equal("Hitta ditt nästa äventyr", target.Heading);
        Assert.Equal("Köp kängor", target.ButtonText);
        Assert.Equal("<p>Bra kängor för varje led.</p><p>Byggda för att hålla.</p>", target.Body!.ToString());
        // Same URL, number and date in every language — not regenerated.
        Assert.Equal("https://example.com/boots", target.CtaUrl);
        Assert.Equal(7, target.Rank);
        Assert.Equal(new DateTime(2026, 3, 1), target.Published);
    }

    [Fact]
    public async Task TranslateAsync_WhenTheTranslationFails_FallsBackToPlaceholderNotTheMastersText()
    {
        var provider = Provider(_ => throw new HttpRequestException("busy", null, HttpStatusCode.ServiceUnavailable));
        var master = Master();
        var target = new Hero();

        await provider.TranslateAsync(master, target, Props(AllProps), "en", "sv", typeof(Hero));

        Assert.False(string.IsNullOrWhiteSpace(target.Heading));
        Assert.NotEqual(master.Heading, target.Heading);
        Assert.NotNull(target.Body);
    }

    [Fact]
    public async Task TranslateAsync_ConfigurationError_StopsAskingForTheRestOfTheRun()
    {
        var provider = Provider(_ => throw new UnauthorizedAccessException("bad key"));

        await provider.TranslateAsync(Master(), new Hero(), Props(AllProps), "en", "sv", typeof(Hero));
        await provider.TranslateAsync(Master(), new Hero(), Props(AllProps), "en", "sv", typeof(Hero));

        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task TranslateAsync_FieldsEmptyInTheMaster_AreNotSent()
    {
        var provider = Provider(_ => Reply(new { Heading = "Hitta ditt nästa äventyr" }));
        var master = new Hero { Heading = "Find your next adventure" };

        await provider.TranslateAsync(master, new Hero(), Props(nameof(Hero.Heading), nameof(Hero.ButtonText), nameof(Hero.Body)), "en", "sv", typeof(Hero));

        Assert.Equal([nameof(Hero.Heading)], provider.Requests.Single().Fields.Select(f => f.Name));
    }

    [Fact]
    public async Task TranslateAsync_BlankProperty_StaysBlankInTheTranslation()
    {
        var brief = new AiWritingBrief().ForContentType<Hero>(t => t.Blank(x => x.Heading));
        var provider = Provider(_ => Reply(new { ButtonText = "Köp kängor", Body = "x" }), brief);
        var target = new Hero();

        await provider.TranslateAsync(Master(), target, Props(AllProps), "en", "sv", typeof(Hero));

        Assert.True(string.IsNullOrEmpty(target.Heading));
        Assert.DoesNotContain(provider.Requests.Single().Fields, f => f.Name == nameof(Hero.Heading));
    }

    [Fact]
    public async Task TranslateAsync_NoWritableTextProperties_MakesNoRequest()
    {
        var provider = Provider(_ => Reply(new { }));

        await provider.TranslateAsync(Master(), new Hero(), Props(nameof(Hero.Rank), nameof(Hero.CtaUrl)), "en", "sv", typeof(Hero));

        Assert.Empty(provider.Requests);
    }

    [Fact]
    public void ProviderWithTranslation_IsRecognisedAsATranslationProvider()
    {
        Assert.IsAssignableFrom<IContentTranslationProvider>(Provider(_ => Reply(new { })));
    }

    // ── language names ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("sv", "Swedish (sv)")]
    [InlineData("en", "English (en)")]
    [InlineData("de", "German (de)")]
    public void LanguageLabel_NamesTheLanguage(string tag, string expected)
    {
        Assert.Equal(expected, AiPromptBuilder.LanguageLabel(tag));
    }

    [Fact]
    public void LanguageLabel_UnknownTag_StillTellsTheModelTheTag()
    {
        Assert.Contains("zz-not-a-language", AiPromptBuilder.LanguageLabel("zz-not-a-language"));
    }

    [Fact]
    public async Task PopulateAsync_AsksForEveryFieldInTheNamedLanguage()
    {
        var provider = Provider(_ => Reply(new { Heading = "x" }));

        await provider.PopulateAsync(new Hero(), Props(nameof(Hero.Heading)), "sv", typeof(Hero));

        var prompt = provider.Requests.Single().UserPrompt;
        Assert.Contains("Swedish (sv) only", prompt);
    }

    // ── limits ───────────────────────────────────────────────────────────────

    private static AiWritingBrief ButtonLimit(int maxWords) => new AiWritingBrief()
        .ForContentType<Hero>(t => t.Property(x => x.ButtonText, "Two or three words.", maxWords: maxWords));

    [Fact]
    public async Task Prompt_StatesTheLimitAsARequirement_NotBesideTheGenericLengthRule()
    {
        var provider = Provider(_ => Reply(new { ButtonText = "Shop boots" }), ButtonLimit(3));

        await provider.PopulateAsync(new Hero(), Props(nameof(Hero.ButtonText)), "en", typeof(Hero));

        var request = provider.Requests.Single();
        Assert.Contains("Two or three words.", request.UserPrompt);
        Assert.Contains("At most 3 words.", request.UserPrompt);
        Assert.Contains("mandatory", request.UserPrompt);
        // The generic "one or two sentences" rule must not contradict the field's own requirement.
        Assert.DoesNotContain("one or two sentences", request.UserPrompt);
    }

    [Fact]
    public async Task Limit_RespectedByTheReply_NeedsNoSecondRequest()
    {
        var provider = Provider(_ => Reply(new { ButtonText = "Shop boots now" }), ButtonLimit(3));
        var hero = new Hero();

        await provider.PopulateAsync(hero, Props(nameof(Hero.ButtonText)), "en", typeof(Hero));

        Assert.Equal("Shop boots now", hero.ButtonText);
        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task Limit_BrokenByTheReply_IsSentBackOnceToBeShortened()
    {
        int call = 0;
        var provider = Provider(_ => ++call == 1
            ? Reply(new { ButtonText = "Discover our latest range of walking boots today" })
            : Reply(new { ButtonText = "Shop boots" }), ButtonLimit(3));
        var hero = new Hero();

        await provider.PopulateAsync(hero, Props(nameof(Hero.ButtonText)), "en", typeof(Hero));

        Assert.Equal("Shop boots", hero.ButtonText);
        Assert.Equal(2, provider.Requests.Count);
        var retry = provider.Requests[1];
        Assert.Contains("Discover our latest range of walking boots today", retry.UserPrompt);
        Assert.Contains("At most 3 words.", retry.UserPrompt);
    }

    [Fact]
    public async Task Limit_StillBrokenAfterTheRetry_IsTrimmedToFit()
    {
        var provider = Provider(_ => Reply(new { ButtonText = "Discover our latest range of walking boots, today." }), ButtonLimit(3));
        var hero = new Hero();

        await provider.PopulateAsync(hero, Props(nameof(Hero.ButtonText)), "en", typeof(Hero));

        Assert.Equal("Discover our latest", hero.ButtonText);
        Assert.Equal(2, provider.Requests.Count); // the original request and one retry, never more
    }

    [Fact]
    public async Task Limit_AlsoAppliesToTranslations()
    {
        var provider = Provider(_ => Reply(new { ButtonText = "Köp våra bästa vandringskängor idag" }), ButtonLimit(3));
        var target = new Hero();

        await provider.TranslateAsync(new Hero { ButtonText = "Shop boots" }, target, Props(nameof(Hero.ButtonText)), "en", "sv", typeof(Hero));

        Assert.True(target.ButtonText!.Split(' ').Length <= 3);
    }

    [Fact]
    public async Task Limit_DoesNotRetry_WhenTheModelFailsOrAiIsSwitchedOff()
    {
        var provider = Provider(_ => throw new UnauthorizedAccessException("bad key"), ButtonLimit(3));

        await provider.PopulateAsync(new Hero(), Props(nameof(Hero.ButtonText)), "en", typeof(Hero));

        Assert.Single(provider.Requests);
    }

    [Theory]
    [InlineData("Shop boots now", 3, null, false)]
    [InlineData("Shop all boots now", 3, null, true)]
    [InlineData("Boots", null, 5, false)]
    [InlineData("Walking boots", null, 5, true)]
    [InlineData("anything at all", null, null, false)]
    public void Exceeds_ChecksWordsAndCharacters(string text, int? maxWords, int? maxCharacters, bool expected)
    {
        var field = new AiTextField("Heading", AiTextFieldKind.ShortText, null, MaxWords: maxWords, MaxCharacters: maxCharacters);

        Assert.Equal(expected, AiTextFormatting.Exceeds(field, text));
    }

    [Theory]
    [InlineData("Discover our latest range", 3, null, "Discover our latest")]
    [InlineData("Discover, our latest, range", 2, null, "Discover, our")]
    [InlineData("Discover our latest range", null, 18, "Discover our")]
    [InlineData("Short", 3, 50, "Short")]
    public void TrimToLimit_CutsAtAWordBoundaryWithoutDanglingPunctuation(string text, int? maxWords, int? maxCharacters, string expected)
    {
        var field = new AiTextField("Heading", AiTextFieldKind.ShortText, null, MaxWords: maxWords, MaxCharacters: maxCharacters);

        Assert.Equal(expected, AiTextFormatting.TrimToLimit(text, field));
    }

    [Fact]
    public async Task Limit_OnRichText_IsOnlyARequirementNotEnforced()
    {
        var brief = new AiWritingBrief().ForContentType<Hero>(t => t.Property(x => x.Body, "Brief.", maxWords: 2));
        var provider = Provider(_ => Reply(new { Body = "This is a rather long paragraph indeed." }), brief);
        var hero = new Hero();

        await provider.PopulateAsync(hero, Props(nameof(Hero.Body)), "en", typeof(Hero));

        Assert.Single(provider.Requests);
        Assert.Contains("rather long paragraph", hero.Body!.ToString());
        Assert.Contains("At most 2 words.", provider.Requests.Single().UserPrompt);
    }

    // ── typed limits on the brief ────────────────────────────────────────────

    [Fact]
    public void Brief_LimitsAreResolved_WithTheDerivedTypeWinning()
    {
        var brief = new AiWritingBrief()
            .ForContentType<BlockData>(t => { })
            .ForContentType<Hero>(t => t.Property(x => x.Heading, "Short.", maxWords: 5, maxCharacters: 40));

        var resolved = brief.Resolve(typeof(Hero));

        Assert.Equal(new PropertyLimit(5, 40), resolved.PropertyLimits[nameof(Hero.Heading)]);
    }

    [Fact]
    public void Brief_PropertyWithoutLimits_HasNoLimit()
    {
        var brief = new AiWritingBrief().ForContentType<Hero>(t => t.Property(x => x.Heading, "Short."));

        Assert.False(brief.Resolve(typeof(Hero)).PropertyLimits.ContainsKey(nameof(Hero.Heading)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(null, 0)]
    public void Brief_InvalidLimits_Throw(int? maxWords, int? maxCharacters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AiWritingBrief().ForContentType<Hero>(t => t.Property(x => x.Heading, "Short.", maxWords, maxCharacters)));
    }

    // ── describing the provider ──────────────────────────────────────────────

    [Fact]
    public void Describe_ReportsTheProviderAndModelAsAi()
    {
        var provider = Provider(_ => Reply(new { }));

        Assert.Equal(new ProviderInfo("Stub", "stub-1", IsAi: true), provider.Describe());
    }

    [Fact]
    public void BogusProvider_DescribesItselfAsPlaceholderData()
    {
        var bogus = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions()),
            NullLogger<BogusContentGenerationProvider>.Instance, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());

        var info = bogus.Describe();

        Assert.Equal("Bogus", info.Name);
        Assert.False(info.IsAi);
    }
}
