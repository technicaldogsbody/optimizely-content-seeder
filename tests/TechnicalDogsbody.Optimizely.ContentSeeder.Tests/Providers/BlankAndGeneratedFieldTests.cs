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

/// <summary>
/// The two "blank or Bogus" choices: <c>Blank</c> leaves a field empty, and <c>UseGenerator</c> has Bogus fill it under the
/// AI provider. Also the generator-level <c>Blank</c>, which leaves a field empty under Bogus.
/// </summary>
public class BlankAndGeneratedFieldTests
{
    public class BlankBlock : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual string? Summary { get; set; }
        public virtual int Count { get; set; }
    }

    private sealed class Accessor : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; }
    }

    private sealed class BriefDefinition(AiWritingBrief brief) : IAiSeedDefinition
    {
        public string Id => "blank-fields";
        public AiWritingBrief WritingBrief => brief;
        public SeedTree Build() => new();
    }

    private sealed class RuleDefinition(ContentGenerators generators) : ISeedDefinition
    {
        public string Id => "blank-rules";
        public ContentGenerators? Generators => generators;
        public SeedTree Build() => new();
    }

    private sealed class StubAi(BogusContentGenerationProvider fallback, ISeedDefinitionAccessor accessor, Func<AiContentRequest, Task<string?>> respond)
        : AiContentGenerationProvider(fallback, accessor, new XhtmlRichTextConverter(), NullLogger.Instance, new OptimizelyContentModelAdapter())
    {
        public List<AiContentRequest> Requests { get; } = [];
        protected override string ProviderName => "Stub";

        protected override Task<string?> GenerateJsonAsync(AiContentRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return respond(request);
        }
    }

    private static StubAi Ai(AiWritingBrief brief, ContentGenerators? generators, Func<AiContentRequest, Task<string?>> respond)
    {
        var accessor = new Accessor
        {
            Current = generators is null ? new BriefDefinition(brief) : new BriefWithRules(brief, generators),
        };
        var fallback = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            NullLogger<BogusContentGenerationProvider>.Instance,
            accessor, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
        return new StubAi(fallback, accessor, respond);
    }

    // A definition that has both a writing brief and generator rules.
    private sealed class BriefWithRules(AiWritingBrief brief, ContentGenerators generators) : IAiSeedDefinition
    {
        public string Id => "both";
        public AiWritingBrief WritingBrief => brief;
        public ContentGenerators? Generators => generators;
        public SeedTree Build() => new();
    }

    private static Task<string?> Reply(object value) => Task.FromResult<string?>(JsonSerializer.Serialize(value));

    private static IEnumerable<PropertyInfo> Props => typeof(BlankBlock).GetProperties();

    // ── UseGenerator ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UseGenerator_IsNotRequestedFromTheModel_AndKeepsTheBogusValue()
    {
        var brief = new AiWritingBrief().ForContentType<BlankBlock>(t => t.UseGenerator(x => x.Heading));
        var generators = new ContentGenerators().ForContentType<BlankBlock>(t => t.Property(x => x.Heading, f => "FIXED"));
        var ai = Ai(brief, generators, _ => Reply(new { Summary = "Written by the model" }));
        var content = new BlankBlock();

        await ai.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.DoesNotContain(ai.Requests.Single().Fields, f => f.Name == nameof(BlankBlock.Heading));
        Assert.Equal("FIXED", content.Heading);
        Assert.Equal("Written by the model", content.Summary);
    }

    [Fact]
    public async Task UseGenerator_OnAFieldWithNoRule_KeepsBogusDefaultText()
    {
        var brief = new AiWritingBrief().ForContentType<BlankBlock>(t => t.UseGenerator(x => x.Heading));
        var ai = Ai(brief, null, _ => Reply(new { Summary = "x" }));
        var content = new BlankBlock();

        await ai.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.False(string.IsNullOrWhiteSpace(content.Heading));
    }

    // ── Blank (brief) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Blank_LeavesTheFieldBlank_EvenWhenTheFallbackWouldFillIt()
    {
        var brief = new AiWritingBrief().ForContentType<BlankBlock>(t => t.Blank(x => x.Heading));
        var generators = new ContentGenerators().ForContentType<BlankBlock>(t => t.Property(x => x.Heading, f => "FIXED"));
        var ai = Ai(brief, generators, _ => Reply(new { Summary = "x" }));
        var content = new BlankBlock();

        await ai.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.True(string.IsNullOrEmpty(content.Heading));
    }

    [Fact]
    public async Task Blank_StaysBlank_WhenTheAiFails()
    {
        var brief = new AiWritingBrief().ForContentType<BlankBlock>(t => t.Blank(x => x.Heading));
        var generators = new ContentGenerators().ForContentType<BlankBlock>(t => t.Property(x => x.Heading, f => "FIXED"));
        var ai = Ai(brief, generators, _ => throw new HttpRequestException("offline"));
        var content = new BlankBlock();

        await ai.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.True(string.IsNullOrEmpty(content.Heading));
    }

    [Fact]
    public async Task Blank_ClearsANonTextValueToItsDefault()
    {
        var brief = new AiWritingBrief().ForContentType<BlankBlock>(t => t.Blank(x => x.Count));
        var generators = new ContentGenerators().ForContentType<BlankBlock>(t => t.Property(x => x.Count, f => 7));
        var ai = Ai(brief, generators, _ => Reply(new { Summary = "x" }));
        var content = new BlankBlock();

        await ai.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.Equal(0, content.Count);
    }

    // ── the last declaration for a field wins ────────────────────────────────

    [Fact]
    public async Task BlankAfterUseGenerator_LeavesTheFieldBlank()
    {
        var brief = new AiWritingBrief().ForContentType<BlankBlock>(t => t
            .UseGenerator(x => x.Heading)
            .Blank(x => x.Heading));
        var generators = new ContentGenerators().ForContentType<BlankBlock>(t => t.Property(x => x.Heading, f => "FIXED"));
        var ai = Ai(brief, generators, _ => Reply(new { Summary = "x" }));
        var content = new BlankBlock();

        await ai.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.True(string.IsNullOrEmpty(content.Heading));
    }

    [Fact]
    public async Task UseGeneratorAfterBlank_KeepsTheBogusValue()
    {
        var brief = new AiWritingBrief().ForContentType<BlankBlock>(t => t
            .Blank(x => x.Heading)
            .UseGenerator(x => x.Heading));
        var generators = new ContentGenerators().ForContentType<BlankBlock>(t => t.Property(x => x.Heading, f => "FIXED"));
        var ai = Ai(brief, generators, _ => Reply(new { Summary = "x" }));
        var content = new BlankBlock();

        await ai.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.Equal("FIXED", content.Heading);
    }

    // ── Blank (generators) ─────────────────────────────────────────────────

    [Fact]
    public async Task GeneratorBlank_LeavesTheFieldBlank_UnderBogus()
    {
        var generators = new ContentGenerators().ForContentType<BlankBlock>(t => t.Blank(x => x.Heading));
        var provider = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            NullLogger<BogusContentGenerationProvider>.Instance,
            new Accessor { Current = new RuleDefinition(generators) }, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
        var content = new BlankBlock();

        await provider.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.True(string.IsNullOrEmpty(content.Heading));
        Assert.False(string.IsNullOrWhiteSpace(content.Summary));
    }

    [Fact]
    public async Task GeneratorBlank_OverridesAGlobalMapEntry_ForThatDefinition()
    {
        var generators = new ContentGenerators().ForContentType<BlankBlock>(t => t.Blank(x => x.Heading));
        var mapOptions = new BogusGeneratorMapOptions();
        mapOptions.AdditionalGenerators["Heading"] = f => "GLOBAL";
        var provider = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(mapOptions)),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            NullLogger<BogusContentGenerationProvider>.Instance,
            new Accessor { Current = new RuleDefinition(generators) }, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
        var content = new BlankBlock();

        await provider.PopulateAsync(content, Props, "en", typeof(BlankBlock));

        Assert.True(string.IsNullOrEmpty(content.Heading));
    }
}
