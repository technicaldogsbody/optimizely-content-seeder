using System.Text.RegularExpressions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>The video sites the seeder understands.</summary>
public enum VideoSite
{
    /// <summary>YouTube, including youtu.be short links.</summary>
    YouTube,

    /// <summary>Vimeo.</summary>
    Vimeo,
}

/// <summary>A video on a known site, parsed from any of the URL forms people paste.</summary>
public sealed record VideoReference(VideoSite Site, string Id)
{
    /// <summary>The page URL an editor would paste: <c>https://www.youtube.com/watch?v=ID</c> or <c>https://vimeo.com/ID</c>.</summary>
    public string WatchUrl => Site == VideoSite.YouTube
        ? $"https://www.youtube.com/watch?v={Id}"
        : $"https://vimeo.com/{Id}";

    /// <summary>
    /// The URL to put in an iframe. YouTube uses the privacy-enhanced <c>youtube-nocookie.com</c> host, so the page
    /// sets no tracking cookies until the visitor plays the video.
    /// </summary>
    public string EmbedUrl => Site == VideoSite.YouTube
        ? $"https://www.youtube-nocookie.com/embed/{Id}"
        : $"https://player.vimeo.com/video/{Id}";
}

/// <summary>Recognises YouTube and Vimeo URLs (watch, short, embed and channel forms).</summary>
public static partial class VideoUrls
{
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex YouTubeId();

    [GeneratedRegex(@"^\d{4,}$")]
    private static partial Regex VimeoId();

    /// <summary>
    /// Parses a YouTube or Vimeo URL. Returns false for anything else (another site, a channel page, plain text),
    /// which callers should treat as "not a video I can embed".
    /// </summary>
    public static bool TryParse(string? url, out VideoReference reference)
    {
        reference = null!;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return false;

        string host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (host == "youtu.be")
            return TryYouTube(segments.FirstOrDefault(), out reference);

        if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com"
            or "youtube-nocookie.com" or "www.youtube-nocookie.com")
        {
            if (segments.Length >= 2 && segments[0] is "embed" or "shorts" or "live" or "v")
                return TryYouTube(segments[1], out reference);

            if (segments is ["watch"])
                return TryYouTube(QueryValue(uri.Query, "v"), out reference);

            return false;
        }

        if (host is "vimeo.com" or "www.vimeo.com" or "player.vimeo.com")
        {
            // vimeo.com/123456789, vimeo.com/channels/x/123456789, vimeo.com/groups/x/videos/123456789,
            // player.vimeo.com/video/123456789, vimeo.com/123456789/hash
            string? id = segments.FirstOrDefault(s => VimeoId().IsMatch(s));
            if (id is not null)
            {
                reference = new VideoReference(VideoSite.Vimeo, id);
                return true;
            }
        }

        return false;
    }

    private static bool TryYouTube(string? id, out VideoReference reference)
    {
        reference = null!;
        if (id is null || !YouTubeId().IsMatch(id))
            return false;

        reference = new VideoReference(VideoSite.YouTube, id);
        return true;
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] == key)
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        return null;
    }
}
