using Bogus;
using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Definitions;

public class NamedInstancesTests
{
    public class NamedHero : BlockData
    {
        public virtual string? Title { get; set; }
        public virtual XhtmlString? Body { get; set; }
    }

    public class NamedDerivedHero : NamedHero { }

    private static Faker F => new("en");

    // ── generator rules ───────────────────────────────────────────────────────

    [Fact]
    public void Generators_NamedRule_BeatsTheDefaultForThatProperty()
    {
        var generators = new ContentGenerators()
            .ForContentType<NamedHero>(t => t.Property(x => x.Title, f => "default"))
            .ForContentType<NamedHero>("alt", t => t.Property(x => x.Title, f => "named"));

        Assert.Equal("named", generators.Resolve(typeof(NamedHero), nameof(NamedHero.Title), instance: "alt")!(F));
    }

    [Fact]
    public void Generators_NamedSetWithoutTheProperty_FallsBackToTheDefault()
    {
        var generators = new ContentGenerators()
            .ForContentType<NamedHero>(t => t.Property(x => x.Title, f => "default"))
            .ForContentType<NamedHero>("alt", t => t.Property(x => x.Body, f => new XhtmlString("<p>x</p>")));

        Assert.Equal("default", generators.Resolve(typeof(NamedHero), nameof(NamedHero.Title), instance: "alt")!(F));
    }

    [Fact]
    public void Generators_NoInstance_UsesTheDefault()
    {
        var generators = new ContentGenerators()
            .ForContentType<NamedHero>(t => t.Property(x => x.Title, f => "default"))
            .ForContentType<NamedHero>("alt", t => t.Property(x => x.Title, f => "named"));

        Assert.Equal("default", generators.Resolve(typeof(NamedHero), nameof(NamedHero.Title))!(F));
    }

    [Fact]
    public void Generators_NamedSetWithALanguageRule_UsesItForThatLanguage()
    {
        var generators = new ContentGenerators()
            .ForContentType<NamedHero>(t => t.Property(x => x.Title, "zh", f => "默认"))
            .ForContentType<NamedHero>("alt", t => t.Property(x => x.Title, "zh", f => "命名"));

        Assert.Equal("命名", generators.Resolve(typeof(NamedHero), nameof(NamedHero.Title), "zh", instance: "alt")!(F));
        Assert.Equal("默认", generators.Resolve(typeof(NamedHero), nameof(NamedHero.Title), "zh")!(F));
    }

    [Fact]
    public void Generators_NamedKitchenSink_IsKitchenSinkOnlyInThatSet()
    {
        var generators = new ContentGenerators()
            .ForContentType<NamedHero>("alt", t => t.KitchenSink(x => x.Body));

        Assert.True(generators.IsKitchenSink(typeof(NamedHero), nameof(NamedHero.Body), instance: "alt"));
        Assert.False(generators.IsKitchenSink(typeof(NamedHero), nameof(NamedHero.Body)));
    }

    [Fact]
    public void Generators_NamedPlainRule_OverridesTheDefaultKitchenSink()
    {
        var generators = new ContentGenerators()
            .ForContentType<NamedHero>(t => t.KitchenSink(x => x.Body))
            .ForContentType<NamedHero>("alt", t => t.Property(x => x.Body, f => new XhtmlString("<p>plain</p>")));

        Assert.False(generators.IsKitchenSink(typeof(NamedHero), nameof(NamedHero.Body), instance: "alt"));
        Assert.True(generators.IsKitchenSink(typeof(NamedHero), nameof(NamedHero.Body)));
    }

    [Fact]
    public void Generators_HasInstance_IsTrueForTheTypeAndItsDerivedTypes()
    {
        var generators = new ContentGenerators().ForContentType<NamedHero>("alt", t => t.Property(x => x.Title, f => "named"));

        Assert.True(generators.HasInstance(typeof(NamedHero), "alt"));
        Assert.True(generators.HasInstance(typeof(NamedDerivedHero), "alt"));
        Assert.False(generators.HasInstance(typeof(NamedHero), "other"));
    }

    [Fact]
    public void Generators_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContentGenerators().ForContentType<NamedHero>(" ", t => t.Property(x => x.Title, f => "x")));
    }

    // ── writing brief ─────────────────────────────────────────────────────────

    [Fact]
    public void Brief_NamedGuidance_ReplacesTheDefaultGuidanceForThatProperty()
    {
        var brief = new AiWritingBrief()
            .ForContentType<NamedHero>(t => t.Property(x => x.Title, "default guidance"))
            .ForContentType<NamedHero>("alt", t => t.Property(x => x.Title, "named guidance"));

        Assert.Equal("named guidance", brief.Resolve(typeof(NamedHero), "alt").PropertyGuidance[nameof(NamedHero.Title)]);
        Assert.Equal("default guidance", brief.Resolve(typeof(NamedHero)).PropertyGuidance[nameof(NamedHero.Title)]);
    }

    [Fact]
    public void Brief_NamedGuidanceWithoutALimit_ClearsTheInheritedLimit()
    {
        var brief = new AiWritingBrief()
            .ForContentType<NamedHero>(t => t.Property(x => x.Title, "short", maxWords: 3))
            .ForContentType<NamedHero>("alt", t => t.Property(x => x.Title, "longer"));

        Assert.False(brief.Resolve(typeof(NamedHero), "alt").PropertyLimits.ContainsKey(nameof(NamedHero.Title)));
        Assert.True(brief.Resolve(typeof(NamedHero)).PropertyLimits.ContainsKey(nameof(NamedHero.Title)));
    }

    [Fact]
    public void Brief_NamedBlank_FlipsTheDefaultUseGenerator()
    {
        var brief = new AiWritingBrief()
            .ForContentType<NamedHero>(t => t.UseGenerator(x => x.Title))
            .ForContentType<NamedHero>("alt", t => t.Blank(x => x.Title));

        var named = brief.Resolve(typeof(NamedHero), "alt");
        Assert.Contains(nameof(NamedHero.Title), named.BlankProperties);
        Assert.DoesNotContain(nameof(NamedHero.Title), named.GeneratedProperties);
    }

    [Fact]
    public void Brief_NamedPrompt_IsAddedAfterTheDefaultPrompt()
    {
        var brief = new AiWritingBrief()
            .ForContentType<NamedHero>(t => t.Prompt("Default prompt."))
            .ForContentType<NamedHero>("alt", t => t.Prompt("Named prompt."));

        Assert.Equal(new[] { "Default prompt.", "Named prompt." }, brief.Resolve(typeof(NamedHero), "alt").Prompts.ToArray());
        Assert.Equal(new[] { "Default prompt." }, brief.Resolve(typeof(NamedHero)).Prompts.ToArray());
    }

    [Fact]
    public void Brief_HasInstance_IsTrueForTheTypeAndItsDerivedTypes()
    {
        var brief = new AiWritingBrief().ForContentType<NamedHero>("alt", t => t.Prompt("x"));

        Assert.True(brief.HasInstance(typeof(NamedDerivedHero), "alt"));
        Assert.False(brief.HasInstance(typeof(NamedHero), "other"));
    }

    [Fact]
    public void Brief_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new AiWritingBrief().ForContentType<NamedHero>("", t => t.Prompt("x")));
    }

    // ── fingerprint ───────────────────────────────────────────────────────────

    [Fact]
    public void Fingerprint_ABlockWithAnInstance_HashesDifferentlyFromOneWithout()
    {
        var plain = new SeedTree(blocks: [new BlockDefinition("hero", typeof(NamedHero))]);
        var named = new SeedTree(blocks: [new BlockDefinition("hero", typeof(NamedHero), instance: "alt")]);

        Assert.NotEqual(SeedTreeFingerprint.Compute(plain, "en", []), SeedTreeFingerprint.Compute(named, "en", []));
    }

    [Fact]
    public void Fingerprint_ANodeWithAnInstance_HashesDifferentlyFromOneWithout()
    {
        var plain = new SeedTree(nodes: [new SeedNode(typeof(NamedHero))]);
        var named = new SeedTree(nodes: [new SeedNode(typeof(NamedHero), instance: "alt")]);

        Assert.NotEqual(SeedTreeFingerprint.Compute(plain, "en", []), SeedTreeFingerprint.Compute(named, "en", []));
    }

    [Fact]
    public void Definitions_BlankInstance_Throws()
    {
        Assert.Throws<ArgumentException>(() => new BlockDefinition("hero", typeof(NamedHero), instance: "  "));
    }

    // ── the scope ─────────────────────────────────────────────────────────────

    [Fact]
    public void Scope_EnterSetsTheNameAndDisposeRestoresIt()
    {
        Assert.Null(SeedInstanceScope.Current);

        using (SeedInstanceScope.Enter("alt"))
            Assert.Equal("alt", SeedInstanceScope.Current);

        Assert.Null(SeedInstanceScope.Current);
    }

    [Fact]
    public void Scope_Nested_RestoresTheOuterName()
    {
        using (SeedInstanceScope.Enter("outer"))
        {
            using (SeedInstanceScope.Enter("inner"))
                Assert.Equal("inner", SeedInstanceScope.Current);

            Assert.Equal("outer", SeedInstanceScope.Current);
        }
    }

    [Fact]
    public async Task Scope_ParallelJobs_EachSeeItsOwnName()
    {
        var seen = new string?[20];

        await Parallel.ForEachAsync(Enumerable.Range(0, seen.Length), async (i, ct) =>
        {
            using (SeedInstanceScope.Enter($"n{i}"))
            {
                await Task.Delay(1, ct);
                seen[i] = SeedInstanceScope.Current;
            }
        });

        for (int i = 0; i < seen.Length; i++)
            Assert.Equal($"n{i}", seen[i]);
        Assert.Null(SeedInstanceScope.Current);
    }
}
