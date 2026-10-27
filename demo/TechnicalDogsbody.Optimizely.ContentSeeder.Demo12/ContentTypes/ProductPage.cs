using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

[ContentType(
    DisplayName = "Product Page",
    GUID = "a1b2c3d4-0001-0000-0000-000000000003",
    Description = "A product content page.",
    GroupName = "Demo")]
public class ProductPage : PageData
{
    [CultureSpecific]
    [Display(Name = "Product Name", Order = 10)]
    public virtual string? ProductName { get; set; }

    [CultureSpecific]
    [Display(Name = "Description", Order = 20)]
    public virtual XhtmlString? Description { get; set; }

    [Display(Name = "Price", Order = 30)]
    public virtual double Price { get; set; }

    [CultureSpecific]
    [Display(Name = "Product Image", Order = 40)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? ProductImage { get; set; }

    [Display(Name = "In Stock", Order = 50)]
    public virtual bool InStock { get; set; }

    [Display(Name = "Teaser Area", Order = 60)]
    public virtual ContentArea? TeaserArea { get; set; }
}
