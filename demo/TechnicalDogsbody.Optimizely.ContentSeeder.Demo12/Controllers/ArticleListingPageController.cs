using EPiServer;
using EPiServer.Core;
using EPiServer.Filters;
using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.Controllers;

public class ArticleListingPageController(IContentLoader contentLoader) : PageController<ArticleListingPage>
{
    public IActionResult Index(ArticleListingPage currentPage, int page = 1)
    {
        // Only articles the current visitor may see, newest first. Paging is over that list, so a page never shows
        // a gap where a hidden article used to be.
        var articles = FilterForVisitor.Filter(contentLoader.GetChildren<ArticlePage>(currentPage.ContentLink))
            .OfType<ArticlePage>()
            .OrderByDescending(a => a.PublishedDate)
            .ToList();

        int size = Math.Max(1, currentPage.PageSize);
        int pageCount = Math.Max(1, (int)Math.Ceiling(articles.Count / (double)size));
        int current = Math.Clamp(page, 1, pageCount);

        var model = new ArticleListingViewModel(
            currentPage,
            articles.Skip((current - 1) * size).Take(size).ToList(),
            current,
            pageCount,
            articles.Count);

        return View(model);
    }
}

/// <summary>What the listing view needs: the page, this page's articles, and where the pager is.</summary>
public sealed record ArticleListingViewModel(
    ArticleListingPage Page,
    IReadOnlyList<ArticlePage> Articles,
    int CurrentPage,
    int PageCount,
    int TotalArticles);
