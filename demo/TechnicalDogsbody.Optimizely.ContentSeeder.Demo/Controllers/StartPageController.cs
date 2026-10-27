using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Controllers;

public class StartPageController : PageController<StartPage>
{
    public IActionResult Index(StartPage currentPage) => View(currentPage);
}
