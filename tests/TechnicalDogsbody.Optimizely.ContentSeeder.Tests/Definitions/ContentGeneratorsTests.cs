using Bogus;
using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Definitions;

public class ContentGeneratorsTests
{
    public class RuleHero : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual string? Title { get; set; }
        public virtual int Count { get; set; }

        public string Describe() => "not a property";
    }

    public class RuleDerivedHero : RuleHero { }

    private static Faker F => new("en");

    [Fact]
    public void ExactTypeRule_Resolves_AndProducesAValue()
    {
        var generators = new ContentGenerators()
            .ForContentType<RuleHero>(t => t.Property(x => x.Heading, f => "fixed"));

        var rule = generators.Resolve(typeof(RuleHero), "Heading");

        Assert.NotNull(rule);
        Assert.Equal("fixed", rule!(F));
    }

    [Fact]
    public void BaseTypeRule_AppliesToDerivedTypes()
    {
        var generators = new ContentGenerators()
            .ForContentType<RuleHero>(t => t.Property(x => x.Heading, f => "base"));

        Assert.NotNull(generators.Resolve(typeof(RuleDerivedHero), "Heading"));
    }

    [Fact]
    public void RuleOnTheMoreDerivedType_WinsOverItsBase()
    {
        var generators = new ContentGenerators()
            .ForContentType<RuleDerivedHero>(t => t.Property(x => x.Heading, f => "derived"))
            .ForContentType<RuleHero>(t => t.Property(x => x.Heading, f => "base"));

        Assert.Equal("derived", generators.Resolve(typeof(RuleDerivedHero), "Heading")!(F));
        Assert.Equal("base", generators.Resolve(typeof(RuleHero), "Heading")!(F));
    }

    [Fact]
    public void LaterRegistration_ForTheSameType_Wins()
    {
        var generators = new ContentGenerators()
            .ForContentType<RuleHero>(t => t.Property(x => x.Heading, f => "first"))
            .ForContentType<RuleHero>(t => t.Property(x => x.Heading, f => "second"));

        Assert.Equal("second", generators.Resolve(typeof(RuleHero), "Heading")!(F));
    }

    [Fact]
    public void DuplicateProperty_InOneBlock_LastWins()
    {
        var generators = new ContentGenerators()
            .ForContentType<RuleHero>(t => t
                .Property(x => x.Heading, f => "one")
                .Property(x => x.Heading, f => "two"));

        Assert.Equal("two", generators.Resolve(typeof(RuleHero), "Heading")!(F));
    }

    [Fact]
    public void UnknownProperty_ResolvesToNull()
    {
        var generators = new ContentGenerators()
            .ForContentType<RuleHero>(t => t.Property(x => x.Heading, f => "x"));

        Assert.Null(generators.Resolve(typeof(RuleHero), "Title"));
    }

    [Fact]
    public void UnrelatedType_ResolvesToNull()
    {
        var generators = new ContentGenerators()
            .ForContentType<RuleHero>(t => t.Property(x => x.Heading, f => "x"));

        Assert.Null(generators.Resolve(typeof(string), "Heading"));
    }

    [Fact]
    public void ValueTypeProperty_IsNamedCorrectly_ThroughTheConvertNode()
    {
        // x => x.Count is boxed to object, which the expression wraps in a Convert node.
        var generators = new ContentGenerators()
            .ForContentType<RuleHero>(t => t.Property(x => x.Count, f => 7));

        Assert.Equal(7, generators.Resolve(typeof(RuleHero), "Count")!(F));
    }

    [Fact]
    public void NonPropertyExpression_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContentGenerators().ForContentType<RuleHero>(t => t.Property(x => x.Describe(), f => "x")));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ContentGenerators().ForContentType<RuleHero>(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ContentGenerators().ForContentType<RuleHero>(t => t.Property(x => x.Heading, null!)));
        Assert.Throws<ArgumentNullException>(() => new ContentGenerators().Resolve(null!, "Heading"));
        Assert.Throws<ArgumentException>(() => new ContentGenerators().Resolve(typeof(RuleHero), " "));
    }

    [Fact]
    public void NoRules_ResolvesToNull()
    {
        Assert.Null(new ContentGenerators().Resolve(typeof(RuleHero), "Heading"));
    }
}
