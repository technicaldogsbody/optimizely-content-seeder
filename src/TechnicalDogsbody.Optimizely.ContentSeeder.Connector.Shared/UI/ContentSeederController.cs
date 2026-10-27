using EPiServer.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.MicroMediator.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Application;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.UI;

/// <summary>
/// Provides REST endpoints consumed by the ContentSeeder admin gadget.
/// All endpoints require the CmsAdmin authorization policy. The seed, reseed, dry run and delete endpoints take an
/// optional <c>definitionId</c> query parameter; leave it out to act on every definition.
/// </summary>
[ApiController]
[Route("api/contentseeder")]
[Authorize(Policy = CmsPolicyNames.CmsAdmin)]
public sealed class ContentSeederController(
    ISeedTracker seedTracker,
    IMediator mediator,
    IContentGenerationProvider textProvider,
    IImageContentFactory imageFactory,
    IServiceScopeFactory serviceScopeFactory,
    SeedOperationMonitor operationMonitor,
    ILogger<ContentSeederController> logger) : ControllerBase
{
    // ── GET /api/contentseeder/status ────────────────────────────────────────

    /// <summary>GET api/contentseeder/status: the tracked content, each definition's status, the running operation and the providers in use.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<ContentSeederStatus>> GetStatus(CancellationToken ct)
    {
        var status = await seedTracker.GetStatusAsync(ct).ConfigureAwait(false);
        var ids = await mediator.SendAsync(new GetDefinitionIdsQuery(), ct).ConfigureAwait(false);
        var changed = await mediator.SendAsync(new GetChangedDefinitionsQuery(), ct).ConfigureAwait(false);
        var seeded = (await seedTracker.GetDefinitionStatusesAsync(ct).ConfigureAwait(false))
            .ToDictionary(s => s.SeedDefinitionId, StringComparer.Ordinal);

        // Every registered definition appears, so one that has never been seeded can still be chosen and seeded.
        var definitions = ids
            .Select(id => (seeded.TryGetValue(id, out var s) ? s : new SeedDefinitionStatus(id, 0, 0, 0, null, false))
                with { IsChanged = changed.Contains(id) })
            .ToList();

        // Whether the CMS has sections is a property of the connector, so the UI asks the registered section seeder.
        bool supportsSections;
        using (var scope = serviceScopeFactory.CreateScope())
            supportsSections = scope.ServiceProvider.GetRequiredService<ISectionSeeder>().SupportsSections;

        return Ok(status with
        {
            SupportsSections = supportsSections,
            Operation = operationMonitor.Current,
            ChangedDefinitions = [.. changed],
            Providers = new SeederProviders(Describe(textProvider), Describe(imageFactory)),
            Definitions = definitions,
        });
    }

    // ── POST /api/contentseeder/dryrun ───────────────────────────────────────

    /// <summary>POST api/contentseeder/dryrun: what a seed would do, for all definitions or one, without writing content. Returns 400 for an unknown definition.</summary>
    [HttpPost("dryrun")]
    public async Task<ActionResult<DryRunResult>> DryRun([FromQuery] string? definitionId, CancellationToken ct)
    {
        if (await UnknownDefinitionAsync(definitionId, ct) is { } error)
            return new ActionResult<DryRunResult>(error);

        var result = await mediator.SendAsync(new DryRunQuery(definitionId), ct).ConfigureAwait(false);
        return Ok(result);
    }

    // ── POST /api/contentseeder/seed ─────────────────────────────────────────

    /// <summary>POST api/contentseeder/seed: starts a seed in the background for all definitions or one. Returns 202 when it starts, 400 for an unknown definition, or 409 if an operation is already running.</summary>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed([FromQuery] string? definitionId, CancellationToken ct)
    {
        if (await UnknownDefinitionAsync(definitionId, ct) is { } error)
            return error;

        return LaunchBackgroundOperation(Name("Seed", definitionId),
            scope => scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new SeedCommand(definitionId)).AsTask());
    }

    // ── POST /api/contentseeder/reseed ───────────────────────────────────────

    /// <summary>POST api/contentseeder/reseed: deletes the seeded content for all definitions or one, then seeds it again, in the background.</summary>
    [HttpPost("reseed")]
    public async Task<IActionResult> Reseed([FromQuery] string? definitionId, CancellationToken ct)
    {
        if (await UnknownDefinitionAsync(definitionId, ct) is { } error)
            return error;

        return LaunchBackgroundOperation(Name("Reseed", definitionId),
            scope => scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new ReseedCommand(definitionId)).AsTask());
    }

    // ── POST /api/contentseeder/delete ───────────────────────────────────────

    /// <summary>
    /// POST api/contentseeder/delete: deletes the content of all definitions or one, in the background. An unknown id is
    /// allowed, so content left behind by a definition that has been removed from the code can still be deleted.
    /// </summary>
    [HttpPost("delete")]
    public IActionResult Delete([FromQuery] string? definitionId)
        => LaunchBackgroundOperation(Name("Delete", definitionId),
            scope => scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new DeleteAllCommand(definitionId)).AsTask());

    // ── private ──────────────────────────────────────────────────────────────

    // Providers say what they are by implementing IDescribesProvider; anything else shows by type name.
    private static ProviderInfo Describe(object provider)
        => (provider as IDescribesProvider)?.Describe() ?? new ProviderInfo(provider.GetType().Name);

    // "Seed" for all definitions, "Seed live-demo-v1" for one. Shown in the admin gadget while it runs.
    private static string Name(string action, string? definitionId)
        => definitionId is null ? action : $"{action} {definitionId}";

    // A 400 response when a definition id is named but isn't registered; null when the id is fine or absent.
    private async Task<ActionResult?> UnknownDefinitionAsync(string? definitionId, CancellationToken ct)
    {
        if (definitionId is null)
            return null;

        var ids = await mediator.SendAsync(new GetDefinitionIdsQuery(), ct).ConfigureAwait(false);
        return ids.Contains(definitionId, StringComparer.Ordinal)
            ? null
            : BadRequest($"No seed definition has the id '{definitionId}'.");
    }

    private IActionResult LaunchBackgroundOperation(string name, Func<IServiceScope, Task> operation)
    {
        // The completion email goes to whoever started the run: the signed-in user's address, if the CMS has one for them.
        // The sign-in name is the email address for the CMS admin accounts, so use it when no email claim is issued.
        string? signInName = User.Identity?.Name;
        string? requester = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("email")?.Value
            ?? (signInName is not null && signInName.Contains('@') ? signInName : null);
        if (!operationMonitor.TryBegin(name, requester, signInName))
            return Conflict("An operation is already in progress.");

        _ = Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                await operation(scope).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
                logger.LogError(ex, "ContentSeeder: Background {Operation} operation failed.", name);
            }
            finally
            {
                operationMonitor.Complete(failure);
            }
        });

        return Accepted();
    }
}
