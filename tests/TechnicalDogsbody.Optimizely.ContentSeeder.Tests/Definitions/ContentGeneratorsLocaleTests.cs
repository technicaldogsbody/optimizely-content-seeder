using Bogus;
using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Definitions;

/// <summary>Per-language generator rules: which rule is used for which CMS language tag.</summary>
public class ContentGeneratorsLocaleTests
{
    public class LocaleHero : BlockData
    {
        public virtual string? Heading { get; set; }
    }

    public class LocaleDerivedHero : LocaleHero { }

    private static Faker F => new("en");

    private static string? Value(ContentGenerators generators, Type type, string? language)
        => generators.Resolve(type, nameof(LocaleHero.Heading), language)?.Invoke(F) as string;

    [Fact]
    public void LanguageRule_IsUsedForThatLanguage_AndNotForOthers()
    {
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t.Property(x => x.Heading, "zh", f => "中文"));

        Assert.Equal("中文", Value(generators, typeof(LocaleHero), "zh"));
        Assert.Null(Value(generators, typeof(LocaleHero), "en"));
        Assert.Null(Value(generators, typeof(LocaleHero), null));
    }

    [Fact]
    public void LanguageRule_BeatsTheLanguageNeutralRule_ForThatLanguage()
    {
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t
                .Property(x => x.Heading, f => "neutral")
                .Property(x => x.Heading, "zh", f => "中文"));

        Assert.Equal("中文", Value(generators, typeof(LocaleHero), "zh"));
        Assert.Equal("neutral", Value(generators, typeof(LocaleHero), "sv"));
    }

    [Fact]
    public void LanguageWithoutItsOwnRule_UsesTheNeutralRule()
    {
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t
                .Property(x => x.Heading, f => "neutral")
                .Property(x => x.Heading, "ar-AE", f => "عربي"));

        Assert.Equal("neutral", Value(generators, typeof(LocaleHero), "sv"));
        Assert.Equal("neutral", Value(generators, typeof(LocaleHero), null));
    }

    [Fact]
    public void RegionalTag_FallsBackToItsBaseLanguage()
    {
        // "zh-Hans" has no rule of its own, so the "zh" rule applies.
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t.Property(x => x.Heading, "zh", f => "中文"));

        Assert.Equal("中文", Value(generators, typeof(LocaleHero), "zh-Hans"));
        Assert.Equal("中文", Value(generators, typeof(LocaleHero), "zh-Hans-CN"));
    }

    [Fact]
    public void ExactRegionalTag_BeatsItsBaseLanguage()
    {
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t
                .Property(x => x.Heading, "zh", f => "base")
                .Property(x => x.Heading, "zh-Hant", f => "traditional"));

        Assert.Equal("traditional", Value(generators, typeof(LocaleHero), "zh-Hant"));
        Assert.Equal("base", Value(generators, typeof(LocaleHero), "zh-Hans"));
    }

    [Fact]
    public void LanguageTags_MatchIgnoringCase()
    {
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t.Property(x => x.Heading, "ar-AE", f => "عربي"));

        Assert.Equal("عربي", Value(generators, typeof(LocaleHero), "ar-ae"));
    }

    [Fact]
    public void MoreDerivedType_NeutralRule_BeatsBaseTypeLanguageRule()
    {
        // The most derived type with a matching rule wins, whichever kind of rule it has.
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t.Property(x => x.Heading, "zh", f => "base-zh"))
            .ForContentType<LocaleDerivedHero>(t => t.Property(x => x.Heading, f => "derived-neutral"));

        Assert.Equal("derived-neutral", Value(generators, typeof(LocaleDerivedHero), "zh"));
    }

    [Fact]
    public void BaseTypeLanguageRule_AppliesToDerivedTypes_ForThatLanguage()
    {
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t.Property(x => x.Heading, "zh", f => "base-zh"));

        Assert.Equal("base-zh", Value(generators, typeof(LocaleDerivedHero), "zh"));
        Assert.Null(Value(generators, typeof(LocaleDerivedHero), "en"));
    }

    [Fact]
    public void LaterLanguageRule_ForTheSameLanguage_Wins()
    {
        var generators = new ContentGenerators()
            .ForContentType<LocaleHero>(t => t
                .Property(x => x.Heading, "zh", f => "first")
                .Property(x => x.Heading, "zh", f => "second"));

        Assert.Equal("second", Value(generators, typeof(LocaleHero), "zh"));
    }

    [Fact]
    public void BlankLanguageTag_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContentGenerators().ForContentType<LocaleHero>(t => t.Property(x => x.Heading, " ", f => "x")));
    }
}
