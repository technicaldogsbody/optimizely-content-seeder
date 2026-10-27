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

/// <summary>
/// Decides which writable properties of a content type the seeder may populate.
/// </summary>
internal static class ContentProperties
{
    // Properties on any of these EPiServer framework types are never passed to the content
    // generation provider. User-defined properties live only on concrete content types
    // (e.g. ArticlePage, HeroBlock), NOT on these base types, so nothing user-defined
    // is lost. PageData is included because it adds URL-segment and other internal
    // properties (URLSegment, PageLink, ArchiveLink, WorkPageID, etc.) that Bogus would
    // otherwise try to set with random values, triggering EPiServer validation errors.
    private static readonly IReadOnlySet<string> _skipPropertyNames =
        typeof(IContent).GetProperties()
            .Concat(typeof(ContentData).GetProperties())
            .Concat(typeof(PageData).GetProperties())
            .Concat(typeof(BlockData).GetProperties())
            .Select(p => p.Name)
            // Layout and UnstructuredData (Visual Builder) are written by the connector, never by a
            // content provider. Their interface is internal to EPiServer, so they are named here.
            .Concat(["Layout", "UnstructuredData"])
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The writable properties of a content type that the seeder may populate. Framework members are excluded.
    /// </summary>
    internal static IEnumerable<PropertyInfo> GetContentProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
               .Where(p => p.CanWrite && !_skipPropertyNames.Contains(p.Name));
}
