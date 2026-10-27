using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.VisualBuilder;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "CTA Section",
    GUID = "a1b2c3d4-0001-0000-0000-000000000224",
    Description =
        "A prominent call-to-action section with centered text and button. Usable as a Visual Builder section.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.SectionEnabledKey])]
public class CTASection : BlockData
{
    [CultureSpecific]
    [Display(Name = "Heading", Order = 10)]
    public virtual string? Heading { get; set; }

    [CultureSpecific]
    [Display(Name = "Description", Order = 20)]
    public virtual string? Description { get; set; }

    [CultureSpecific]
    [Display(Name = "Button Text", Order = 30)]
    public virtual string? ButtonText { get; set; }

    [Display(Name = "Button URL", Order = 40)]
    public virtual string? ButtonUrl { get; set; }

    [Display(Name = "Button Opens in New Tab", Order = 50)]
    public virtual bool ButtonOpenInNewTab { get; set; }

    [Display(Name = "Background Color", Order = 60)]
    public virtual string? BackgroundColor { get; set; }
}
