using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Rich Text Element",
    GUID = "a1b2c3d4-0001-0000-0000-000000000014",
    Description = "A block of formatted body copy. Usable as a Visual Builder element.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.ElementEnabledKey])]
public class RichTextElementBlock : BlockData
{
    [CultureSpecific]
    [Display(Name = "Body", Order = 10)]
    public virtual XhtmlString? Body { get; set; }
}
