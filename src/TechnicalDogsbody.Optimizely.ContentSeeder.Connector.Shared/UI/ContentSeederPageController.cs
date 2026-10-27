using EPiServer.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.UI;

/// <summary>
/// Serves the ContentSeeder admin UI within the Optimizely CMS shell.
/// </summary>
[Authorize(Policy = CmsPolicyNames.CmsAdmin)]
[Route("contentseeder")]
public sealed class ContentSeederPageController : Controller
{
    /// <summary>
    /// Serves the admin page that hosts the gadget.
    /// </summary>
    [HttpGet("")]
    [HttpGet("index")]
    public IActionResult Index() => View("~/Views/ContentSeeder/Index.cshtml");
}
