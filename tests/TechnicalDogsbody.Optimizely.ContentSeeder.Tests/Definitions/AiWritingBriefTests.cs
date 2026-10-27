using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Definitions;

public class AiWritingBriefTests
{
    private class FakeBlock : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual string? Summary { get; set; }
        public virtual string? CtaUrl { get; set; }
    }

    private sealed class FakeHero : FakeBlock
    {
        public string? Tagline { get; set; }
    }

    [Fact]
    public void Resolve_ExactType_ReturnsPromptGuidanceAndExclusions()
    {
        var brief = new AiWritingBrief()
            .ForContentType<FakeHero>(t => t
                .Prompt("Seasonal heroes.")
                .Property(x => x.Heading, "Five words maximum.")
                .Blank(x => x.Tagline));

        var resolved = brief.Resolve(typeof(FakeHero));

        Assert.Equal(["Seasonal heroes."], resolved.Prompts);
        Assert.Equal("Five words maximum.", resolved.PropertyGuidance["Heading"]);
        Assert.Contains("Tagline", resolved.BlankProperties);
    }

    [Fact]
    public void Resolve_BaseTypeGuidance_AppliesToDerivedTypes_WithDerivedWinning()
    {
        var brief = new AiWritingBrief()
            .ForContentType<FakeHero>(t => t.Prompt("Hero prompt.").Property(x => x.Heading, "Derived heading."))
            .ForContentType<FakeBlock>(t => t.Prompt("Block prompt.").Property(x => x.Heading, "Base heading.").Blank(x => x.Summary));

        var resolved = brief.Resolve(typeof(FakeHero));

        // Base guidance first, most-derived last — regardless of registration order.
        Assert.Equal(["Block prompt.", "Hero prompt."], resolved.Prompts);
        Assert.Equal("Derived heading.", resolved.PropertyGuidance["Heading"]);
        Assert.Contains("Summary", resolved.BlankProperties);
    }

    [Fact]
    public void Resolve_UnrelatedType_ReturnsEmptyGuidance()
    {
        var brief = new AiWritingBrief().ForContentType<FakeHero>(t => t.Prompt("Hero prompt."));

        var resolved = brief.Resolve(typeof(FakeBlock));

        Assert.Empty(resolved.Prompts);
        Assert.Empty(resolved.PropertyGuidance);
        Assert.Empty(resolved.BlankProperties);
    }

    [Fact]
    public void Property_WithNonPropertyExpression_Throws()
    {
        var brief = new AiWritingBrief();

        Assert.Throws<ArgumentException>(() =>
            brief.ForContentType<FakeHero>(t => t.Property(x => x.Heading + "!", "nope")));
    }

    [Fact]
    public void Prompt_Blank_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new AiWritingBrief().ForContentType<FakeHero>(t => t.Prompt("  ")));
    }
}
