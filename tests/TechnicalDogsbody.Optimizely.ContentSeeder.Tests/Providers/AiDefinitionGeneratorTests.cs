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
/// A definition's Generators rules and the AI provider. Rules are for Bogus, so they must not stop the AI writing prose;
/// they do set the non-text values, and the placeholder an item keeps when the AI fails.
/// </summary>
public class AiDefinitionGeneratorTests
{
    public class AiGenBlock : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual int Count { get; set; }
    }

    private sealed class Accessor : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; }
    }

    // A plain (non-AI) definition that only has generator rules.
    private sealed class RuleDefinition(ContentGenerators generators) : ISeedDefinition
    {
        public string Id => "ai-rules";
        public ContentGenerators? Generators => generators;
        public SeedTree Build() => new();
    }

    private sealed class StubAi(
        BogusContentGenerationProvider fallback,
        ISeedDefinitionAccessor accessor,
        Func<AiContentRequest, Task<string?>> respond)
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

    private static (StubAi Provider, Accessor Accessor) Create(
        ContentGenerators generators, Func<AiContentRequest, Task<string?>> respond)
    {
        var accessor = new Accessor { Current = new RuleDefinition(generators) };
        var fallback = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            NullLogger<BogusContentGenerationProvider>.Instance,
            accessor, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());

        return (new StubAi(fallback, accessor, respond), accessor);
    }

    private static Task<string?> Reply(object value) => Task.FromResult<string?>(JsonSerializer.Serialize(value));

    [Fact]
    public async Task ATextPropertyWithARule_IsStillSentToTheModel_AndTheModelWins()
    {
        var generators = new ContentGenerators().ForContentType<AiGenBlock>(t => t.Property(x => x.Heading, f => "RULE"));
        var (provider, _) = Create(generators, _ => Reply(new { Heading = "Written by the model" }));
        var content = new AiGenBlock();

        await provider.PopulateAsync(content, typeof(AiGenBlock).GetProperties(), "en", typeof(AiGenBlock));

        Assert.Contains(provider.Requests.Single().Fields, f => f.Name == nameof(AiGenBlock.Heading));
        Assert.Equal("Written by the model", content.Heading);
    }

    [Fact]
    public async Task ANonTextPropertyWithARule_GetsTheRuleValue_AndIsNotSentToTheModel()
    {
        var generators = new ContentGenerators().ForContentType<AiGenBlock>(t => t.Property(x => x.Count, f => 7));
        var (provider, _) = Create(generators, _ => Reply(new { Heading = "Written by the model" }));
        var content = new AiGenBlock();

        await provider.PopulateAsync(content, typeof(AiGenBlock).GetProperties(), "en", typeof(AiGenBlock));

        Assert.Equal(7, content.Count);
        Assert.DoesNotContain(provider.Requests.Single().Fields, f => f.Name == nameof(AiGenBlock.Count));
    }

    [Fact]
    public async Task WhenTheModelFails_ATextPropertyKeepsItsRulePlaceholder()
    {
        var generators = new ContentGenerators().ForContentType<AiGenBlock>(t => t.Property(x => x.Heading, f => "RULE"));
        var (provider, _) = Create(generators, _ => throw new HttpRequestException("offline"));
        var content = new AiGenBlock();

        await provider.PopulateAsync(content, typeof(AiGenBlock).GetProperties(), "en", typeof(AiGenBlock));

        Assert.Equal("RULE", content.Heading);
    }
}
