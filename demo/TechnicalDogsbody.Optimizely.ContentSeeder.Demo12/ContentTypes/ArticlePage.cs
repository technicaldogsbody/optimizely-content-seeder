using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

[ContentType(
    DisplayName = "Article Page",
    GUID = "a1b2c3d4-0001-0000-0000-000000000002",
    Description = "An article content page.",
    GroupName = "Demo")]
public class ArticlePage : PageData
{
    [CultureSpecific]
    [Display(Name = "Title", Order = 10)]
    public virtual string? Title { get; set; }

    [CultureSpecific]
    [Display(Name = "Introduction", Order = 20)]
    public virtual string? Introduction { get; set; }

    [CultureSpecific]
    [Display(Name = "Body", Order = 30)]
    public virtual XhtmlString? Body { get; set; }

    [CultureSpecific]
    [Display(Name = "Hero Image", Order = 40)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? HeroImage { get; set; }

    [Display(Name = "Published Date", Order = 50)]
    public virtual DateTime PublishedDate { get; set; }

    [CultureSpecific]
    [Display(Name = "Author", Order = 60)]
    public virtual string? Author { get; set; }
}
