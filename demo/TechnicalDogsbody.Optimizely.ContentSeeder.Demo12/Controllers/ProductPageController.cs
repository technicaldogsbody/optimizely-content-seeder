using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.Controllers;

public class ProductPageController : PageController<ProductPage>
{
    public IActionResult Index(ProductPage currentPage) => View(currentPage);
}
