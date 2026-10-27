using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Framework.Blobs;
using EPiServer.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class VideoProviderTests
{
    private class FakeVideoData : VideoData { }

    private const string YouTubeFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns="http://www.w3.org/2005/Atom">
          <title>Blender</title>
          <entry><id>yt:video:aaaaaaaaaaa</id><yt:videoId>aaaaaaaaaaa</yt:videoId><title>First video</title></entry>
          <entry><id>yt:video:bbbbbbbbbbb</id><yt:videoId>bbbbbbbbbbb</yt:videoId><title>Second video</title></entry>
          <entry><id>yt:video:ccccccccccc</id><yt:videoId>ccccccccccc</yt:videoId><title>Third &amp; last</title></entry>
        </feed>
        """;

    // The first two <link>s belong to the channel, not to a video, and must be ignored.
    private const string VimeoFeed = """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0"><channel>
          <title>Videos</title><link>https://vimeo.com/yourown3dsoftware/videos</link>
          <image><link>https://vimeo.com/yourown3dsoftware/videos</link></image>
          <item><title>Sintel</title><link>https://vimeo.com/265010648</link></item>
          <item><title>Spring</title><link>https://vimeo.com/153608970</link></item>
        </channel></rss>
        """;

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!.ToString());
            await Task.Delay(15, cancellationToken); // let concurrent callers overlap
            return respond(request);
        }
    }

    private sealed class ListLogger : ILogger<VideoProvider>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private static HttpResponseMessage Text(string body, string contentType = "text/xml")
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private static (VideoProvider Provider, RoutingHandler Handler, ListLogger Log, Mock<IContentRepository> Repo) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        bool withVideoType = true,
        ContentSeederOptions? options = null)
    {
        var handler = new RoutingHandler(respond);
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(ContentSeederNames.HttpClient)).Returns(() => new HttpClient(handler, disposeHandler: false));

        var repo = new Mock<IContentRepository>();
        repo.Setup(r => r.GetDefault<FakeVideoData>(It.IsAny<ContentReference>())).Returns(() => new FakeVideoData());
        repo.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>())).Returns(() => new ContentFolder());
        int nextId = 100;
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(() => new ContentReference(Interlocked.Increment(ref nextId)));

        var loader = new Mock<IContentLoader>();
        loader.Setup(l => l.GetChildren<ContentFolder>(It.IsAny<ContentReference>())).Returns([]);

        var contentTypes = new Mock<IContentTypeRepository>();
        contentTypes.Setup(r => r.List()).Returns(withVideoType ? [new ContentType { ID = 1, ModelType = typeof(FakeVideoData) }] : []);
        contentTypes.Setup(r => r.Load(It.IsAny<Type>())).Returns(new ContentType { ID = 1 });

        var blob = new Mock<Blob>(new Uri("blob:fake"));
        blob.Setup(b => b.OpenWrite()).Returns(new MemoryStream());
        var blobs = new Mock<IBlobFactory>();
        blobs.Setup(f => f.CreateBlob(It.IsAny<Uri>(), It.IsAny<string>())).Returns(blob.Object);

        var opts = OptionsFactory.Create(options ?? new ContentSeederOptions());
        var writer = new SeededMediaWriter(repo.Object, loader.Object, blobs.Object, contentTypes.Object, opts, new SeedDefinitionAccessor(), NullLogger<SeededMediaWriter>.Instance);
        var log = new ListLogger();
        return (new VideoProvider(http.Object, writer, opts, log), handler, log, repo);
    }

    private static HttpResponseMessage Feeds(HttpRequestMessage request)
    {
        string url = request.RequestUri!.ToString();
        return url.Contains("youtube.com/feeds") ? Text(YouTubeFeed)
            : url.Contains("vimeo.com") && url.EndsWith("/rss") ? Text(VimeoFeed, "application/rss+xml")
            : Status(HttpStatusCode.NotFound);
    }

    // ── YouTube and Vimeo feeds ──────────────────────────────────────────────

    [Fact]
    public async Task YouTubeChannel_ReadsTheChannelFeed_AndReturnsAWatchUrlWithItsTitle()
    {
        var (provider, handler, _, _) = Create(Feeds);

        var video = await provider.ResolveAsync(VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg"));

        Assert.StartsWith("https://www.youtube.com/watch?v=", video!.Url);
        Assert.Null(video.Asset);
        Assert.Contains(video.Title, new[] { "First video", "Second video", "Third & last" });
        Assert.Equal("https://www.youtube.com/feeds/videos.xml?channel_id=UCSMOQeBJ2RAnuFungnQOxLg", Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task YouTubePlaylist_ReadsThePlaylistFeed()
    {
        var (provider, handler, _, _) = Create(Feeds);

        await provider.ResolveAsync(VideoSource.YouTubePlaylist("PLa1F2ddGya_-UvuAqHAksYnB0qL9yWDO6"));

        Assert.Equal("https://www.youtube.com/feeds/videos.xml?playlist_id=PLa1F2ddGya_-UvuAqHAksYnB0qL9yWDO6", Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task VimeoChannel_AndVimeoUser_UseTheirOwnFeeds_AndIgnoreNonVideoLinks()
    {
        var (provider, handler, _, _) = Create(Feeds);

        var channel = await provider.ResolveAsync(VideoSource.Vimeo("staffpicks"));
        var user = await provider.ResolveAsync(VideoSource.VimeoUser("yourown3dsoftware"));

        Assert.Equal(
            ["https://vimeo.com/channels/staffpicks/videos/rss", "https://vimeo.com/yourown3dsoftware/videos/rss"],
            handler.Requests.ToArray());
        Assert.Matches(@"^https://vimeo\.com/\d+$", channel!.Url);
        Assert.Contains(user!.Title, new[] { "Sintel", "Spring" });
    }

    [Fact]
    public async Task EachFeed_IsReadOnce_HoweverManyPropertiesAskForIt()
    {
        var (provider, handler, _, _) = Create(Feeds);

        var picks = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(_ => provider.ResolveAsync(VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg"))));

        Assert.All(picks, p => Assert.NotNull(p));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Picks_DoNotRepeatAVideoUntilTheFeedIsUsedUp()
    {
        var (provider, _, _, _) = Create(Feeds);
        var source = VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg");

        var first = new[] { await Pick(), await Pick(), await Pick() };
        var second = new[] { await Pick(), await Pick(), await Pick() };

        Assert.Equal(3, first.Distinct().Count());
        Assert.Equal(3, second.Distinct().Count());

        async Task<string?> Pick() => (await provider.ResolveAsync(source))!.Url;
    }

    [Fact]
    public async Task YouTubeHandle_IsLookedUpFromTheChannelPage_ThenItsFeedIsRead()
    {
        var (provider, handler, _, _) = Create(request =>
        {
            string url = request.RequestUri!.ToString();
            if (url == "https://www.youtube.com/@BlenderOfficial")
                return Text("""..."channelId":"UCother","rssUrl":"https://www.youtube.com/feeds/videos.xml?channel_id=UCSMOQeBJ2RAnuFungnQOxLg","x":1...""", "text/html");
            return Feeds(request);
        });

        var video = await provider.ResolveAsync(VideoSource.YouTube("@BlenderOfficial"));
        await provider.ResolveAsync(VideoSource.YouTube("@BlenderOfficial"));

        Assert.NotNull(video);
        Assert.Equal(
            ["https://www.youtube.com/@BlenderOfficial", "https://www.youtube.com/feeds/videos.xml?channel_id=UCSMOQeBJ2RAnuFungnQOxLg"],
            handler.Requests.ToArray());
    }

    // ── failure handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task UnreadableFeed_WithAFallback_UsesTheFallbackUrls()
    {
        var (provider, _, log, _) = Create(_ => Status(HttpStatusCode.InternalServerError));

        var video = await provider.ResolveAsync(
            VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg", fallback: ["https://youtu.be/aqz-KE-bpKQ"]));

        Assert.Equal("https://www.youtube.com/watch?v=aqz-KE-bpKQ", video!.Url);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task UnreadableFeed_WithNoFallback_ReturnsNull_AndWarnsOncePerFeed()
    {
        var (provider, handler, log, _) = Create(_ => throw new HttpRequestException("offline"));

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => provider.ResolveAsync(VideoSource.Vimeo("staffpicks"))));

        Assert.All(results, Assert.Null);
        Assert.Single(handler.Requests);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GarbageFeed_IsTreatedAsEmpty()
    {
        var (provider, _, _, _) = Create(_ => Text("<<< not xml"));

        Assert.Null(await provider.ResolveAsync(VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg")));
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var (provider, _, _, _) = Create(Feeds);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ResolveAsync(VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg"), cts.Token));
    }

    // ── URL lists ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UrlList_PicksOnlyFromTheList_InWatchForm_WithoutTouchingTheNetwork()
    {
        var (provider, handler, _, _) = Create(Feeds);
        var source = VideoSource.Urls("https://youtu.be/aqz-KE-bpKQ", "https://player.vimeo.com/video/265010648");

        var urls = new List<string?>();
        for (int i = 0; i < 6; i++)
            urls.Add((await provider.ResolveAsync(source))!.Url);

        Assert.All(urls, u => Assert.Contains(u, new[] { "https://www.youtube.com/watch?v=aqz-KE-bpKQ", "https://vimeo.com/265010648" }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void UrlList_RejectsAnEmptyList()
    {
        Assert.Throws<ArgumentException>(() => VideoSource.Urls());
        Assert.Throws<ArgumentException>(() => VideoSource.Urls("https://youtu.be/aqz-KE-bpKQ", " "));
    }

    // ── the sample video ─────────────────────────────────────────────────────

    [Fact]
    public async Task Uploaded_DownloadsTheSampleOnce_AndSavesOneAssetSharedByEveryCaller()
    {
        var (provider, handler, _, repo) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });

        var videos = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => provider.ResolveAsync(VideoSource.Uploaded())));

        Assert.All(videos, v => Assert.Equal(videos[0]!.Asset, v!.Asset));
        Assert.NotNull(videos[0]!.Asset);
        Assert.Null(videos[0]!.Url);
        Assert.Single(handler.Requests);
        Assert.Contains("Big_Buck_Bunny", handler.Requests.Single());
        repo.Verify(r => r.Save(It.Is<IContent>(c => c is FakeVideoData && c.Name == "seed-video.mp4"), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()), Times.Once);
    }

    [Fact]
    public async Task Uploaded_UsesTheConfiguredUrl_AndTheUrlGivenToUploaded()
    {
        var (provider, handler, _, _) = Create(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) },
            options: new ContentSeederOptions { SampleVideoUrl = "https://example.test/configured.mp4" });

        await provider.ResolveAsync(VideoSource.Uploaded());
        await provider.ResolveAsync(VideoSource.Uploaded("https://example.test/explicit.mp4"));

        Assert.Equal(["https://example.test/configured.mp4", "https://example.test/explicit.mp4"], handler.Requests.ToArray());
    }

    [Fact]
    public async Task Uploaded_WithNoVideoDataTypeRegistered_WarnsAndReturnsNull()
    {
        var (provider, handler, log, _) = Create(_ => Status(HttpStatusCode.OK), withVideoType: false);

        Assert.Null(await provider.ResolveAsync(VideoSource.Uploaded()));
        Assert.Empty(handler.Requests);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("VideoData"));
    }

    [Fact]
    public async Task Uploaded_WhenTheDownloadFails_WarnsAndReturnsNull()
    {
        var (provider, _, log, repo) = Create(_ => Status(HttpStatusCode.NotFound));

        Assert.Null(await provider.ResolveAsync(VideoSource.Uploaded()));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("SampleVideoUrl"));
        repo.Verify(r => r.Save(It.Is<IContent>(c => c is FakeVideoData), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()), Times.Never);
    }
}
