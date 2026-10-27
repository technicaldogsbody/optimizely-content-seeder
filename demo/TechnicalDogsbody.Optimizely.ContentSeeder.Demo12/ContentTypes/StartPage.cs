using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

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
}
