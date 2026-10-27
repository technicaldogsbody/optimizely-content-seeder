using EPiServer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

/// <summary>The Bogus provider picks the rule for the language it is generating.</summary>
public class BogusLanguageRuleTests
{
    public class LangBlock : BlockData
    {
        public virtual string? Heading { get; set; }
    }

    private sealed class Accessor(ISeedDefinition definition) : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; } = definition;
    }

    private sealed class RuleDefinition(ContentGenerators generators) : ISeedDefinition
    {
        public string Id => "lang-rules";
        public ContentGenerators? Generators => generators;
        public SeedTree Build() => new();
    }

    private static BogusContentGenerationProvider Provider(ContentGenerators generators) => new(
        Mock.Of<IImageContentFactory>(),
        new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
        OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
        NullLogger<BogusContentGenerationProvider>.Instance,
        new Accessor(new RuleDefinition(generators)));

    [Fact]
    public async Task EachLanguage_GetsItsOwnRule_AndOthersGetTheNeutralOne()
    {
        var generators = new ContentGenerators()
            .ForContentType<LangBlock>(t => t
                .Property(x => x.Heading, f => "neutral")
                .Property(x => x.Heading, "zh", f => "中文"));
        var provider = Provider(generators);

        var zh = new LangBlock();
        await provider.PopulateAsync(zh, typeof(LangBlock).GetProperties(), "zh", typeof(LangBlock));
        var sv = new LangBlock();
        await provider.PopulateAsync(sv, typeof(LangBlock).GetProperties(), "sv", typeof(LangBlock));

        Assert.Equal("中文", zh.Heading);
        Assert.Equal("neutral", sv.Heading);
    }
}
