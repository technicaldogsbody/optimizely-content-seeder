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
using EPiServer.VisualBuilder;
using EPiServer.VisualBuilder.Compositions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Adds rows, columns and components to a Visual Builder composition, and applies display styles to them.
/// </summary>
internal static class CompositionNodes
{
    // AddRow/AddColumn/AddComponent have name-less overloads; use them rather than passing null.
    /// <summary>
    /// Adds a row to a section. A row without a name uses the name-less overload, so no null is passed.
    /// </summary>
    internal static StructureNode AddRowNode(SectionNode section, string? name)
        => name is null ? section.AddRow() : section.AddRow(name);

    /// <summary>
    /// Adds a column to a row. A column without a name uses the name-less overload.
    /// </summary>
    internal static StructureNode AddColumnNode(StructureNode row, string? name)
        => name is null ? row.AddColumn() : row.AddColumn(name);

    /// <summary>
    /// Adds an element to a column as a component. A component without a name uses the name-less overload.
    /// </summary>
    internal static ComponentNode AddComponentNode(StructureNode column, BlockData element, string? name)
        => name is null ? column.AddComponent(element) : column.AddComponent(element, name);

    /// <summary>
    /// Applies a display template and display settings to a node. Does nothing when neither is set.
    /// </summary>
    internal static void ApplyStyles(
        CompositionNode node,
        string? displayTemplateKey,
        IReadOnlyDictionary<string, string>? displaySettings)
    {
        if (displayTemplateKey is null && displaySettings is null)
            return;

        node.WithStyles(displayTemplateKey!, displaySettings?.ToDictionary(k => k.Key, v => v.Value)!);
    }
}
