using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using EPiServer.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>
/// Default <see cref="IVideoProvider"/>. YouTube and Vimeo videos come from each site's public feed (no API key);
/// the sample video is downloaded once and saved as a media asset.
/// <para>
/// Each feed is read once per run however many properties use it, and picks come from a shuffled bag so a run does not
/// repeat a video until the feed is used up. A feed that cannot be read falls back to the source's fallback URLs, and
/// failing that returns null: seeding never fails because a video site was unreachable.
/// </para>
/// </summary>
public sealed class VideoProvider(
    IHttpClientFactory httpClientFactory,
    SeededMediaWriter mediaWriter,
    IOptions<ContentSeederOptions> options,
    ILogger<VideoProvider> logger) : IVideoProvider
{
    private const string SampleName = "seed-video.mp4";
    private static readonly TimeSpan _feedTimeout = TimeSpan.FromSeconds(20);

    private static readonly XNamespace _atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace _yt = "http://www.youtube.com/xml/schemas/2015";

    /// <summary>
    /// A video taken from a channel or user feed: its watch URL and its title.
    /// </summary>
    internal sealed record FeedVideo(string Url, string? Title);

    // One request per feed per run. Lazy so concurrent first callers share one fetch.
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<FeedVideo>>>> _feeds = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<ContentReference?>>> _samples = new();

    // A shuffled queue per source, refilled when empty.
    private readonly Dictionary<string, Queue<FeedVideo>> _bags = new();
    private readonly object _bagLock = new();

    /// <inheritdoc/>
    public async Task<ResolvedVideo?> ResolveAsync(VideoSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            return source.Kind switch
            {
                VideoSourceKind.Uploaded => await ResolveSampleAsync(source, ct).ConfigureAwait(false),
                VideoSourceKind.UrlList => Pick($"list:{string.Join('|', source.Links)}", source.Links.Select(ToVideo).ToList()),
                _ => await ResolveFeedAsync(source, ct).ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ContentSeeder: Could not choose a video for {Source}. The property is left as it was.", source.Describe());
            return null;
        }
    }

    // ── feeds ────────────────────────────────────────────────────────────────

    private async Task<ResolvedVideo?> ResolveFeedAsync(VideoSource source, CancellationToken ct)
    {
        string? feedUrl = await ResolveFeedUrlAsync(source, ct).ConfigureAwait(false);
        IReadOnlyList<FeedVideo> videos = feedUrl is null
            ? []
            : await _feeds
                .GetOrAdd(feedUrl, url => new Lazy<Task<IReadOnlyList<FeedVideo>>>(() => ReadFeedAsync(source, url, ct)))
                .Value.ConfigureAwait(false);

        if (videos.Count == 0 && source.Fallback.Count > 0)
            videos = source.Fallback.Select(ToVideo).ToList();

        return Pick(feedUrl ?? source.Describe(), videos);
    }

    // A YouTube handle ("@BlenderOfficial") isn't a feed id, so read the channel's page once for the feed URL it advertises.
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _handles = new(StringComparer.OrdinalIgnoreCase);

    private Task<string?> ResolveFeedUrlAsync(VideoSource source, CancellationToken ct)
        => source.Kind == VideoSourceKind.YouTubeChannel && source.Id!.StartsWith('@')
            ? _handles.GetOrAdd(source.Id, handle => new Lazy<Task<string?>>(() => LookUpHandleAsync(handle, source, ct))).Value
            : Task.FromResult<string?>(FeedUrl(source));

    private async Task<string?> LookUpHandleAsync(string handle, VideoSource source, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_feedTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.youtube.com/{Uri.EscapeDataString(handle).Replace("%40", "@")}");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; ContentSeeder)");
            request.Headers.TryAddWithoutValidation("Cookie", "CONSENT=YES+1; SOCS=CAI"); // skip the EU consent interstitial

            var client = httpClientFactory.CreateClient(ContentSeederNames.HttpClient);
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string page = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            var match = System.Text.RegularExpressions.Regex.Match(
                page, @"""rssUrl"":""(https://www\.youtube\.com/feeds/videos\.xml\?channel_id=UC[A-Za-z0-9_-]+)""");
            if (match.Success)
                return match.Groups[1].Value;

            logger.LogWarning("ContentSeeder: Could not find the feed for YouTube handle {Handle}{Fallback}. Use the channel id (UC…) instead.", handle, FallbackNote(source));
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ContentSeeder: Could not look up YouTube handle {Handle}{Fallback}. Use the channel id (UC…) instead.", handle, FallbackNote(source));
            return null;
        }
    }

    private async Task<IReadOnlyList<FeedVideo>> ReadFeedAsync(VideoSource source, string feedUrl, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_feedTimeout);

            var client = httpClientFactory.CreateClient(ContentSeederNames.HttpClient);
            string xml = await client.GetStringAsync(feedUrl, timeout.Token).ConfigureAwait(false);

            var videos = Parse(source.Kind, xml);
            if (videos.Count == 0)
                logger.LogWarning("ContentSeeder: The feed {Feed} had no videos{Fallback}.", feedUrl, FallbackNote(source));
            else
                logger.LogDebug("ContentSeeder: Read {Count} videos from {Feed}.", videos.Count, feedUrl);

            return videos;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ContentSeeder: Could not read the feed {Feed}{Fallback}.", feedUrl, FallbackNote(source));
            return [];
        }
    }

    private static string FallbackNote(VideoSource source)
        => source.Fallback.Count > 0 ? $"; using the {source.Fallback.Count} fallback URL(s)" : "; the property is left as it was";

    private static string FeedUrl(VideoSource source) => source.Kind switch
    {
        VideoSourceKind.YouTubeChannel => $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(source.Id!)}",
        VideoSourceKind.YouTubePlaylist => $"https://www.youtube.com/feeds/videos.xml?playlist_id={Uri.EscapeDataString(source.Id!)}",
        VideoSourceKind.VimeoChannel => $"https://vimeo.com/channels/{Uri.EscapeDataString(source.Id!)}/videos/rss",
        VideoSourceKind.VimeoUser => $"https://vimeo.com/{Uri.EscapeDataString(source.Id!)}/videos/rss",
        _ => throw new InvalidOperationException($"{source.Kind} has no feed."),
    };

    // Parses YouTube's Atom feed or Vimeo's RSS feed. Entries that aren't a recognisable video are skipped.
    /// <summary>
    /// Reads a YouTube Atom feed or a Vimeo RSS feed. Entries that are not recognisable videos are skipped.
    /// </summary>
    internal static IReadOnlyList<FeedVideo> Parse(VideoSourceKind kind, string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var document = XDocument.Load(reader);

        var videos = new List<FeedVideo>();
        if (kind is VideoSourceKind.YouTubeChannel or VideoSourceKind.YouTubePlaylist)
        {
            foreach (var entry in document.Descendants(_atom + "entry"))
            {
                string? id = entry.Element(_yt + "videoId")?.Value;
                if (id is not null && VideoUrls.TryParse($"https://www.youtube.com/watch?v={id}", out var video))
                    videos.Add(new FeedVideo(video.WatchUrl, entry.Element(_atom + "title")?.Value));
            }
        }
        else
        {
            foreach (var item in document.Descendants("item"))
            {
                if (VideoUrls.TryParse(item.Element("link")?.Value, out var video))
                    videos.Add(new FeedVideo(video.WatchUrl, item.Element("title")?.Value));
            }
        }

        return videos;
    }

    // ── choosing ─────────────────────────────────────────────────────────────

    private static FeedVideo ToVideo(string url)
        => VideoUrls.TryParse(url, out var video) ? new FeedVideo(video.WatchUrl, null) : new FeedVideo(url, null);

    private ResolvedVideo? Pick(string bagKey, IReadOnlyList<FeedVideo> videos)
    {
        if (videos.Count == 0)
            return null;

        FeedVideo chosen;
        lock (_bagLock)
        {
            if (!_bags.TryGetValue(bagKey, out var bag))
                _bags[bagKey] = bag = new Queue<FeedVideo>();

            if (bag.Count == 0)
            {
                foreach (var video in videos.OrderBy(_ => Random.Shared.Next()))
                    bag.Enqueue(video);
            }

            chosen = bag.Dequeue();
        }

        return new ResolvedVideo(chosen.Url, null, chosen.Title);
    }

    // ── the sample video ─────────────────────────────────────────────────────

    private async Task<ResolvedVideo?> ResolveSampleAsync(VideoSource source, CancellationToken ct)
    {
        string url = source.Links.FirstOrDefault() ?? options.Value.SampleVideoUrl;
        var asset = await _samples
            .GetOrAdd(url, u => new Lazy<Task<ContentReference?>>(() => CreateSampleAsync(u, ct)))
            .Value.ConfigureAwait(false);

        return asset is null ? null : new ResolvedVideo(null, asset.ToHandle(), null);
    }

    private async Task<ContentReference?> CreateSampleAsync(string url, CancellationToken ct)
    {
        var videoType = mediaWriter.FindMediaType(typeof(VideoData), ".mp4");
        if (videoType is null)
        {
            logger.LogWarning(
                "ContentSeeder: No VideoData subtype is registered. " +
                "Add a class that extends EPiServer.Core.VideoData with a [ContentType] attribute " +
                "to enable seeded video files.");
            return null;
        }

        try
        {
            // A resumed Seed finds the video it saved last time rather than downloading and saving it again.
            if (mediaWriter.FindByName(SampleName) is { } existing && !ContentReference.IsNullOrEmpty(existing))
                return existing;

            var client = httpClientFactory.CreateClient(ContentSeederNames.HttpClient);
            byte[] bytes = await client.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            if (bytes.Length == 0)
                throw new InvalidOperationException("The download was empty.");

            return await mediaWriter.SaveAsync(videoType, SampleName, bytes, ".mp4", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ContentSeeder: Could not download or save the sample video from {Url}. Uploaded video properties are left empty. " +
                "Set ContentSeeder:SampleVideoUrl to a reachable .mp4.", url);
            return null;
        }
    }
}
