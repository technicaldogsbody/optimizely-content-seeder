using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Statistic Element",
    GUID = "a1b2c3d4-0001-0000-0000-000000000018",
    Description = "A single highlighted number/label pair (e.g. '10,000+ customers'). Usable as a Visual Builder element.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.ElementEnabledKey])]
public class StatisticElementBlock : BlockData
{
    [CultureSpecific]
    [Display(Name = "Value", Order = 10)]
    public virtual string? Value { get; set; }

    [CultureSpecific]
    [Display(Name = "Label", Order = 20)]
    public virtual string? Label { get; set; }
}
