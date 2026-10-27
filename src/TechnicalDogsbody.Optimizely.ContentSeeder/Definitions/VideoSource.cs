namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>Where a seeded video comes from.</summary>
public enum VideoSourceKind
{
    /// <summary>A small sample video, downloaded once and saved as a media asset that every property shares.</summary>
    Uploaded,
    /// <summary>A random video from a YouTube channel's public feed.</summary>
    YouTubeChannel,
    /// <summary>A random video from a YouTube playlist's public feed.</summary>
    YouTubePlaylist,
    /// <summary>A random video from a Vimeo channel's public feed.</summary>
    VimeoChannel,
    /// <summary>A random video from a Vimeo user's public feed.</summary>
    VimeoUser,
    /// <summary>A random video from a fixed list of URLs.</summary>
    UrlList,
}

/// <summary>
/// Says where one video property's value comes from, for the <c>videos</c> parameter of a definition
/// (the video equivalent of <c>imageSizes</c>): the key is the property name.
/// <example>
/// <code>
/// videos: new Dictionary&lt;string, VideoSource&gt;
/// {
///     [nameof(VideoBlock.VideoUrl)] = VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg", titleProperty: nameof(VideoBlock.Caption)),
///     [nameof(VideoBlock.Video)]    = VideoSource.Uploaded(),
/// }
/// </code>
/// </example>
/// Feeds are the public ones each site publishes, so no API key is needed. Property types: <see cref="Uploaded"/>
/// needs a <c>ContentReference</c>; every other source stores a watch URL in a <c>string</c> property.
/// </summary>
public sealed class VideoSource
{
    /// <summary>What kind of source this is.</summary>
    public VideoSourceKind Kind { get; }

    /// <summary>The channel id, playlist id or Vimeo channel name. Null for <see cref="VideoSourceKind.Uploaded"/> and URL lists.</summary>
    public string? Id { get; }

    /// <summary>The URLs of a <see cref="VideoSourceKind.UrlList"/>, or the sample video's URL for <see cref="VideoSourceKind.Uploaded"/> when overridden.</summary>
    public IReadOnlyList<string> Links { get; }

    /// <summary>
    /// When set, the chosen video's own title is written to this string property (e.g. a caption), so the text matches
    /// the video. It replaces any generated or AI-written text for that property.
    /// </summary>
    public string? TitleProperty { get; }

    /// <summary>Watch URLs to choose from when a feed cannot be read. Empty means the property is left as it was.</summary>
    public IReadOnlyList<string> Fallback { get; }

    private VideoSource(
        VideoSourceKind kind, string? id, IReadOnlyList<string>? urls, string? titleProperty, IReadOnlyList<string>? fallback)
    {
        Kind = kind;
        Id = id;
        Links = urls ?? [];
        TitleProperty = string.IsNullOrWhiteSpace(titleProperty) ? null : titleProperty;
        Fallback = fallback ?? [];
    }

    /// <summary>
    /// A small sample video, saved once as a media asset in the seeded media folder and shared by every property that
    /// asks for it. Needs a <c>ContentReference</c> property and a registered <c>VideoData</c> content type.
    /// </summary>
    /// <param name="url">The video to download. Null uses <c>ContentSeederOptions.SampleVideoUrl</c>.</param>
    public static VideoSource Uploaded(string? url = null)
        => new(VideoSourceKind.Uploaded, null, url is null ? null : [url], null, null);

    /// <summary>
    /// A random recent video from a YouTube channel: its id (starts with "UC") or its handle (e.g. <c>"@BlenderOfficial"</c>,
    /// which is looked up from the channel page, so prefer the id if you need it to be rock solid).
    /// </summary>
    public static VideoSource YouTube(string channelId, string? titleProperty = null, IReadOnlyList<string>? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        return new(VideoSourceKind.YouTubeChannel, channelId.Trim(), null, titleProperty, fallback);
    }

    /// <summary>A random video from a YouTube playlist (the id starts with "PL").</summary>
    public static VideoSource YouTubePlaylist(string playlistId, string? titleProperty = null, IReadOnlyList<string>? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        return new(VideoSourceKind.YouTubePlaylist, playlistId.Trim(), null, titleProperty, fallback);
    }

    /// <summary>
    /// A random recent video from a Vimeo channel, e.g. <c>"staffpicks"</c> for vimeo.com/channels/staffpicks.
    /// For a user (vimeo.com/someone) use <see cref="VimeoUser"/>.
    /// </summary>
    public static VideoSource Vimeo(string channel, string? titleProperty = null, IReadOnlyList<string>? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        return new(VideoSourceKind.VimeoChannel, channel.Trim(), null, titleProperty, fallback);
    }


    /// <summary>A random recent video from a Vimeo user's page, e.g. <c>"yourown3dsoftware"</c> for vimeo.com/yourown3dsoftware.</summary>
    public static VideoSource VimeoUser(string user, string? titleProperty = null, IReadOnlyList<string>? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        return new(VideoSourceKind.VimeoUser, user.Trim(), null, titleProperty, fallback);
    }
    /// <summary>A random video from a fixed list of YouTube or Vimeo URLs (watch, short or embed form).</summary>
    public static VideoSource Urls(params string[] urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        if (urls.Length == 0 || urls.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Provide at least one non-empty URL.", nameof(urls));

        return new(VideoSourceKind.UrlList, null, urls, null, null);
    }

    /// <summary>A stable one-line description, used to detect that a definition has changed.</summary>
    public string Describe()
        => string.Join(':', Kind, Id ?? string.Join(',', Links), TitleProperty, string.Join(',', Fallback));
}
