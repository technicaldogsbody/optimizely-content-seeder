using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

/// <summary>A banner held as a local block on <see cref="StartPage.Banner"/>.</summary>
[ContentType(
    DisplayName = "Site Banner",
    GUID = "a1b2c3d4-0001-0000-0000-000000000403",
    Description = "A banner shown at the top of the start page.",
    GroupName = "Demo",
    AvailableInEditMode = false)]
public class SiteBannerBlock : BlockData
{
    [CultureSpecific]
    [Display(Name = "Heading", Order = 10)]
    public virtual string? Heading { get; set; }

    [CultureSpecific]
    [Display(Name = "Message", Order = 20)]
    public virtual string? Message { get; set; }

    [Display(Name = "Image", Order = 30)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? Image { get; set; }
}
