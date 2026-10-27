using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.DataAnnotations;
using EPiServer.Security;
using EPiServer.VisualBuilder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

/// <summary>Covers the <c>videos</c> declarations of definitions: when they are applied and how languages treat them.</summary>
public class SeedOrchestratorVideoTests
{
    private class FakeVideoBlock : BlockData, IContent
    {
        public string Name { get; set; } = string.Empty;
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public Guid ContentGuid { get; set; } = Guid.NewGuid();
        public bool IsDeleted { get; set; }

        public virtual string? VideoUrl { get; set; }

        public virtual ContentReference? Video { get; set; }

        [CultureSpecific]
        public virtual string? Caption { get; set; }
    }

    private sealed class ListLogger : ILogger<SeedOrchestrator>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }

    private static readonly ContentReference SavedRef = new(500);
    private static readonly ContentHandle VideoAsset = new("777");
    private const string WatchUrl = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";

    private sealed record Harness(
        SeedOrchestrator Sut,
        Mock<IVideoProvider> Videos,
        Mock<IContentGenerationProvider> Provider,
        FakeVideoBlock Master,
        FakeVideoBlock Branch,
        ListLogger Log);

    private static Harness Build(
        SeedTree tree,
        bool withVideoProvider = true,
        bool canTranslate = false,
        string[]? languages = null)
    {
        var master = new FakeVideoBlock();
        var branch = new FakeVideoBlock();

        var provider = new Mock<IContentGenerationProvider>();
        provider.Setup(p => p.PopulateAsync(
                It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type?>(),
                It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        if (canTranslate)
        {
            provider.As<IContentTranslationProvider>().Setup(t => t.TranslateAsync(
                    It.IsAny<object>(), It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Type?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        var repo = new Mock<IContentRepository>();
        repo.Setup(r => r.GetDefault<FakeVideoBlock>(It.IsAny<ContentReference>())).Returns(master);
        repo.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>())).Returns(() => new ContentFolder());
        repo.Setup(r => r.CreateLanguageBranch<FakeVideoBlock>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>())).Returns(branch);
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>())).Returns(SavedRef);

        var loader = new Mock<IContentLoader>();
        IContentData? loadable = master;
        loader.Setup(l => l.TryGet<IContentData>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>(), out loadable)).Returns(true);

        var tracker = new Mock<ISeedTracker>();
        tracker.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ContentSeederRecord>());
        tracker.Setup(t => t.SetDefinitionFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var types = new Mock<IContentTypeRepository>();
        types.Setup(r => r.Load(It.IsAny<Type>())).Returns(new ContentType { ID = 1 });

        // Uploaded sources resolve to an asset, every other source to a watch URL with a title.
        var videos = new Mock<IVideoProvider>();
        videos.Setup(v => v.ResolveAsync(It.IsAny<VideoSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VideoSource s, CancellationToken _) => s.Kind == VideoSourceKind.Uploaded
                ? new ResolvedVideo(null, VideoAsset, null)
                : new ResolvedVideo(WatchUrl, null, "Big Buck Bunny"));

        var log = new ListLogger();
        var seederOptions = new ContentSeederOptions { MasterLanguage = "en", Languages = languages ?? [] };
        IVideoProvider videoProvider = withVideoProvider ? videos.Object : new NullVideoProvider(log);
        var sections = TestSectionSeeder.Create(
            repo.Object, loader.Object, types.Object, provider.Object,
            Mock.Of<IBlueprintRepository>(), Mock.Of<IBlueprintContentFactory>(), Mock.Of<ICompositionMapper>(),
            tracker.Object, seederOptions, videoProvider, log);

        var sut = new SeedOrchestrator(
            repo.Object, loader.Object, types.Object, tracker.Object, provider.Object,
            Mock.Of<IImageContentFactory>(f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            sections,
            OptionsFactory.Create(seederOptions),
            [new LambdaSeedDefinition("def", () => tree)],
            log,
            definitionAccessor: new SeedDefinitionAccessor(),
            videoProvider: videoProvider);

        return new Harness(sut, videos, provider, master, branch, log);
    }

    private static ExperienceSeedTree BlockWith(
        IReadOnlyDictionary<string, VideoSource> videos, Action<object, int>? configure = null)
        => new(blocks: [new BlockDefinition("video", typeof(FakeVideoBlock), configure, videos: videos)]);

    private static IReadOnlyDictionary<string, VideoSource> Both() => new Dictionary<string, VideoSource>
    {
        [nameof(FakeVideoBlock.VideoUrl)] = VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg", titleProperty: nameof(FakeVideoBlock.Caption)),
        [nameof(FakeVideoBlock.Video)] = VideoSource.Uploaded(),
    };

    // ── applying videos ──────────────────────────────────────────────────────

    [Fact]
    public async Task Block_GetsTheChosenUrl_TheUploadedAsset_AndTheVideosTitle()
    {
        var h = Build(BlockWith(Both()));

        await h.Sut.SeedAsync();

        Assert.Equal(WatchUrl, h.Master.VideoUrl);
        Assert.Equal(777, h.Master.Video!.ID);
        Assert.Equal("Big Buck Bunny", h.Master.Caption);
    }

    [Fact]
    public async Task Videos_AreAppliedAfterGeneration_SoTheVideosTitleReplacesGeneratedText()
    {
        var h = Build(BlockWith(Both()));
        h.Provider.Setup(p => p.PopulateAsync(
                It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type?>(),
                It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>?>(), It.IsAny<CancellationToken>()))
            .Callback<object, IEnumerable<PropertyInfo>, string, Type?, IReadOnlyDictionary<string, (int, int)>?, CancellationToken>(
                (content, _, _, _, _, _) => { if (content is FakeVideoBlock b) { b.Caption = "generated"; b.VideoUrl = "http://bogus.example"; } })
            .Returns(Task.CompletedTask);

        await h.Sut.SeedAsync();

        Assert.Equal("Big Buck Bunny", h.Master.Caption);
        Assert.Equal(WatchUrl, h.Master.VideoUrl);
    }

    [Fact]
    public async Task Configure_RunsAfterVideos_SoItCanStillOverride()
    {
        var h = Build(BlockWith(Both(), configure: (block, _) => ((FakeVideoBlock)block).Caption = "Hand-written"));

        await h.Sut.SeedAsync();

        Assert.Equal("Hand-written", h.Master.Caption);
        Assert.Equal(WatchUrl, h.Master.VideoUrl);
    }

    [Fact]
    public async Task Pages_AndSharedSections_AlsoGetTheirVideos()
    {
        // A shared (leaf) section is created through the block path with the section's videos.
        var tree = new ExperienceSeedTree(sections:
            [new SectionDefinition("video-section", typeof(FakeVideoBlock), videos: Both())]);
        var h = Build(tree);

        await h.Sut.SeedAsync();

        Assert.Equal(WatchUrl, h.Master.VideoUrl);
        Assert.Equal(777, h.Master.Video!.ID);
    }

    // ── mistakes in a definition never stop a seed ───────────────────────────

    [Fact]
    public async Task AnUploadedVideoOnAStringProperty_IsSkippedWithAWarning()
    {
        var videos = new Dictionary<string, VideoSource> { [nameof(FakeVideoBlock.VideoUrl)] = VideoSource.Uploaded() };
        var h = Build(BlockWith(videos));

        await h.Sut.SeedAsync();

        Assert.Null(h.Master.VideoUrl);
        h.Videos.Verify(v => v.ResolveAsync(It.IsAny<VideoSource>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(h.Log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("ContentReference"));
    }

    [Fact]
    public async Task AFeedVideoOnAContentReferenceProperty_IsSkippedWithAWarning()
    {
        var videos = new Dictionary<string, VideoSource> { [nameof(FakeVideoBlock.Video)] = VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg") };
        var h = Build(BlockWith(videos));

        await h.Sut.SeedAsync();

        Assert.True(ContentReference.IsNullOrEmpty(h.Master.Video));
        Assert.Contains(h.Log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("string"));
    }

    [Fact]
    public async Task AnUnknownProperty_IsSkippedWithAWarning_AndTheRestStillApply()
    {
        var videos = new Dictionary<string, VideoSource>
        {
            ["Nope"] = VideoSource.Urls(WatchUrl),
            [nameof(FakeVideoBlock.VideoUrl)] = VideoSource.Urls(WatchUrl),
        };
        var h = Build(BlockWith(videos));

        await h.Sut.SeedAsync();

        Assert.Equal(WatchUrl, h.Master.VideoUrl);
        Assert.Contains(h.Log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Nope"));
    }

    [Fact]
    public async Task AVideoThatCannotBeChosen_LeavesThePropertyAlone()
    {
        var h = Build(BlockWith(Both()));
        h.Videos.Setup(v => v.ResolveAsync(It.IsAny<VideoSource>(), It.IsAny<CancellationToken>())).ReturnsAsync((ResolvedVideo?)null);

        await h.Sut.SeedAsync();

        Assert.Null(h.Master.VideoUrl);
        Assert.True(ContentReference.IsNullOrEmpty(h.Master.Video));
    }

    [Fact]
    public async Task WithNoVideoProviderRegistered_VideosAreSkippedWithOneWarning_AndSeedingContinues()
    {
        var tree = new ExperienceSeedTree(blocks:
        [
            new BlockDefinition("a", typeof(FakeVideoBlock), videos: Both()),
            new BlockDefinition("b", typeof(FakeVideoBlock), videos: Both()),
        ]);
        var h = Build(tree, withVideoProvider: false);

        await h.Sut.SeedAsync();

        Assert.Null(h.Master.VideoUrl);
        Assert.Single(h.Log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("IVideoProvider"));
    }

    // ── other languages ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LanguageVersion_KeepsTheMastersVideoTitle_WhetherOrNotTheProviderTranslates(bool canTranslate)
    {
        var h = Build(BlockWith(Both()), canTranslate: canTranslate, languages: ["sv"]);

        // Whatever the provider writes for the branch (translation or placeholder), it must not replace the
        // title of the video, which doesn't change with the language.
        h.Provider.Setup(p => p.PopulateAsync(
                It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), "sv", It.IsAny<Type?>(),
                It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>?>(), It.IsAny<CancellationToken>()))
            .Callback<object, IEnumerable<PropertyInfo>, string, Type?, IReadOnlyDictionary<string, (int, int)>?, CancellationToken>(
                (content, _, _, _, _, _) => ((FakeVideoBlock)content).Caption = "lorem ipsum")
            .Returns(Task.CompletedTask);
        if (canTranslate)
        {
            h.Provider.As<IContentTranslationProvider>().Setup(t => t.TranslateAsync(
                    It.IsAny<object>(), It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Type?>(), It.IsAny<CancellationToken>()))
                .Callback<object, object, IEnumerable<PropertyInfo>, string, string, Type?, CancellationToken>(
                    (_, target, _, _, _, _, _) => ((FakeVideoBlock)target).Caption = "översatt titel")
                .Returns(Task.CompletedTask);
        }

        await h.Sut.SeedAsync();

        Assert.Equal("Big Buck Bunny", h.Master.Caption);
        Assert.Equal("Big Buck Bunny", h.Branch.Caption);
    }
}
