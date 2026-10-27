using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.VisualBuilder;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Feature Cards Section",
    GUID = "a1b2c3d4-0001-0000-0000-000000000323",
    Description =
        "A section for displaying a grid of feature cards with uniform layout. Usable as a Visual Builder section.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.SectionEnabledKey])]
public class FeatureCardsSection : BlockData
{
    [CultureSpecific]
    [Display(Name = "Section Heading", Order = 10)]
    public virtual string? Heading { get; set; }

    [CultureSpecific]
    [Display(Name = "Section Description", Order = 20)]
    public virtual string? Description { get; set; }

    [Display(Name = "Number of Columns", Order = 30)]
    public virtual int? ColumnCount { get; set; } = 3;
}
