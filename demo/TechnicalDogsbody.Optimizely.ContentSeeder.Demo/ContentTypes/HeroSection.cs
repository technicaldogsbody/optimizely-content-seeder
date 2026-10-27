using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.Web;
using EPiServer.VisualBuilder;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Hero Section",
    GUID = "a1b2c3d4-0001-0000-0000-000000000222",
    Description =
        "A large banner section with background image, heading, tagline, and call-to-action. Usable as a Visual Builder section.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.SectionEnabledKey])]
public class HeroSection : BlockData
{
    [CultureSpecific]
    [Display(Name = "Heading", Order = 10)]
    public virtual string? Heading { get; set; }

    [CultureSpecific]
    [Display(Name = "Tagline", Order = 20)]
    public virtual string? Tagline { get; set; }

    [Display(Name = "Background Image", Order = 30)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? BackgroundImage { get; set; }

    [CultureSpecific]
    [Display(Name = "CTA Button Text", Order = 40)]
    public virtual string? CtaButtonText { get; set; }

    [Display(Name = "CTA Button URL", Order = 50)]
    public virtual string? CtaButtonUrl { get; set; }

    [Display(Name = "Button Opens in New Tab", Order = 60)]
    public virtual bool CtaOpenInNewTab { get; set; }
}
