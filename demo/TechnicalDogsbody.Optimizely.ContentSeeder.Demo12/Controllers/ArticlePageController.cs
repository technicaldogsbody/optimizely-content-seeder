using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.Controllers;

public class ArticlePageController : PageController<ArticlePage>
{
    public IActionResult Index(ArticlePage currentPage) => View(currentPage);
}
