using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

/// <summary>A link in a footer column.</summary>
[ContentType(
    DisplayName = "Footer Link",
    GUID = "a1b2c3d4-0001-0000-0000-000000000402",
    Description = "A footer link.",
    GroupName = "Demo",
    AvailableInEditMode = false)]
public class FooterLinkBlock : BlockData
{
    [Display(Name = "Text", Order = 10)]
    public virtual string? Text { get; set; }

    [Display(Name = "URL", Order = 20)]
    public virtual string? Url { get; set; }
}
