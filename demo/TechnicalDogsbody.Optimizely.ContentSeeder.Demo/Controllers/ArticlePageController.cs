using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Controllers;

public class ArticlePageController : PageController<ArticlePage>
{
    public IActionResult Index(ArticlePage currentPage) => View(currentPage);
}
