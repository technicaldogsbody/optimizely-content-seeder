using EPiServer;
using EPiServer.Core;
using EPiServer.DataAccess;
using EPiServer.Security;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>A page the seeder did not create, sitting under a seeded page. Deleting the seeded page deletes it too.</summary>
/// <param name="ContentId">The page's content ID.</param>
/// <param name="Name">The page's name, for the warning.</param>
/// <param name="SeededParentId">The seeded page it sits under.</param>
public sealed record EditorPage(int ContentId, string Name, int SeededParentId);

/// <summary>A page that editor content can be moved under. It is never a seeded page or anything under one.</summary>
/// <param name="ContentId">The page's content ID.</param>
/// <param name="Name">The page's name, for the dropdown.</param>
public sealed record MoveTarget(int ContentId, string Name);

/// <summary>What a reseed or delete would remove that the seeder did not create, and where it could be moved.</summary>
/// <param name="Pages">Editor pages under seeded pages. Empty when the delete is safe.</param>
/// <param name="Targets">Pages the editor pages can be moved under.</param>
public sealed record EditorContentReport(IReadOnlyList<EditorPage> Pages, IReadOnlyList<MoveTarget> Targets);

/// <summary>
/// Finds editor-created pages under seeded pages, which a delete would remove with them, and moves them out of the way
/// when asked. A delete runs with forceDelete, which takes a page's whole subtree.
/// </summary>
public sealed class EditorContentGuard(
    IContentRepository contentRepository,
    ISeedTracker seedTracker,
    IEnumerable<ISeedDefinition> seedDefinitions)
{
    // A dropdown longer than this is no use to an editor, so the walk stops here.
    private const int MaxTargets = 200;

    /// <summary>Reports the editor pages under seeded pages, and the pages they could move under, for one definition or all.</summary>
    public async Task<EditorContentReport> ReportAsync(string? definitionId, CancellationToken ct)
    {
        var definitionIds = definitionId is null
            ? seedDefinitions.Select(d => d.Id).ToList()
            : [definitionId];

        // The seeded pages in scope. Editor pages are only looked for under these.
        var seededPageIds = await SeededPageIdsAsync(definitionIds, ct).ConfigureAwait(false);
        // Every seeded page, whichever definition made it, is never offered as a move target.
        var allSeededPageIds = definitionId is null
            ? seededPageIds
            : await SeededPageIdsAsync(seedDefinitions.Select(d => d.Id).ToList(), ct).ConfigureAwait(false);

        var pages = new List<EditorPage>();
        foreach (var seededId in seededPageIds)
            CollectEditorPages(new ContentReference(seededId), seededId, seededPageIds, pages);

        // A seeded page nested under another is walked from both, so the same editor page can be found twice.
        var uniquePages = pages.DistinctBy(p => p.ContentId).ToList();

        // Walk from the root, so each site's start page is offered too. Seeded pages, and anything under them, are skipped.
        var targets = new List<MoveTarget>();
        CollectTargets(ContentReference.RootPage, allSeededPageIds, targets);

        return new EditorContentReport(uniquePages, targets);
    }

    /// <summary>Moves each editor page, with its subtree, under the target page. Stops at the first failure, so the delete never runs half-moved.</summary>
    public void Move(IEnumerable<int> pageIds, int targetId)
    {
        foreach (var pageId in pageIds)
        {
            contentRepository.Move(
                new ContentReference(pageId),
                new ContentReference(targetId),
                AccessLevel.NoAccess,
                AccessLevel.NoAccess);
        }
    }

    // The IDs of the pages a definition's records track, as the seeder created them.
    private async Task<HashSet<int>> SeededPageIdsAsync(IEnumerable<string> definitionIds, CancellationToken ct)
    {
        var ids = new HashSet<int>();
        foreach (var id in definitionIds)
        {
            foreach (var record in await seedTracker.GetByDefinitionAsync(id, ct).ConfigureAwait(false))
            {
                if (record.ContentType == ContentSeederRecord.ContentTypes.Page)
                    ids.Add(record.ContentId);
            }
        }

        return ids;
    }

    // Walks a seeded page's children. A seeded child is walked on in turn. Any other child is an editor page, and its own subtree goes with it.
    private void CollectEditorPages(ContentReference parent, int seededParentId, ISet<int> seededPageIds, List<EditorPage> pages)
    {
        foreach (var child in contentRepository.GetChildren<PageData>(parent))
        {
            var id = child.ContentLink.ID;
            if (seededPageIds.Contains(id))
            {
                CollectEditorPages(child.ContentLink, id, seededPageIds, pages);
                continue;
            }

            pages.Add(new EditorPage(id, child.Name, seededParentId));
        }
    }

    // Walks from the start page down through pages that aren't seeded. A seeded page is never offered, and nor is anything under one.
    private void CollectTargets(ContentReference start, ISet<int> seededPageIds, List<MoveTarget> targets)
    {
        var pending = new Stack<ContentReference>();
        pending.Push(start);

        while (pending.Count > 0 && targets.Count < MaxTargets)
        {
            var current = pending.Pop();
            foreach (var child in contentRepository.GetChildren<PageData>(current))
            {
                // The Recycle Bin sits under the root, but nothing should be moved into it.
                if (child.ContentLink.ID == ContentReference.WasteBasket.ID || seededPageIds.Contains(child.ContentLink.ID))
                    continue;

                targets.Add(new MoveTarget(child.ContentLink.ID, child.Name));
                pending.Push(child.ContentLink);
                if (targets.Count >= MaxTargets)
                    return;
            }
        }
    }
}
