using System.Collections.Concurrent;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Framework.Blobs;
using EPiServer.Framework.DataAnnotations;
using EPiServer.Security;
using EPiServer.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>
/// Saves media assets (images, videos) into the seeded media folder and deletes it again. Each seed definition has its
/// own subfolder, <c>SeededImagesFolder/{definitionId}</c>, so one definition's assets are never removed by another's
/// reseed. Shared by the image factory and the video provider. Scoped: resolved fresh per seed operation.
/// </summary>
public sealed class SeededMediaWriter(
    IContentRepository contentRepository,
    IContentLoader contentLoader,
    IBlobFactory blobFactory,
    IContentTypeRepository contentTypeRepository,
    IOptions<ContentSeederOptions> options,
    ISeedDefinitionAccessor definitionAccessor,
    ILogger<SeededMediaWriter> logger)
{
    private readonly ConcurrentDictionary<(Type Base, string? Extension), Type?> _mediaTypes = new();
    private readonly SeededFolders _folders = new(contentRepository, contentLoader);

    /// <summary>
    /// The registered content type to save media of kind <paramref name="baseType"/> (e.g. <see cref="ImageData"/>
    /// or <see cref="VideoData"/>) as, or null if the site has none. When several exist, one whose
    /// <c>[MediaDescriptor]</c> lists <paramref name="preferredExtension"/> is preferred.
    /// </summary>
    public Type? FindMediaType(Type baseType, string? preferredExtension = null)
        => _mediaTypes.GetOrAdd((baseType, preferredExtension), key =>
        {
            var candidates = contentTypeRepository.List()
                .Select(ct => ct.ModelType)
                .Where(t => t is not null && key.Base.IsAssignableFrom(t) && t != key.Base)
                .Select(t => t!)
                .ToList();

            if (key.Extension is { } extension)
            {
                var preferred = candidates.FirstOrDefault(t => ListsExtension(t, extension));
                if (preferred is not null)
                    return preferred;
            }

            return candidates.FirstOrDefault();
        });

    /// <summary>
    /// Saves <paramref name="bytes"/> as a published asset of <paramref name="mediaType"/> in the seeded media folder
    /// of the definition being seeded. Throws if it cannot be saved; callers decide whether that is fatal.
    /// </summary>
    public async Task<ContentReference> SaveAsync(
        Type mediaType, string name, byte[] bytes, string extension, CancellationToken ct = default)
    {
        var parent = GetOrCreateSeededFolder(CurrentOwner);

        var getDefault = typeof(IContentRepository)
            .GetMethods()
            .First(m => m.Name == nameof(IContentRepository.GetDefault)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters() is { Length: 1 })
            .MakeGenericMethod(mediaType);

        var media = (MediaData)getDefault.Invoke(contentRepository, [parent])!;
        var content = media as IContent
                      ?? throw new InvalidOperationException(
                          $"ContentSeeder: GetDefault returned a type that does not implement IContent for '{mediaType.FullName}'.");

        content.Name = name;

        var blob = blobFactory.CreateBlob(media.BinaryDataContainer, extension);
        await using (var stream = blob.OpenWrite())
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        media.BinaryData = blob;

        var contentType = contentTypeRepository.Load(mediaType);
        if (contentType is not null)
            content.ContentTypeID = contentType.ID;

        var savedRef = contentRepository.Save(content, SaveAction.Publish, AccessLevel.NoAccess);
        logger.LogDebug("ContentSeeder: Created media asset '{Name}' (ContentId={ContentId}).", name, savedRef.ID);
        return savedRef;
    }

    /// <summary>
    /// Finds an asset of this exact name in the seeded media folder of the definition being seeded, or null. Never creates the folder.
    /// </summary>
    public ContentReference? FindByName(string name)
    {
        var folder = FindSeededFolder(CurrentOwner);
        if (folder is null)
            return null;

        return contentLoader.GetChildren<MediaData>(folder)
            .OfType<IContent>()
            .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.ContentLink;
    }

    /// <summary>
    /// Deletes the seeded media folder of one definition, or the whole seeded folder when <paramref name="seedDefinitionId"/>
    /// is null, with every asset under it. Never throws: failures are logged.
    /// </summary>
    /// <param name="seedDefinitionId">The definition whose images to delete, or null for every definition's images.</param>
    public void DeleteFolder(string? seedDefinitionId)
    {
        string path = FolderPath(seedDefinitionId);
        try
        {
            var folderRef = FindSeededFolder(seedDefinitionId);
            if (folderRef is not null && !ContentReference.IsNullOrEmpty(folderRef))
            {
                contentRepository.Delete(folderRef, forceDelete: true, access: AccessLevel.NoAccess);
                logger.LogInformation("ContentSeeder: Deleted seeded images folder '{Folder}'.", path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ContentSeeder: Failed to delete seeded images folder '{Folder}'. It may need manual cleanup.", path);
        }
        finally
        {
            _folders.Clear();
        }
    }

    // The definition whose images are being written, or null (the base folder) outside a definition.
    private string? CurrentOwner => definitionAccessor.Current?.Id;

    private string FolderPath(string? seedDefinitionId)
        => SeededFolders.OwnerPath(options.Value.SeededImagesFolder, seedDefinitionId);

    private static ContentReference MediaRoot
        => SystemDefinition.Current?.GlobalAssetsRoot ?? ContentReference.GlobalBlockFolder;

    private ContentReference GetOrCreateSeededFolder(string? seedDefinitionId)
        => _folders.GetOrCreate(MediaRoot, FolderPath(seedDefinitionId));

    private ContentReference? FindSeededFolder(string? seedDefinitionId)
        => _folders.Find(MediaRoot, FolderPath(seedDefinitionId));

    private static bool ListsExtension(Type type, string extension)
        => type.GetCustomAttributes(typeof(MediaDescriptorAttribute), inherit: true)
            .OfType<MediaDescriptorAttribute>()
            .Any(a => (a.ExtensionString ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(extension.TrimStart('.'), StringComparer.OrdinalIgnoreCase));
}
