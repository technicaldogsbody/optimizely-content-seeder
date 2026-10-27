using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Image Element",
    GUID = "a1b2c3d4-0001-0000-0000-000000000015",
    Description = "A single image with optional caption. Usable as a Visual Builder element.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.ElementEnabledKey])]
public class ImageElementBlock : BlockData
{
    [Display(Name = "Image", Order = 10)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? Image { get; set; }

    [CultureSpecific]
    [Display(Name = "Alt Text", Order = 20)]
    public virtual string? AltText { get; set; }

    [CultureSpecific]
    [Display(Name = "Caption", Order = 30)]
    public virtual string? Caption { get; set; }
}
