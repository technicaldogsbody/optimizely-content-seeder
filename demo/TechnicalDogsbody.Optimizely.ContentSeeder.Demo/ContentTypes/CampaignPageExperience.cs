using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.VisualBuilder;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Campaign Page Experience",
    GUID = "a1b2c3d4-0001-0000-0000-000000000012",
    Description = "A time-bound marketing campaign page, composed in Visual Builder from sections and elements.",
    GroupName = "Demo")]
public class CampaignPageExperience : ExperienceData
{
    [CultureSpecific]
    [Display(Name = "Campaign Name", Order = 10)]
    public virtual string? CampaignName { get; set; }

    [CultureSpecific]
    [Display(Name = "Tagline", Order = 20)]
    public virtual string? Tagline { get; set; }

    [Display(Name = "Start Date", Order = 30)]
    public virtual DateTime? StartDate { get; set; }

    [Display(Name = "End Date", Order = 40)]
    public virtual DateTime? EndDate { get; set; }

    [Display(Name = "Hero Image", Order = 50)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? HeroImage { get; set; }

    [CultureSpecific]
    [Display(Name = "Meta Description", Order = 60)]
    public virtual string? MetaDescription { get; set; }
}
