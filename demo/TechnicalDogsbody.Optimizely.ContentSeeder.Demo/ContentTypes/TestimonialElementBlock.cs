using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Testimonial Element",
    GUID = "a1b2c3d4-0001-0000-0000-000000000017",
    Description = "A customer quote with attribution. Usable as a Visual Builder element.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.ElementEnabledKey, CompositionBehavior.SectionEnabledKey])]
public class TestimonialElementBlock : BlockData
{
    [CultureSpecific]
    [Display(Name = "Quote", Order = 10)]
    public virtual string? Quote { get; set; }

    [CultureSpecific]
    [Display(Name = "Author Name", Order = 20)]
    public virtual string? AuthorName { get; set; }

    [CultureSpecific]
    [Display(Name = "Author Title", Order = 30)]
    public virtual string? AuthorTitle { get; set; }

    [Display(Name = "Author Photo", Order = 40)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? AuthorPhoto { get; set; }
}
