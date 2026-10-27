using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

/// <summary>
/// A video: an uploaded file, or a YouTube / Vimeo link. One type that works everywhere — as a block in a
/// <c>ContentArea</c>, as a Visual Builder section, and as a Visual Builder element.
/// </summary>
// The GUID is the one VideoElementBlock had, so Optimizely treats this as that type renamed rather than a new one.
[ContentType(
    DisplayName = "Video",
    GUID = "a1b2c3d4-0001-0000-0000-000000000016",
    Description = "A video: an uploaded file or a YouTube / Vimeo link. Usable as a block, a Visual Builder section and a Visual Builder element.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.SectionEnabledKey, CompositionBehavior.ElementEnabledKey])]
public class VideoBlock : BlockData
{
    [Display(Name = "Video file", Description = "An uploaded video. Used instead of the link below when set.", Order = 10)]
    [UIHint(UIHint.Video)]
    [AllowedTypes(typeof(VideoData))]
    public virtual ContentReference? Video { get; set; }

    [Display(Name = "Video URL", Description = "A YouTube or Vimeo link.", Order = 20)]
    public virtual string? VideoUrl { get; set; }

    [CultureSpecific]
    [Display(Name = "Caption", Order = 30)]
    public virtual string? Caption { get; set; }
}
