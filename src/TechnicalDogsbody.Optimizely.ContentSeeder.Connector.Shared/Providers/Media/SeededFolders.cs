using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Security;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>
/// Finds and creates the folders that seeded content is written into, one subfolder per seed definition, for both
/// media (under the global assets root) and shared blocks (under the For All Sites block folder).
/// </summary>
internal sealed class SeededFolders(IContentRepository contentRepository, IContentLoader contentLoader)
{
    private readonly object _lock = new();

    // Resolved once per operation and reused, so a run that creates dozens of items only resolves each folder once.
    // Keyed by root and path together, since the same path can sit under two roots.
    private readonly ConcurrentDictionary<string, ContentReference> _folders = new(StringComparer.Ordinal);

    /// <summary>
    /// The folder at <paramref name="path"/> under <paramref name="root"/>, created with any missing segments.
    /// Locked: several items can be created at once when AI generation runs in parallel.
    /// </summary>
    internal ContentReference GetOrCreate(ContentReference root, string path)
    {
        string key = $"{root.ID}|{path}";
        lock (_lock)
        {
            if (_folders.TryGetValue(key, out var cached) && !ContentReference.IsNullOrEmpty(cached))
                return cached;

            var parent = root;
            foreach (string segment in SplitPath(path))
            {
                var existing = contentLoader.GetChildren<ContentFolder>(parent)
                    .FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                {
                    parent = existing.ContentLink;
                    continue;
                }

                var folder = contentRepository.GetDefault<ContentFolder>(parent);
                folder.Name = segment;
                parent = contentRepository.Save(folder, SaveAction.Publish, AccessLevel.NoAccess);
            }

            _folders[key] = parent;
            return parent;
        }
    }

    /// <summary>
    /// The folder at <paramref name="path"/> under <paramref name="root"/>, or null if it does not exist. Never creates it.
    /// </summary>
    internal ContentReference? Find(ContentReference root, string path)
    {
        var parent = root;

        foreach (string segment in SplitPath(path))
        {
            var match = contentLoader.GetChildren<ContentFolder>(parent)
                .FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return null;

            parent = match.ContentLink;
        }

        return parent;
    }

    /// <summary>
    /// Forgets every cached folder, so the next lookup reads the CMS again.
    /// </summary>
    internal void Clear()
    {
        lock (_lock)
            _folders.Clear();
    }

    /// <summary>
    /// The folder path for one seed definition: the base folder, plus one subfolder for the definition, or the base folder alone when
    /// <paramref name="seedDefinitionId"/> is null.
    /// </summary>
    internal static string OwnerPath(string basePath, string? seedDefinitionId)
        => seedDefinitionId is null
            ? basePath
            : $"{basePath.TrimEnd('/')}/{OwnerSegment(seedDefinitionId)}";

    // A definition id as a folder name: anything that isn't a letter, digit, dash or underscore becomes a dash.
    private static string OwnerSegment(string seedDefinitionId)
        => Regex.Replace(seedDefinitionId, "[^A-Za-z0-9_-]", "-");

    private static string[] SplitPath(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
