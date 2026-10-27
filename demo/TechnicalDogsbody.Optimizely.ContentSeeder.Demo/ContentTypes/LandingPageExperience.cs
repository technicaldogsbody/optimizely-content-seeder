using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.VisualBuilder;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Landing Page Experience",
    GUID = "a1b2c3d4-0001-0000-0000-000000000011",
    Description = "A general-purpose marketing landing page, composed in Visual Builder from sections and elements.",
    GroupName = "Demo")]
public class LandingPageExperience : ExperienceData
{
    [CultureSpecific]
    [Display(Name = "Heading", Order = 10)]
    public virtual string? Heading { get; set; }

    [CultureSpecific]
    [Display(Name = "Tagline", Order = 20)]
    public virtual string? Tagline { get; set; }

    [Display(Name = "Hero Image", Order = 30)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? HeroImage { get; set; }

    [CultureSpecific]
    [Display(Name = "Meta Description", Order = 40)]
    public virtual string? MetaDescription { get; set; }
}
