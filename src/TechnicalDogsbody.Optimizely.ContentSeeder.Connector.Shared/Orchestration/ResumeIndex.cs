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

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

// The complete master-language pages from earlier runs, in the order they were created, grouped by where they sit
// (parent) and what they are (content type). The tree walk takes from the group that matches the node it is on.
/// <summary>
/// The complete master-language pages from earlier runs, grouped by parent and content type, so the page walk can reuse them in the order they were created.
/// </summary>
internal sealed class ResumeIndex
{
    private readonly Dictionary<(int ParentId, int ContentTypeId), Queue<ContentSeederRecord>> _pages = new();

    /// <summary>
    /// Adds a page to the group for its parent and content type.
    /// </summary>
    internal void Add(int parentId, int contentTypeId, ContentSeederRecord record)
    {
        if (!_pages.TryGetValue((parentId, contentTypeId), out var queue))
            _pages[(parentId, contentTypeId)] = queue = new Queue<ContentSeederRecord>();

        queue.Enqueue(record);
    }

    /// <summary>
    /// Takes the next page for a parent and content type. Returns false when none is left.
    /// </summary>
    internal bool TryTake(int parentId, int contentTypeId, out ContentSeederRecord record)
    {
        record = null!;
        return _pages.TryGetValue((parentId, contentTypeId), out var queue) && queue.TryDequeue(out record!);
    }
}
