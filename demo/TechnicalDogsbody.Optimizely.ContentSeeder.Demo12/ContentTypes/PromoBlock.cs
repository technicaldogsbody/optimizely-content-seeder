using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

[ContentType(
    DisplayName = "Promo Block",
    GUID = "a1b2c3d4-0001-0000-0000-000000000005",
    Description = "A promotional block.",
    GroupName = "Demo")]
public class PromoBlock : BlockData
{
    [CultureSpecific]
    [Display(Name = "Title", Order = 10)]
    public virtual string? Title { get; set; }

    [CultureSpecific]
    [Display(Name = "Body", Order = 20)]
    public virtual XhtmlString? Body { get; set; }

    [CultureSpecific]
    [Display(Name = "Image", Order = 30)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? Image { get; set; }

    [CultureSpecific]
    [Display(Name = "Link URL", Order = 40)]
    public virtual string? LinkUrl { get; set; }
}
