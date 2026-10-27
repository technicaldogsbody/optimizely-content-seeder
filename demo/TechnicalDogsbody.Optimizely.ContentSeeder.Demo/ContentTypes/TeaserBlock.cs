using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Teaser Block",
    GUID = "a1b2c3d4-0001-0000-0000-000000000006",
    Description = "A teaser block.",
    GroupName = "Demo")]
public class TeaserBlock : BlockData
{
    [CultureSpecific]
    [Display(Name = "Heading", Order = 10)]
    public virtual string? Heading { get; set; }

    [CultureSpecific]
    [Display(Name = "Summary", Order = 20)]
    public virtual string? Summary { get; set; }

    [CultureSpecific]
    [Display(Name = "Thumbnail Image", Order = 30)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? ThumbnailImage { get; set; }
}
