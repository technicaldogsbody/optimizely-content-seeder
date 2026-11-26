using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Validation;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Start Page",
    GUID = "a1b2c3d4-0001-0000-0000-000000000001",
    Description = "The root page of the site.",
    GroupName = "Demo")]
public class StartPage : PageData
{
    [Display(Name = "Hero Area", Order = 10)]
    public virtual ContentArea? HeroArea { get; set; }

    [Display(Name = "Promo Area", Order = 20)]
    public virtual ContentArea? PromoArea { get; set; }

    // A local block: ContentSeeder fills it in place. The CMS does not allow [CultureSpecific] here, so the block's own
    // text properties are culture-specific, and ContentSeeder translates those for each language.
    [Display(Name = "Banner", Order = 30)]
    public virtual SiteBannerBlock? Banner { get; set; }

    // A block list with the site's own bounds: ContentSeeder creates 1 to 4 columns, so the page can be published.
    [Display(Name = "Footer columns", Description = "The link columns shown in the footer. Add up to 4.", Order = 200)]
    [MinElements(1)]
    [MaxElements(4)]
    public virtual IList<FooterColumnBlock>? FooterColumns { get; set; }
}
