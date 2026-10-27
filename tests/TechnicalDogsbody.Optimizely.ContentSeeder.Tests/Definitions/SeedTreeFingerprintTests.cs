using EPiServer.Core;
using EPiServer.VisualBuilder;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Definitions;

public class SeedTreeFingerprintTests
{
    private sealed class FakeExperience : ExperienceData { }
    private sealed class FakeSection : SectionData { }
    private sealed class FakeElement : BlockData { }
    private sealed class OtherElement : BlockData { }

    private static ExperienceSeedTree Tree(
        int maxColumns = 3,
        Type? elementType = null,
        int pageCount = 2,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, string>? settings = null)
        => new(
            blocks: [new BlockDefinition("promo", typeof(FakeElement), configure: configure)],
            nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: pageCount, sections: [new SectionSlotAssignment("content", displaySettings: settings)])],
            sections:
            [
                new SectionDefinition("content", typeof(FakeSection), rows:
                [
                    new RowDefinition(columns: [new ColumnDefinition(elements: [new ElementDefinition(elementType ?? typeof(FakeElement))])]),
                    new RepeatingElementDefinition(typeof(FakeElement), minColumns: 1, maxColumns: maxColumns)
                ])
            ]);

    private static string Fp(SeedTree tree, string master = "en", string[]? languages = null)
        => SeedTreeFingerprint.Compute(tree, master, languages ?? []);

    [Fact]
    public void SameShape_ProducesSameFingerprint()
    {
        Assert.Equal(Fp(Tree()), Fp(Tree()));
    }

    [Fact]
    public void Fingerprint_IsSha256Hex()
    {
        Assert.Matches("^[0-9a-f]{64}$", Fp(Tree()));
    }

    [Fact]
    public void ChangingRepeatingRange_ChangesFingerprint()
    {
        Assert.NotEqual(Fp(Tree(maxColumns: 3)), Fp(Tree(maxColumns: 6)));
    }

    [Fact]
    public void ChangingElementType_ChangesFingerprint()
    {
        Assert.NotEqual(Fp(Tree()), Fp(Tree(elementType: typeof(OtherElement))));
    }

    [Fact]
    public void ChangingPageCount_ChangesFingerprint()
    {
        Assert.NotEqual(Fp(Tree(pageCount: 2)), Fp(Tree(pageCount: 5)));
    }

    [Fact]
    public void AddingARowSource_ChangesFingerprint()
    {
        var withExtraRow = new ExperienceSeedTree(
            nodes: Tree().Nodes,
            sections:
            [
                new SectionDefinition("content", typeof(FakeSection), rows:
                [
                    .. Tree().Sections[0].Rows,
                    new RepeatingElementDefinition(typeof(OtherElement), minColumns: 2, maxColumns: 2)
                ])
            ],
            blocks: Tree().Blocks);

        Assert.NotEqual(Fp(Tree()), Fp(withExtraRow));
    }

    [Fact]
    public void ChangingLanguages_ChangesFingerprint()
    {
        Assert.NotEqual(Fp(Tree(), languages: []), Fp(Tree(), languages: ["sv"]));
        Assert.NotEqual(Fp(Tree(), master: "en"), Fp(Tree(), master: "en-GB"));
    }

    [Fact]
    public void ChangingStyleSettings_ChangesFingerprint()
    {
        var dark = new Dictionary<string, string> { ["overlay"] = "dark" };
        var light = new Dictionary<string, string> { ["overlay"] = "light" };

        Assert.NotEqual(Fp(Tree(settings: dark)), Fp(Tree(settings: light)));
    }

    [Fact]
    public void StyleSettingOrder_DoesNotChangeFingerprint()
    {
        var a = new Dictionary<string, string> { ["overlay"] = "dark", ["height"] = "full" };
        var b = new Dictionary<string, string> { ["height"] = "full", ["overlay"] = "dark" };

        Assert.Equal(Fp(Tree(settings: a)), Fp(Tree(settings: b)));
    }

    [Fact]
    public void ConfigureCallbacks_AreNotPartOfTheFingerprint()
    {
        Assert.Equal(Fp(Tree(configure: (_, _) => { })), Fp(Tree(configure: null)));
    }

    // ── videos ───────────────────────────────────────────────────────────────

    private static ExperienceSeedTree WithVideos(Func<IReadOnlyDictionary<string, VideoSource>?> videos) => new(
        blocks: [new BlockDefinition("video", typeof(FakeElement), videos: videos())],
        nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, videos: videos(), sections: [new SectionSlotAssignment("s")])],
        sections:
        [
            new SectionDefinition("s", typeof(FakeSection), videos: videos(), rows:
            [
                new RowDefinition(columns: [new ColumnDefinition(elements: [new ElementDefinition(typeof(FakeElement), videos: videos())])]),
                new RepeatingElementDefinition(typeof(FakeElement), minColumns: 1, maxColumns: 2, videos: videos()),
            ])
        ]);

    private static IReadOnlyDictionary<string, VideoSource> Videos(VideoSource source)
        => new Dictionary<string, VideoSource> { ["VideoUrl"] = source };

    [Fact]
    public void DefinitionsWithoutVideos_KeepTheFingerprintTheyAlwaysHad()
    {
        // No video marker is written when nothing is declared, so adding the feature changed nobody's fingerprint.
        Assert.DoesNotContain("|V:", Fp(WithVideos(() => null)));
        Assert.DoesNotContain("|V:", Fp(Tree()));
    }

    [Fact]
    public void AddingAVideo_ChangesTheFingerprint()
    {
        Assert.NotEqual(Fp(WithVideos(() => null)), Fp(WithVideos(() => Videos(VideoSource.Uploaded()))));
    }

    [Fact]
    public void ChangingAVideosSource_ChangesTheFingerprint()
    {
        var a = Fp(WithVideos(() => Videos(VideoSource.YouTube("UCaaaaaaaaaaaaaaaaaaaaaa"))));
        var b = Fp(WithVideos(() => Videos(VideoSource.YouTube("UCbbbbbbbbbbbbbbbbbbbbbb"))));
        var c = Fp(WithVideos(() => Videos(VideoSource.Vimeo("UCaaaaaaaaaaaaaaaaaaaaaa"))));
        var d = Fp(WithVideos(() => Videos(VideoSource.YouTube("UCaaaaaaaaaaaaaaaaaaaaaa", titleProperty: "Caption"))));

        Assert.Equal(4, new[] { a, b, c, d }.Distinct().Count());
    }

    [Fact]
    public void SameVideos_ProduceTheSameFingerprint()
    {
        Assert.Equal(
            Fp(WithVideos(() => Videos(VideoSource.Urls("https://youtu.be/aqz-KE-bpKQ")))),
            Fp(WithVideos(() => Videos(VideoSource.Urls("https://youtu.be/aqz-KE-bpKQ")))));
    }
}
