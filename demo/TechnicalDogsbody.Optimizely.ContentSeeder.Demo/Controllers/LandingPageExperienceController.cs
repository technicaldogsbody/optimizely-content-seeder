using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Controllers;

public class LandingPageExperienceController : PageController<LandingPageExperience>
{
    public IActionResult Index(LandingPageExperience currentPage) => View(currentPage);
}
