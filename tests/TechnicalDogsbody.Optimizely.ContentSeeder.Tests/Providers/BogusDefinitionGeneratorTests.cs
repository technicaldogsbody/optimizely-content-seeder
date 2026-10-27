using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using System.Reflection;
using Bogus;
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

/// <summary>How a seed definition's Generators rules change what the Bogus provider writes, and what they leave alone.</summary>
public class BogusDefinitionGeneratorTests
{
    public class GenBlock : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual string? Title { get; set; }
        public virtual int Count { get; set; }
        public virtual XhtmlString? Body { get; set; }
    }

    private sealed class Accessor : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; }
    }

    private sealed class RuleDefinition(ContentGenerators? generators) : ISeedDefinition
    {
        public string Id => "rules";
        public ContentGenerators? Generators => generators;
        public SeedTree Build() => new();
    }

    private static Accessor AccessorFor(ContentGenerators? generators) => new() { Current = new RuleDefinition(generators) };

    private static BogusContentGenerationProvider Provider(
        ISeedDefinitionAccessor? accessor,
        Action<Dictionary<string, Func<Faker, object>>>? globalMap = null)
    {
        var mapOptions = new BogusGeneratorMapOptions();
        globalMap?.Invoke(mapOptions.AdditionalGenerators);

        return new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(mapOptions)),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            NullLogger<BogusContentGenerationProvider>.Instance,
            accessor, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
    }

    private static Task Populate(BogusContentGenerationProvider provider, GenBlock content, string language = "en")
        => provider.PopulateAsync(content, typeof(GenBlock).GetProperties(), language, typeof(GenBlock));

    [Fact]
    public async Task DefinitionRule_SetsTheValue()
    {
        var generators = new ContentGenerators().ForContentType<GenBlock>(t => t.Property(x => x.Heading, f => "RULE"));
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(generators)), content);

        Assert.Equal("RULE", content.Heading);
    }

    [Fact]
    public async Task DefinitionRule_BeatsTheGlobalQualifiedAndUnqualifiedEntries()
    {
        var generators = new ContentGenerators().ForContentType<GenBlock>(t => t.Property(x => x.Heading, f => "RULE"));
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(generators), map =>
        {
            map["GenBlock.Heading"] = f => "GLOBAL-QUALIFIED";
            map["Heading"] = f => "GLOBAL-UNQUALIFIED";
        }), content);

        Assert.Equal("RULE", content.Heading);
    }

    [Fact]
    public async Task WithNoRuleForAProperty_TheGlobalMapStillApplies()
    {
        var generators = new ContentGenerators().ForContentType<GenBlock>(t => t.Property(x => x.Heading, f => "RULE"));
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(generators), map => map["Title"] = f => "GLOBAL-TITLE"), content);

        Assert.Equal("RULE", content.Heading);
        Assert.Equal("GLOBAL-TITLE", content.Title);
    }

    [Fact]
    public async Task WithNoGlobalEntry_AndNoRule_TheBuiltInValueIsUsed()
    {
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(null)), content);

        Assert.False(string.IsNullOrWhiteSpace(content.Heading));
        Assert.NotEqual("RULE", content.Heading);
    }

    [Fact]
    public async Task DefinitionWithNoGenerators_ChangesNothing()
    {
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(null)), content);

        Assert.False(string.IsNullOrWhiteSpace(content.Title));
    }

    [Fact]
    public async Task NoAccessor_BehavesAsBefore()
    {
        var content = new GenBlock();

        await Populate(Provider(accessor: null), content);

        Assert.False(string.IsNullOrWhiteSpace(content.Heading));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("sv", "sv")]
    public async Task RuleReceivesTheFakerForTheLanguageBeingGenerated(string language, string expected)
    {
        // f.Locale is the locale the faker was created for, so this shows the rule runs in the right language.
        var generators = new ContentGenerators().ForContentType<GenBlock>(t => t.Property(x => x.Heading, f => f.Locale));
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(generators)), content, language);

        Assert.Equal(expected, content.Heading);
    }

    [Fact]
    public async Task StringRule_OnARichTextProperty_IsWrapped()
    {
        var generators = new ContentGenerators().ForContentType<GenBlock>(t => t.Property(x => x.Body, f => "hello there"));
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(generators)), content);

        Assert.NotNull(content.Body);
        Assert.Contains("hello there", content.Body!.ToString());
    }

    [Fact]
    public async Task ARuleThatThrows_LogsAndLeavesThatProperty_ButOthersStillPopulate()
    {
        var generators = new ContentGenerators().ForContentType<GenBlock>(t =>
            t.Property(x => x.Heading, f => throw new InvalidOperationException("boom")));
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(generators)), content);

        Assert.Null(content.Heading);
        Assert.False(string.IsNullOrWhiteSpace(content.Title));
    }

    [Fact]
    public async Task ARuleReturningTheWrongType_IsSkipped_ButOthersStillPopulate()
    {
        // An int for a string property makes SetValue throw; the property is skipped with a warning.
        var generators = new ContentGenerators().ForContentType<GenBlock>(t => t.Property(x => x.Heading, f => 42));
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(generators)), content);

        Assert.Null(content.Heading);
        Assert.False(string.IsNullOrWhiteSpace(content.Title));
    }

    [Fact]
    public async Task ARuleOnAnIntProperty_IsUsed()
    {
        var generators = new ContentGenerators().ForContentType<GenBlock>(t => t.Property(x => x.Count, f => 7));
        var content = new GenBlock();

        await Populate(Provider(AccessorFor(generators)), content);

        Assert.Equal(7, content.Count);
    }
}
