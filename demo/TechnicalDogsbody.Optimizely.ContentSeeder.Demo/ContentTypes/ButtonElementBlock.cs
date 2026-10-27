using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Button Element",
    GUID = "a1b2c3d4-0001-0000-0000-000000000013",
    Description = "A single call-to-action button. Usable as a Visual Builder element.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.ElementEnabledKey])]
public class ButtonElementBlock : BlockData
{
    [CultureSpecific]
    [Display(Name = "Button Text", Order = 10)]
    public virtual string? ButtonText { get; set; }

    [Display(Name = "Button URL", Order = 20)]
    public virtual string? ButtonUrl { get; set; }

    [Display(Name = "Open In New Tab", Order = 30)]
    public virtual bool OpenInNewTab { get; set; }
}
