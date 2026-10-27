using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

/// <summary>
/// A listing of the <see cref="ArticlePage"/> children of this page, newest first, a fixed number per page.
/// The articles are the page's children, so the listing needs no content area.
/// </summary>
[ContentType(
    DisplayName = "Article Listing Page",
    GUID = "a1b2c3d4-0001-0000-0000-000000000031",
    Description = "Lists the article pages beneath it, with paging.",
    GroupName = "Demo")]
public class ArticleListingPage : PageData
{
    [CultureSpecific]
    [Display(Name = "Title", Order = 10)]
    public virtual string? Title { get; set; }

    [CultureSpecific]
    [Display(Name = "Introduction", Order = 20)]
    public virtual string? Introduction { get; set; }

    [Display(Name = "Articles per page", Description = "How many articles to show on each page.", Order = 30)]
    public virtual int PageSize { get; set; } = 6;
}
