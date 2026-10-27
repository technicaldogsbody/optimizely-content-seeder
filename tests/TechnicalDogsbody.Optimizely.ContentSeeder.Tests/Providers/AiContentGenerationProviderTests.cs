using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
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

public class AiContentGenerationProviderTests
{
    public class FakeHero : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual string? CtaUrl { get; set; }
        public virtual XhtmlString? Body { get; set; }
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

    // Stub provider: no network — returns whatever the test's responder gives it and records requests.
    private sealed class StubProvider(
        BogusContentGenerationProvider fallback,
        ISeedDefinitionAccessor accessor,
        Func<AiContentRequest, Task<string?>> responder)
        : AiContentGenerationProvider(fallback, accessor, new XhtmlRichTextConverter(), NullLogger.Instance, new OptimizelyContentModelAdapter())
    {
        public List<AiContentRequest> Requests { get; } = [];
        protected override string ProviderName => "Stub";

        protected override Task<string?> GenerateJsonAsync(AiContentRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return responder(request);
        }

        protected override bool IsConfigurationError(Exception exception) => exception is UnauthorizedAccessException;
    }

    private static BogusContentGenerationProvider Bogus() => new(
        Mock.Of<IImageContentFactory>(),
        new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
        OptionsFactory.Create(new ContentSeederOptions()),
        NullLogger<BogusContentGenerationProvider>.Instance, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());

    private static StubProvider Provider(Func<AiContentRequest, Task<string?>> responder, AiWritingBrief? brief = null)
        => new(Bogus(), new Accessor { Current = brief is null ? null : new AiDefinition(brief) }, responder);

    private static IEnumerable<PropertyInfo> Props => typeof(FakeHero).GetProperties()
        .Where(p => p.Name is nameof(FakeHero.Heading) or nameof(FakeHero.CtaUrl) or nameof(FakeHero.Body));

    private static Task<string?> Reply(object value) => Task.FromResult<string?>(JsonSerializer.Serialize(value));

    [Fact]
    public async Task PopulateAsync_WritesProseFields_AndLeavesUrlToBogus()
    {
        var provider = Provider(_ => Reply(new { Heading = "Fresh roasts weekly", Body = "First paragraph.\n\nSecond paragraph." }));
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("Fresh roasts weekly", hero.Heading);
        Assert.Equal("<p>First paragraph.</p><p>Second paragraph.</p>", hero.Body!.ToString());
        Assert.StartsWith("http", hero.CtaUrl);
        Assert.DoesNotContain(provider.Requests.Single().Fields, f => f.Name == nameof(FakeHero.CtaUrl));
    }

    [Fact]
    public async Task PopulateAsync_RichText_DropsUnsafeTagsButKeepsTheirText()
    {
        var provider = Provider(_ => Reply(new { Heading = "x", Body = "<script>alert(1)</script>" }));
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("<p>alert(1)</p>", hero.Body!.ToString());
    }

    [Fact]
    public async Task PopulateAsync_ShortText_NeverContainsMarkup()
    {
        var provider = Provider(_ => Reply(new { Heading = "<b>Bold</b> &amp; bright", Body = "Plain." }));
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("Bold & bright", hero.Heading);
    }

    [Fact]
    public async Task PopulateAsync_RichText_IsCappedAtMaxParagraphs()
    {
        var brief = new AiWritingBrief { MaxParagraphs = 2 };
        var provider = Provider(_ => Reply(new { Heading = "x", Body = "One.\n\nTwo.\n\nThree." }), brief);
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("<p>One.</p><p>Two.</p>", hero.Body!.ToString());
    }

    [Fact]
    public async Task PopulateAsync_Brief_IsSentToTheModel()
    {
        var brief = new AiWritingBrief { Prompt = "A coffee roaster's shop.", ToneOfVoice = "Warm and plain-spoken.", AvoidPhrases = ["elevate"] }
            .ForContentType<FakeHero>(t => t.Prompt("Seasonal heroes.").Property(x => x.Heading, "Five words maximum."));
        var provider = Provider(_ => Reply(new { Heading = "x", Body = "y" }), brief);

        await provider.PopulateAsync(new FakeHero(), Props, "sv", typeof(FakeHero));

        var request = provider.Requests.Single();
        Assert.Contains("A coffee roaster's shop.", request.SystemPrompt);
        Assert.Contains("Warm and plain-spoken.", request.SystemPrompt);
        Assert.Contains("\"elevate\"", request.SystemPrompt);
        Assert.Contains("Seasonal heroes.", request.UserPrompt);
        Assert.Contains("Swedish (sv)", request.UserPrompt);
        Assert.Equal("Five words maximum.", request.Fields.Single(f => f.Name == nameof(FakeHero.Heading)).Guidance);
    }

    [Fact]
    public async Task PopulateAsync_BlankProperty_IsNotRequestedAndIsLeftBlank()
    {
        var brief = new AiWritingBrief().ForContentType<FakeHero>(t => t.Blank(x => x.Heading));
        var provider = Provider(_ => Reply(new { Body = "y" }), brief);
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.DoesNotContain(provider.Requests.Single().Fields, f => f.Name == nameof(FakeHero.Heading));
        Assert.True(string.IsNullOrEmpty(hero.Heading)); // Blank leaves the field blank, not Bogus
    }

    [Fact]
    public async Task PopulateAsync_GuidanceOnNonProseProperty_OptsItIn()
    {
        var brief = new AiWritingBrief().ForContentType<FakeHero>(t => t.Property(x => x.CtaUrl, "A relative path like /shop."));
        var provider = Provider(_ => Reply(new { Heading = "x", CtaUrl = "/shop", Body = "y" }), brief);
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("/shop", hero.CtaUrl);
    }

    [Fact]
    public async Task PopulateAsync_SchemaRequiresEveryFieldAndNoOthers()
    {
        var provider = Provider(_ => Reply(new { Heading = "x", Body = "y" }));

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        using var schema = JsonDocument.Parse(provider.Requests.Single().JsonSchema);
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["Heading", "Body"], schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Order().Reverse());
    }

    [Fact]
    public async Task PopulateAsync_ProviderFails_KeepsBogusValues()
    {
        var provider = Provider(_ => throw new HttpRequestException("network down"));
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.False(string.IsNullOrEmpty(hero.Heading));
        Assert.NotNull(hero.Body);
    }

    [Fact]
    public async Task PopulateAsync_InvalidJson_KeepsBogusValues()
    {
        var provider = Provider(_ => Task.FromResult<string?>("not json"));
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.False(string.IsNullOrEmpty(hero.Heading));
    }

    [Fact]
    public async Task PopulateAsync_ConfigurationError_StopsCallingTheModelForTheRestOfTheRun()
    {
        var provider = Provider(_ => throw new UnauthorizedAccessException("bad key"));

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task PopulateAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        var provider = Provider(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero), ct: cts.Token));
    }

    [Fact]
    public async Task PopulateAsync_PlainDefinition_UsesEmptyBrief()
    {
        var provider = new StubProvider(Bogus(), new Accessor { Current = Mock.Of<ISeedDefinition>() },
            _ => Reply(new { Heading = "x", Body = "y" }));

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.DoesNotContain("About the site", provider.Requests.Single().SystemPrompt);
    }
}
