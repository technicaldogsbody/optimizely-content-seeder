using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Security;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Deletes what a seed run created: blocks, pages, section blueprints, the tracking records and the seeded images.
/// Can do this for every definition, or for one definition on its own.
/// </summary>
internal sealed class ContentDeleter(
    IContentRepository contentRepository,
    IContentLoader contentLoader,
    ISectionSeeder sections,
    ISeedTracker seedTracker,
    IImageContentFactory imageContentFactory,
    IEnumerable<ISeedDefinition> seedDefinitions,
    SeededFolders folders,
    ContentSeederOptions options,
    ILogger logger)
{

    /// <summary>
    /// Deletes the tracked content of one definition, or of every definition when <paramref name="definitionId"/> is null,
    /// then clears its tracking records and seeded images. Items that are already gone are skipped.
    /// </summary>
    /// <param name="definitionId">The definition to delete, or null for every definition.</param>
    /// <param name="ct">Cancels the operation.</param>
    public async Task DeleteAllAsync(string? definitionId, CancellationToken ct = default)
    {
        logger.LogInformation(definitionId is null
            ? "Deleting all seeded content."
            : "Deleting seeded content for '{DefinitionId}'.", definitionId);

        // Delete blocks/shared section instances and pages separately; blocks and shared section
        // instances first to avoid orphaned ContentArea/UnstructuredData references. Inline (structural)
        // section placements have no standalone identity and need no separate deletion — they go with
        // their parent page.
        var definitionIds = definitionId is null
            ? seedDefinitions.Select(d => d.Id).ToList()
            : [definitionId];

        var records = await GetRecordsAsync(definitionIds, ct).ConfigureAwait(false);
        var blocks = records.Where(r => r.ContentType is ContentSeederRecord.ContentTypes.Block
                                         or ContentSeederRecord.ContentTypes.SectionInstance).ToList();
        var pages = records.Where(r => r.ContentType == ContentSeederRecord.ContentTypes.Page).ToList();
        var blueprints = records.Where(r => r.ContentType == ContentSeederRecord.ContentTypes.SectionBlueprint).ToList();

        foreach (var record in blocks)
        {
            ct.ThrowIfCancellationRequested();
            DeleteContent(record.ContentId);
        }

        foreach (var record in pages.OrderByDescending(r => r.CreatedUtc))
        {
            ct.ThrowIfCancellationRequested();
            DeleteContent(record.ContentId);
        }

        foreach (string id in definitionIds)
            DeleteEmptyBlockFolder(id);

        // Blueprints are Guid-keyed metadata, not ContentReference-addressable content, so the section seeder deletes them.
        foreach (var record in blueprints)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await sections.DeleteBlueprintAsync(record.ContentGuid, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete blueprint {BlueprintId}. It may have already been deleted.", record.ContentGuid);
            }
        }

        // With no definition named, the whole tracking table goes, including records of definitions no longer registered.
        // With one named, only that definition's records and fingerprint go, so the others are left alone.
        if (definitionId is null)
            await seedTracker.TruncateAsync(ct).ConfigureAwait(false);
        else
            await seedTracker.DeleteDefinitionAsync(definitionId, ct).ConfigureAwait(false);

        // Images are not tracked as ContentSeederRecord rows (they aren't page/block content), so they are cleaned up
        // here. Each definition has its own seeded folder, so deleting one never removes another definition's images.
        await imageContentFactory.DeleteSeededImagesAsync(definitionId, ct).ConfigureAwait(false);

        logger.LogInformation("Deleted {BlockCount} blocks and {PageCount} pages{Scope}.",
            blocks.Count, pages.Count, definitionId is null ? string.Empty : $" for '{definitionId}'");
    }

    // Removes a definition's shared-block folder once its blocks are gone, but only when it is empty: anything an
    // editor has put in it since is left alone.
    private void DeleteEmptyBlockFolder(string definitionId)
    {
        string path = SeededFolders.OwnerPath(options.SeededBlocksFolder, definitionId);
        try
        {
            var folder = folders.Find(ContentReference.GlobalBlockFolder, path);
            if (folder is null || ContentReference.IsNullOrEmpty(folder))
                return;

            if (contentLoader.GetChildren<IContent>(folder).Any())
            {
                logger.LogInformation("Seeded blocks folder '{Folder}' still has content and was kept.", path);
                return;
            }

            contentRepository.Delete(folder, forceDelete: true, access: AccessLevel.NoAccess);
            folders.Clear();
            logger.LogInformation("Deleted empty seeded blocks folder '{Folder}'.", path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete seeded blocks folder '{Folder}'. It may need manual cleanup.", path);
        }
    }

    private void DeleteContent(int contentId)
    {
        try
        {
            var contentRef = new ContentReference(contentId);
            contentRepository.Delete(contentRef, forceDelete: true, access: AccessLevel.NoAccess);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete content with ID {ContentId}. It may have already been deleted.", contentId);
        }
    }

    private async Task<IReadOnlyList<ContentSeederRecord>> GetRecordsAsync(IEnumerable<string> definitionIds, CancellationToken ct)
    {
        var allRecords = new List<ContentSeederRecord>();
        foreach (var definitionId in definitionIds)
        {
            ct.ThrowIfCancellationRequested();
            var records = await seedTracker.GetByDefinitionAsync(definitionId, ct).ConfigureAwait(false);
            allRecords.AddRange(records);
        }
        return allRecords;
    }
}
