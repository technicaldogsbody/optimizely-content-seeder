using EPiServer.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.UI;

/// <summary>
/// Serves the ContentSeeder admin UI within the Optimizely CMS shell.
/// </summary>
[Authorize(Policy = CmsPolicyNames.CmsAdmin)]
[Route("contentseeder")]
public sealed class ContentSeederPageController : Controller
{
    /// <summary>The documentation, linked from the admin page and from the setup message.</summary>
    public const string DocumentationUrl = "https://github.com/technicaldogsbody/optimizely-content-seeder";

    private const string SetupMessage =
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Content Seeder</title></head>" +
        "<body style=\"font-family:sans-serif;max-width:40rem;margin:2rem auto;padding:0 1rem\">" +
        "<h1>Content Seeder is not set up</h1>" +
        "<p>The package is installed, but <code>services.AddContentSeeder()</code> is not called in " +
        "<code>Startup.cs</code> (or <code>Program.cs</code>). Add it to the services setup and restart the site.</p>" +
        "<p><a href=\"" + DocumentationUrl + "\">Documentation</a></p>" +
        "</body></html>";

    /// <summary>
    /// Serves the admin page that hosts the gadget. If AddContentSeeder() was not called, explains the fix instead of
    /// failing with a missing view.
    /// </summary>
    [HttpGet("")]
    [HttpGet("index")]
    public IActionResult Index()
    {
        // AddContentSeeder() registers IContentCapabilities, so its absence means the seeder isn't set up.
        if (HttpContext.RequestServices.GetService<IContentCapabilities>() is null)
            return Content(SetupMessage, "text/html; charset=utf-8");

        return View("~/Views/ContentSeeder/Index.cshtml");
    }
}
