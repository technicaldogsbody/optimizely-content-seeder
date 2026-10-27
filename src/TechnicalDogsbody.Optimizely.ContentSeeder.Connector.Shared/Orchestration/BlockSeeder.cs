using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.DataAnnotations;
using EPiServer.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentProperties;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentRepositoryMethods;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Seeds shared blocks: the master-language block, then its language branches. Shared section items use the
/// same path, since a shared section is content like any block.
/// </summary>
internal sealed class BlockSeeder(
    IContentRepository contentRepository,
    IContentTypeRepository contentTypeRepository,
    IContentGenerationProvider contentGenerationProvider,
    ISeedTracker seedTracker,
    SaveActions saves,
    VideoApplier videos,
    LanguageTranslator translator,
    SeededFolders folders,
    ContentSeederOptions options,
    ILogger logger)
{
    /// <summary>
    /// Creates one language version of a shared block, translated from the master or generated for the language, then tracks it as complete.
    /// </summary>
    internal async Task CreateBlockLanguageBranchAsync(
        BlockDefinition blockDef,
        ContentReference masterRef,
        string seedDefinitionId,
        string masterLanguage,
        string language,
        Guid seedRunId,
        CancellationToken ct,
        string trackingContentType = ContentSeederRecord.ContentTypes.Block)
    {
        ct.ThrowIfCancellationRequested();
        using var _ = SeedInstanceScope.Enter(blockDef.Instance);

        var contentType = contentTypeRepository.Load(blockDef.Type)
            ?? throw new InvalidOperationException(
                $"ContentSeeder: No content type registered for block type '{blockDef.Type.FullName}'.");

        var createBranch = _createLanguageBranch.MakeGenericMethod(blockDef.Type);

        var branch = (BlockData)createBranch.Invoke(contentRepository, [masterRef, Cultures.Resolve(language, logger)])!;
        var branchContent = branch as IContent
            ?? throw new InvalidOperationException($"Language branch for block '{blockDef.Id}' does not implement IContent.");

        // Use the concrete type (not the Castle proxy) to discover only user-defined properties.
        // The proxy re-exposes all EPiServer interface members (Language, Status, etc.) as public
        // properties, which causes Bogus to overwrite EPiServer's internal fields.
        // For language branches only populate [CultureSpecific] properties. Block properties without it
        // (images, numbers, links…) are "common": EPiServer shares them from the master language version and
        // refuses to save them on a branch ("Cannot save non-culture specific property").
        var props = GetContentProperties(blockDef.Type)
            .Where(p => p.GetCustomAttribute<CultureSpecificAttribute>() is not null)
            .ToList();
        await translator.PopulateBranchAsync(branch, masterRef, masterLanguage, props, language, blockDef.Type, blockDef.ImageSizes, blockDef.Videos, ct).ConfigureAwait(false);
        LanguageTranslator.ConfigureLanguageVersion(branch, blockDef.Configure, 0, blockDef.Type);

        // EPiServer validates that Name is non-empty before saving; CreateLanguageBranch does
        // not copy the master name into the branch, so set it explicitly if missing.
        if (string.IsNullOrWhiteSpace(branchContent.Name))
            branchContent.Name = $"{blockDef.Type.Name}-{blockDef.Id}";

        // Restore ContentTypeID — providers must never overwrite it.
        branchContent.ContentTypeID = contentType.ID;

        var trackRecord = new ContentSeederRecord
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = seedDefinitionId,
            BlockDefinitionId = blockDef.Id,
            ContentGuid = branchContent.ContentGuid,
            ContentId = masterRef.ID,
            ContentType = trackingContentType,
            Language = language,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            SeedRunId = seedRunId,
            IsPartial = true
        };
        await seedTracker.InsertAsync(trackRecord, ct).ConfigureAwait(false);

        var branchRef = contentRepository.Save(
            branchContent, saves.ActionFor(branchContent, GetContentProperties(blockDef.Type), $"Block {blockDef.Id} ({language})"), AccessLevel.NoAccess);
        await seedTracker.MarkCompleteAsync(trackRecord.Id, branchRef.ID, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the master-language version of a shared block, populated by the content provider, saves it, and tracks it.
    /// </summary>
    internal async Task<ContentReference> CreateBlockAsync(
        BlockDefinition blockDef,
        string seedDefinitionId,
        string language,
        Guid seedRunId,
        CancellationToken ct,
        string trackingContentType = ContentSeederRecord.ContentTypes.Block)
    {
        ct.ThrowIfCancellationRequested();
        using var _ = SeedInstanceScope.Enter(blockDef.Instance);

        var contentType = contentTypeRepository.Load(blockDef.Type)
            ?? throw new InvalidOperationException(
                $"ContentSeeder: No content type registered for block type '{blockDef.Type.FullName}'. " +
                "Ensure the type has a [ContentType] attribute and the application has initialised.");

        logger.LogDebug("Creating block {BlockId} with ContentTypeID={ContentTypeId} ({TypeName}).",
            blockDef.Id, contentType.ID, blockDef.Type.Name);

        // Shared blocks (and shared sections) go in this seed's own folder, Seeded/{seed id}, under For All Sites.
        var blockFolder = folders.GetOrCreate(
            ContentReference.GlobalBlockFolder, SeededFolders.OwnerPath(options.SeededBlocksFolder, seedDefinitionId));

        // Use GetDefault<TConcreteType>(ContentReference) — the 1-param overload looks up
        // the content type by CLR type internally and sets ContentTypeID correctly.
        var block = (BlockData)_getDefaultOneParam
            .MakeGenericMethod(blockDef.Type)
            .Invoke(contentRepository, [blockFolder])!;

        logger.LogDebug("Block instance ContentTypeID after GetDefault: {ContentTypeId}.",
            (block as IContent)?.ContentTypeID);

        // Use the concrete type (not the Castle proxy) — see CreateBlockLanguageBranchAsync for rationale.
        var props = GetContentProperties(blockDef.Type).ToList();
        logger.LogDebug("Block {BlockId} GetType={BlockType}, props to populate: [{Props}].",
            blockDef.Id, block.GetType().FullName, string.Join(", ", props.Select(p => p.Name)));
        await contentGenerationProvider.PopulateAsync(block, props, language, blockDef.Type, blockDef.ImageSizes, ct).ConfigureAwait(false);
        await videos.ApplyVideosAsync(block, blockDef.Videos, ct).ConfigureAwait(false);
        blockDef.Configure?.Invoke(block, 0);

        var blockContent = block as IContent
            ?? throw new InvalidOperationException($"Block created for '{blockDef.Id}' does not implement IContent.");

        // Defensive: always restore ContentTypeID — providers must never overwrite it.
        blockContent.ContentTypeID = contentType.ID;

        if (string.IsNullOrEmpty(blockContent.Name))
            blockContent.Name = $"{blockDef.Type.Name}-{blockDef.Id}";

        var trackRecord = new ContentSeederRecord
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = seedDefinitionId,
            BlockDefinitionId = blockDef.Id,
            ContentGuid = blockContent.ContentGuid,
            ContentId = 0, // set after save
            ContentType = trackingContentType,
            Language = language,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            SeedRunId = seedRunId,
            IsPartial = true
        };
        await seedTracker.InsertAsync(trackRecord, ct).ConfigureAwait(false);

        logger.LogDebug(
            "About to Save block {BlockId}: ContentTypeID={ContentTypeId}, ParentLink={ParentLink}, BlockFolder={BlockFolder}, ContentTypeName={ContentTypeName}.",
            blockDef.Id,
            blockContent.ContentTypeID,
            blockContent.ParentLink,
            blockFolder,
            contentType.Name);

        var savedRef = contentRepository.Save(
            blockContent, saves.ActionFor(blockContent, GetContentProperties(blockDef.Type), $"Block {blockDef.Id}"), AccessLevel.NoAccess);

        trackRecord.ContentId = savedRef.ID;
        await seedTracker.MarkCompleteAsync(trackRecord.Id, savedRef.ID, ct).ConfigureAwait(false);

        return savedRef;
    }
}
