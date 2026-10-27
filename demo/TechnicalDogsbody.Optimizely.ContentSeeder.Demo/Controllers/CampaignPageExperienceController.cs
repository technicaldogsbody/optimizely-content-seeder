using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Controllers;

public class CampaignPageExperienceController : PageController<CampaignPageExperience>
{
    public IActionResult Index(CampaignPageExperience currentPage) => View(currentPage);
}
