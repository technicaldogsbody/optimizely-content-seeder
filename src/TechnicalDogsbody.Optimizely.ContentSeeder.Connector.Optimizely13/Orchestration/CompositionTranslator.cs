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
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.CompositionNodes;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentProperties;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Copies a master's Visual Builder layout into a language branch, translating the inline elements it holds.
/// Shared sections and blocks are referenced, not copied.
/// </summary>
internal sealed class CompositionTranslator(
    IContentLoader contentLoader,
    IContentGenerationProvider contentGenerationProvider,
    ICompositionMapper compositionMapper,
    GenerationRunner runner,
    ILogger logger)
{
    // ── private: Visual Builder compositions in other languages ──────────────

    // Rebuilds the master's composition for a language branch: the same sections, rows, columns and
    // components, with the master's inline elements cloned and translated. Shared sections and blocks are
    // referenced, not copied; they have language versions of their own.
    /// <summary>
    /// Copies the master's Visual Builder layout into a language branch. Inline elements are cloned and translated; shared sections and blocks are referenced.
    /// </summary>
    internal async Task CopyCompositionToBranchAsync(
        ExperienceData branch,
        ContentReference masterRef,
        string masterLanguage,
        string language,
        CancellationToken ct,
        IReadOnlyList<SectionInstancePlan?>? slotPlans = null)
    {
        if (!contentLoader.TryGet<ExperienceData>(
                masterRef.ToReferenceWithoutVersion(), Cultures.Resolve(masterLanguage, logger), out var master))
        {
            logger.LogWarning("Could not load the master version of {Page} to copy its layout to '{Language}'.", masterRef, language);
            return;
        }

        var source = compositionMapper.ToComposition(master, filterForPublished: false);
        if (source is null)
            return;

        var target = new Composition { ExperienceData = branch };
        // Cloning is quick; translating each inline element is not, so those are collected and run together.
        var translations = new List<TranslationJob>();
        var cursor = new InstanceCursor(slotPlans ?? Array.Empty<SectionInstancePlan?>(), logger);
        await CloneNodesAsync(source.Nodes, target, masterLanguage, language, translations, cursor, ct).ConfigureAwait(false);
        await TranslateAllAsync(translations, masterLanguage, language, ct).ConfigureAwait(false);
        compositionMapper.Populate(branch, target);
    }

    // An inline element to translate: the master's, and the clone that receives the translation.
    // Instance is the named rule set of the element or section, carried from the definition by position (see InstanceCursor).
    private sealed record TranslationJob(object Source, object Target, string? Instance);

    // Walks the master composition in the order it was built, handing out the named rule set of each root slot and of each
    // inline element. Positions line up because the composition keeps the order the seeder created the items in. If the
    // composition ever has more slots or elements than were planned, the extra ones get no named set, and one warning is logged.
    private sealed class InstanceCursor(IReadOnlyList<SectionInstancePlan?> slots, ILogger logger)
    {
        private int _slot;
        private SectionInstancePlan? _section;
        private int _element;
        private bool _warned;

        // The named set of the section being cloned, read when its translation job is added.
        internal string? SectionInstance => _section?.SectionInstance;

        // The next root slot. Shared slots have no plan, so they return null.
        internal SectionInstancePlan? NextSlot()
        {
            if (_slot < slots.Count)
                return slots[_slot++];

            Mismatch();
            return null;
        }

        internal void StartSection(SectionInstancePlan? section)
        {
            _section = section;
            _element = 0;
        }

        internal string? NextElement()
        {
            var elements = _section?.ElementInstances;
            if (elements is not null && _element < elements.Count)
                return elements[_element++];

            Mismatch();
            return null;
        }

        private void Mismatch()
        {
            if (_warned)
                return;

            _warned = true;
            logger.LogWarning(
                "A layout's sections or elements do not match the seed definition they were planned from. Those without a planned named rule set use the default rules.");
        }
    }

    // Translates the cloned layout elements. Elements whose source content is identical (a repeating
    // element that reuses its content, or simply the same text twice) are translated once and the result
    // copied to the rest, so they cost one request rather than one each.
    private async Task TranslateAllAsync(
        IReadOnlyList<TranslationJob> jobs, string masterLanguage, string language, CancellationToken ct)
    {
        // The named set is part of the key: two identical elements with different sets are not the same item to translate.
        var groups = jobs.GroupBy(j => j.Instance + "\n" + TranslationKey(j.Source)).Select(g => g.ToList()).ToList();

        logger.LogInformation(
            "Translating {Count} layout elements ({Distinct} distinct) to '{Language}', up to {Parallel} at a time…",
            jobs.Count, groups.Count, language, runner.Parallelism);
        var timer = System.Diagnostics.Stopwatch.StartNew();

        await runner.RunAsync(
            groups.Select<List<TranslationJob>, Func<CancellationToken, Task>>(
                group => token => TranslateInlineAsync(group[0].Source, group[0].Target, group[0].Instance, masterLanguage, language, token)),
            ct).ConfigureAwait(false);

        foreach (var group in groups)
        {
            foreach (var duplicate in group.Skip(1))
                CopyContent(group[0].Target, duplicate.Target);
        }

        logger.LogInformation("Translated {Count} layout elements to '{Language}' in {Seconds:0.0}s.", jobs.Count, language, timer.Elapsed.TotalSeconds);
    }

    // Two elements share a key when they are the same type with the same text and values.
    private static string TranslationKey(object source)
    {
        var type = source.GetOriginalType();
        var key = new StringBuilder(type.FullName);
        foreach (var prop in GetContentProperties(type))
        {
            if (prop.PropertyType == typeof(ContentArea))
                continue;

            key.Append('\n').Append(prop.Name).Append('=').Append(prop.GetValue(source));
        }

        return key.ToString();
    }

    // Copies the writable property values of one element onto another of the same type.
    private static void CopyContent(object from, object to)
    {
        foreach (var prop in GetContentProperties(from.GetOriginalType()))
        {
            if (prop.PropertyType == typeof(ContentArea))
                continue;

            object? value = prop.GetValue(from);
            // XhtmlString is a reference type; give each element its own rather than sharing one instance.
            prop.SetValue(to, value is XhtmlString html ? new XhtmlString(html.ToString()) : value);
        }
    }

    private async Task CloneNodesAsync(
        IEnumerable<CompositionNode> sourceNodes,
        StructureNode parent,
        string masterLanguage,
        string language,
        List<TranslationJob> translations,
        InstanceCursor cursor,
        CancellationToken ct)
    {
        foreach (var node in sourceNodes)
        {
            ct.ThrowIfCancellationRequested();

            CompositionNode? copy;
            switch (node)
            {
                case SectionNode section when parent is Composition root:
                    cursor.StartSection(cursor.NextSlot());
                    copy = await CloneSectionAsync(section, root, masterLanguage, language, translations, cursor, ct).ConfigureAwait(false);
                    break;

                case ComponentNode component:
                    // A component directly on the page is a shared block, which takes a slot but has no inline element of its own.
                    if (parent is Composition)
                        cursor.NextSlot();
                    copy = await CloneComponentAsync(component, parent, masterLanguage, language, translations, cursor, ct).ConfigureAwait(false);
                    break;

                case StructureNode row when row.NodeType == LayoutStructureType.Row && parent is SectionNode sectionParent:
                    copy = AddRowNode(sectionParent, row.Name);
                    await CloneNodesAsync(row.Nodes, (StructureNode)copy, masterLanguage, language, translations, cursor, ct).ConfigureAwait(false);
                    break;

                case StructureNode column when column.NodeType == LayoutStructureType.Column:
                    copy = AddColumnNode(parent, column.Name);
                    await CloneNodesAsync(column.Nodes, (StructureNode)copy, masterLanguage, language, translations, cursor, ct).ConfigureAwait(false);
                    break;

                default:
                    logger.LogWarning("Skipping a {NodeType} composition node when copying a layout to '{Language}'.", node.NodeType, language);
                    continue;
            }

            if (node.DisplayTemplateKey is not null || node.DisplaySettings is { Count: > 0 })
                copy.WithStyles(node.DisplayTemplateKey!, node.DisplaySettings is null ? null! : new Dictionary<string, string>(node.DisplaySettings));
        }
    }

    private async Task<SectionNode> CloneSectionAsync(
        SectionNode section, Composition root, string masterLanguage, string language,
        List<TranslationJob> translations, InstanceCursor cursor, CancellationToken ct)
    {
        var data = section.SectionData;

        // A shared section is referenced by link and its rows live in the shared item itself.
        if (HasIdentity(data))
            return section.Name is null ? root.AddSection(data) : root.AddSection(data, section.Name);

        var clone = (SectionData)data.CreateWritableClone();
        translations.Add(new TranslationJob(data, clone, cursor.SectionInstance));

        var copy = section.Name is null ? root.AddSection(clone) : root.AddSection(clone, section.Name);
        await CloneNodesAsync(section.Nodes, copy, masterLanguage, language, translations, cursor, ct).ConfigureAwait(false);
        return copy;
    }

    private async Task<ComponentNode> CloneComponentAsync(
        ComponentNode component, StructureNode parent, string masterLanguage, string language,
        List<TranslationJob> translations, InstanceCursor cursor, CancellationToken ct)
    {
        var block = component.Component;
        if (!HasIdentity(block))
        {
            var clone = (BlockData)block.CreateWritableClone();
            var original = block;
            translations.Add(new TranslationJob(original, clone, cursor.NextElement()));
            block = clone;
        }

        return AddComponentNode(parent, block, component.Name);
    }

    // An inline block is a value embedded in its page; anything with a content link is a separate item.
    private static bool HasIdentity(BlockData block)
        => block is IContent { ContentLink: { } link } && !ContentReference.IsNullOrEmpty(link);

    private async Task TranslateInlineAsync(
        object source, object target, string? instance, string masterLanguage, string language, CancellationToken ct)
    {
        // Each translation sets its own named set, so the translator's rules match the element being translated.
        using var _ = SeedInstanceScope.Enter(instance);

        var type = source.GetOriginalType();
        var props = GetContentProperties(type).ToList();

        if (contentGenerationProvider is IContentTranslationProvider translator)
            await translator.TranslateAsync(source, target, props, masterLanguage, language, type, ct).ConfigureAwait(false);
        else
            await contentGenerationProvider.PopulateAsync(target, props, language, type, imageSizes: null, ct).ConfigureAwait(false);

        VideoApplier.PreserveVideos(source, target);
    }
}
