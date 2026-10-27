using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

[ContentType(
    DisplayName = "Hero Block",
    GUID = "a1b2c3d4-0001-0000-0000-000000000004",
    Description = "A hero banner block.",
    GroupName = "Demo")]
public class HeroBlock : BlockData
{
    [CultureSpecific]
    [Display(Name = "Heading", Order = 10)]
    public virtual string? Heading { get; set; }

    [CultureSpecific]
    [Display(Name = "Sub Heading", Order = 20)]
    public virtual string? SubHeading { get; set; }

    [CultureSpecific]
    [Display(Name = "Background Image", Order = 30)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? BackgroundImage { get; set; }

    [CultureSpecific]
    [Display(Name = "CTA Text", Order = 40)]
    public virtual string? CtaText { get; set; }

    [CultureSpecific]
    [Display(Name = "CTA URL", Order = 50)]
    public virtual string? CtaUrl { get; set; }
}
