using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.Controllers;

public class StartPageController : PageController<StartPage>
{
    public IActionResult Index(StartPage currentPage) => View(currentPage);
}
