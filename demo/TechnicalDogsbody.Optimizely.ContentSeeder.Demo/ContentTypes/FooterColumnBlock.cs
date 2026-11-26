using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Web;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

/// <summary>One column of the footer, used as an item of <see cref="StartPage.FooterColumns"/>.</summary>
[ContentType(
    DisplayName = "Footer Column",
    GUID = "a1b2c3d4-0001-0000-0000-000000000401",
    Description = "A column of footer links.",
    GroupName = "Demo",
    AvailableInEditMode = false)]
public class FooterColumnBlock : BlockData
{
    [Display(Name = "Heading", Order = 10)]
    public virtual string? Heading { get; set; }

    [Display(Name = "Logo", Order = 20)]
    [UIHint(UIHint.Image)]
    public virtual ContentReference? Logo { get; set; }

    [Display(Name = "Links", Order = 30)]
    public virtual IList<FooterLinkBlock>? Links { get; set; }
}
