using System.Collections;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Video properties for a definition: where each one's value comes from. Build one with <c>SeedFactory.Video(...)</c>
/// and pass it as <c>videos</c>; chain <see cref="Video"/> for more than one property.
/// <code>
/// videos: Video(nameof(VideoBlock.VideoUrl), VideoSource.YouTube("@BlenderOfficial", titleProperty: nameof(VideoBlock.Caption)))
///     .Video(nameof(VideoBlock.Video), VideoSource.Uploaded())
/// </code>
/// It is also the plain <c>videos</c> dictionary, so it can be passed anywhere one is accepted.
/// </summary>
public sealed class VideoSet : IReadOnlyDictionary<string, VideoSource>
{
    private readonly Dictionary<string, VideoSource> _sources = new(StringComparer.Ordinal);

    /// <summary>Adds (or replaces) a video property, and returns this set so more can be chained.</summary>
    /// <param name="property">The property name, e.g. <c>nameof(VideoBlock.VideoUrl)</c>.</param>
    /// <param name="source">Where the video comes from (see <see cref="VideoSource"/>).</param>
    public VideoSet Video(string property, VideoSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        ArgumentNullException.ThrowIfNull(source);
        _sources[property] = source;
        return this;
    }

    // ── IReadOnlyDictionary<string, VideoSource> ─────────────────────────────

    /// <inheritdoc/>
    public VideoSource this[string key] => _sources[key];

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _sources.Keys;

    /// <inheritdoc/>
    public IEnumerable<VideoSource> Values => _sources.Values;

    /// <inheritdoc/>
    public int Count => _sources.Count;

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _sources.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out VideoSource value) => _sources.TryGetValue(key, out value!);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, VideoSource>> GetEnumerator() => _sources.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
