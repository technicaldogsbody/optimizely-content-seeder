using EPiServer.Core;
using EPiServer.Framework.DataAnnotations;
using EPiServer.Framework.Web;
using EPiServer.Web;
using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.Controllers;

/// <summary>
/// Renders blocks (and Visual Builder sections and elements) for the editor's preview, so a seeded block
/// can be opened from the Blocks panel and seen on its own. Blocks are not pages and have no URL, so the
/// CMS asks for a template tagged "Preview" / "Edit" — this is it.
/// </summary>
[TemplateDescriptor(
    Inherited = true,
    TemplateTypeCategory = TemplateTypeCategories.MvcController, // block controllers are MvcPartialController by default
    Tags = [RenderingTags.Preview, RenderingTags.Edit],
    AvailableWithoutTag = false)]
[VisitorGroupImpersonation]
public class PreviewController : ActionControllerBase, IRenderTemplate<BlockData>
{
    public IActionResult Index(IContent currentContent) => View(currentContent);
}
