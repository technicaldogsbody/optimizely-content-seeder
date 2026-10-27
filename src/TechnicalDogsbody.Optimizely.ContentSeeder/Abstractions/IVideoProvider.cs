using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// A video chosen for a property. Exactly one of <see cref="Url"/> or <see cref="Asset"/> is set.
/// </summary>
/// <param name="Url">The watch URL, for a YouTube or Vimeo video.</param>
/// <param name="Asset">The media asset, for the uploaded sample video.</param>
/// <param name="Title">The video's own title, when the source knows it.</param>
public sealed record ResolvedVideo(string? Url, ContentHandle? Asset, string? Title);

/// <summary>
/// Chooses the video for a <see cref="VideoSource"/>. The default implementation reads YouTube and Vimeo public feeds and
/// downloads the sample video; replace it to source videos some other way.
/// Scoped lifetime — resolved fresh per seed operation, and called from several threads at once.
/// </summary>
public interface IVideoProvider
{
    /// <summary>
    /// Returns a video for the source, or null if there is none (the property is then left as it was).
    /// Never throws except for cancellation.
    /// </summary>
    Task<ResolvedVideo?> ResolveAsync(VideoSource source, CancellationToken ct = default);
}
