using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.DataAnnotations;
using EPiServer.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentProperties;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Chooses and writes videos for the declarations in a seed definition. Never throws: a video that can't be
/// chosen leaves the property as it was.
/// </summary>
internal sealed class VideoApplier(IVideoProvider videoProvider, ILogger logger)
{
    private readonly ConcurrentDictionary<string, bool> _videoWarnings = new();

    // Reports a problem with a video declaration once per run, not once per item.
    private void WarnVideoOnce(string key, string message, params object?[] args)
    {
        if (_videoWarnings.TryAdd(key, true))
            logger.LogWarning(message, args);
    }

    // Chooses a video for each property named in the definition and writes it, plus the video's title when asked.
    // Runs after the content provider and before the definition's configure callback, so configure still wins.
    // Never throws: a video that can't be chosen leaves the property as it was.
    /// <summary>
    /// Chooses a video for each video property of an item and writes it, plus the title when asked. Runs after the content provider and before the configure callback, so configure still wins. A video that cannot be chosen is skipped.
    /// </summary>
    internal async Task ApplyVideosAsync(object content, IReadOnlyDictionary<string, VideoSource> videos, CancellationToken ct)
    {
        if (videos.Count == 0)
            return;

        var type = content.GetOriginalType();
        foreach (var (propertyName, source) in videos)
        {
            ct.ThrowIfCancellationRequested();

            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            string key = $"{type.FullName}.{propertyName}";
            if (property is null || !property.CanWrite)
            {
                WarnVideoOnce(key, "ContentSeeder: {Type} has no writable property '{Property}' for a video.", type.Name, propertyName);
                continue;
            }

            bool wantsAsset = source.Kind == VideoSourceKind.Uploaded;
            bool isAsset = property.PropertyType == typeof(ContentReference);
            bool isText = property.PropertyType == typeof(string) || property.PropertyType == typeof(Url);
            if (wantsAsset ? !isAsset : !isText)
            {
                WarnVideoOnce(key, wantsAsset
                    ? "ContentSeeder: {Type}.{Property} must be a ContentReference to hold an uploaded video, but is {PropertyType}."
                    : "ContentSeeder: {Type}.{Property} must be a string (or Url) to hold a video URL, but is {PropertyType}.",
                    type.Name, propertyName, property.PropertyType.Name);
                continue;
            }

            var video = await videoProvider.ResolveAsync(source, ct).ConfigureAwait(false);
            if (video is null)
                continue;

            if (wantsAsset)
            {
                if (video.Asset is { IsEmpty: false } asset)
                    property.SetValue(content, asset.ToReference());
            }
            else if (video.Url is not null)
            {
                // Boxed explicitly: Url converts implicitly from string, so a plain ?: would turn the string into a Url.
                object value = property.PropertyType == typeof(Url) ? new Url(video.Url) : (object)video.Url;
                property.SetValue(content, value);
            }

            if (source.TitleProperty is { } titleName && video.Title is not null)
            {
                var title = type.GetProperty(titleName, BindingFlags.Public | BindingFlags.Instance);
                if (title is { CanWrite: true } && title.PropertyType == typeof(string))
                    title.SetValue(content, video.Title);
                else
                    WarnVideoOnce($"{key}->{titleName}", "ContentSeeder: {Type}.{Property} cannot receive the video title: it must be a writable string.", type.Name, titleName);
            }
        }
    }

    // A video's title belongs to the video, not to a language, so a language version keeps the master's rather
    // than a translated or generated one that no longer matches the video.
    /// <summary>
    /// Copies the video titles from the master to a language branch. A title belongs to the video, not to the language.
    /// </summary>
    internal static void CopyVideoTitles(object master, object branch, IReadOnlyDictionary<string, VideoSource> videos)
    {
        var type = branch.GetOriginalType();
        foreach (string titleName in videos.Values.Select(v => v.TitleProperty).OfType<string>().Distinct())
        {
            var title = type.GetProperty(titleName, BindingFlags.Public | BindingFlags.Instance);
            if (title is { CanWrite: true } && title.PropertyType == typeof(string) && title.GetValue(master) is string text)
                title.SetValue(branch, text);
        }
    }

    // An inline element in another language is a clone of the master's that the provider then rewrote. Writing
    // a fresh random value over a YouTube/Vimeo URL would swap the video, so put the master's URL back.
    /// <summary>
    /// Puts the source's video URLs back after a translation, so a provider that rewrites text cannot swap the video.
    /// </summary>
    internal static void PreserveVideos(object source, object target)
    {
        foreach (var prop in GetContentProperties(source.GetOriginalType()))
        {
            if (prop.PropertyType == typeof(string)
                && prop.GetValue(source) is string url
                && VideoUrls.TryParse(url, out _))
            {
                prop.SetValue(target, url);
            }
        }
    }
}
