using System.Collections.Concurrent;
using System.Reflection;
using Bogus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

/// <summary>
/// Implements <see cref="IContentGenerationProvider"/> using Bogus fake data.
/// <para>
/// Resolution order per property:
/// <list type="number">
///   <item><see cref="XhtmlString"/> → lorem paragraph wrapped in &lt;p&gt;</item>
///   <item><see cref="ContentReference"/> in <c>imageSizes</c> → real media asset via <see cref="IImageContentFactory"/></item>
///   <item><see cref="ContentReference"/> not in <c>imageSizes</c> → skipped (left null)</item>
///   <item>Local block → populated in place; block list (<c>IList&lt;TBlock&gt;</c>) → a few populated blocks, within the
///   property's element bounds. Nested image properties are named in <c>imageSizes</c> by path, e.g. <c>"Hero.Image"</c></item>
///   <item>Exact name match in <see cref="IBogusPropertyMap"/></item>
///   <item>Suffix match in <see cref="IBogusPropertyMap"/></item>
///   <item>CLR type fallback via <see cref="IBogusPropertyMap.ResolveByType"/></item>
/// </list>
/// </para>
/// Scoped lifetime — resolved fresh per seed operation to match <see cref="IImageContentFactory"/>.
/// </summary>
public sealed class BogusContentGenerationProvider(
    IImageContentFactory imageContentFactory,
    IBogusPropertyMap propertyMap,
    IOptions<ContentSeederOptions> options,
    ILogger<BogusContentGenerationProvider> logger,
    ISeedDefinitionAccessor? definitionAccessor = null,
    IRichTextConverter? richText = null,
    IContentModelAdapter? contentModel = null) : IContentGenerationProvider, IDescribesProvider
{
    private readonly IRichTextConverter _richText = richText ?? new NullRichTextConverter();

    /// <inheritdoc/>
    public ProviderInfo Describe() => new("Bogus", "Generated placeholder text");

    // Framework properties of the CMS's base content types, which must never be overwritten.
    private readonly IContentModelAdapter _model = contentModel ?? new NullContentModelAdapter();

    /// <inheritdoc/>
    public Task PopulateAsync(
        object content,
        IEnumerable<PropertyInfo> properties,
        string language,
        Type? contentType = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        CancellationToken ct = default)
        => PopulateAsync(content, properties, language, contentType, imageSizes, imageProviders: null, ct);

    /// <inheritdoc/>
    public async Task PopulateAsync(
        object content,
        IEnumerable<PropertyInfo> properties,
        string language,
        Type? contentType,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes,
        IReadOnlyDictionary<string, ImageProvider>? imageProviders,
        CancellationToken ct = default)
        => await PopulateCoreAsync(content, properties, language, contentType, imageSizes, imageProviders, depth: 0,
            languageVersion: LanguageVersionScope.IsActive, ct).ConfigureAwait(false);

    private async Task PopulateCoreAsync(
        object content,
        IEnumerable<PropertyInfo> properties,
        string language,
        Type? contentType,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes,
        IReadOnlyDictionary<string, ImageProvider>? imageProviders,
        int depth,
        bool languageVersion,
        CancellationToken ct)
    {
        if (imageProviders is not null && depth == 0)
        {
            foreach (string name in imageProviders.Keys)
            {
                if (imageSizes is null || !imageSizes.ContainsKey(name))
                    logger.LogWarning(
                        "ContentSeeder: imageProviders names {PropertyName} on {ContentType}, but imageSizes does not, so no image is created for it.",
                        name, contentType?.Name ?? content.GetType().Name);
            }
        }

        var faker = CreateFaker(language);
        string? typeName = contentType?.Name;

        foreach (var prop in properties)
        {
            ct.ThrowIfCancellationRequested();

            if (!prop.CanWrite)
                continue;

            if (_model.FrameworkPropertyNames.Contains(prop.Name))
                continue;

            // Content area — managed by the orchestrator via ContentAreaAssignment.
            if (_model.IsContentArea(prop.PropertyType))
                continue;

            // Content reference — only create an image asset when the caller explicitly
            // declared this property in imageSizes. All other reference properties
            // are left null (they point to specific content the seeder cannot guess).
            if (_model.IsContentReference(prop.PropertyType))
            {
                if (imageSizes is not null && imageSizes.TryGetValue(prop.Name, out var size))
                {
                    try
                    {
                        var image = imageProviders is not null && imageProviders.TryGetValue(prop.Name, out var provider)
                            ? await imageContentFactory.CreateImageAsync(size.Width, size.Height, provider, ct).ConfigureAwait(false)
                            : await imageContentFactory.CreateImageAsync(size.Width, size.Height, ct).ConfigureAwait(false);

                        if (!image.IsEmpty)
                            prop.SetValue(content, _model.FromAsset(prop.PropertyType, image));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to create image asset for {PropertyName} on {ContentType}. Skipping.",
                            prop.Name, content.GetType().Name);
                    }
                }

                continue;
            }

            // Local block (HeroBlock Hero) or block list (IList<SomeBlock>) — each block is populated like any other
            // content, with the image entries whose path starts with this property's name ("Hero.Image"). A rule on the
            // seed definition for the property itself still wins, so it is handled with the other values below.
            bool isList = _model.TryGetBlockListItemType(prop.PropertyType, out var itemType);
            if ((isList || _model.IsLocalBlock(prop.PropertyType))
                && definitionAccessor?.Current?.Generators?.Resolve(
                    contentType ?? _model.GetOriginalType(content), prop.Name, language, SeedInstanceScope.Current) is null)
            {
                var nested = new NestedImages(ChildMap(imageSizes, prop.Name), ChildMap(imageProviders, prop.Name));
                if (isList)
                    await PopulateBlockListAsync(content, prop, itemType!, faker, language, nested, depth, ct).ConfigureAwait(false);
                else
                    await PopulateLocalBlockAsync(content, prop, language, nested, depth, languageVersion, ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                // A rule on the seed definition wins over the global map and the built-in heuristics.
                var rule = definitionAccessor?.Current?.Generators?.Resolve(
                    contentType ?? _model.GetOriginalType(content), prop.Name, language, SeedInstanceScope.Current);
                object? value = rule is not null
                    ? RuleValue(rule(faker), prop)
                    : ResolveValue(prop, faker, typeName);

                if (value is not null)
                    prop.SetValue(content, value);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to set property {PropertyName} on {ContentType}. Skipping.",
                    prop.Name, content.GetType().Name);
            }
        }
    }

    // Local blocks and block lists can nest (a block whose own list holds blocks); this stops a block type that lists itself.
    internal const int MaxBlockDepth = 3;

    // Default number of items when the property sets no upper bound.
    private const int DefaultMaxBlockListItems = 3;

    private sealed record NestedImages(
        IReadOnlyDictionary<string, (int Width, int Height)>? Sizes,
        IReadOnlyDictionary<string, ImageProvider>? Providers);

    // The entries of an image map for the blocks of one property, with that property's name removed from the path:
    // "FooterColumns.Image" becomes "Image" for each block of FooterColumns. Null when there are none.
    internal static IReadOnlyDictionary<string, T>? ChildMap<T>(IReadOnlyDictionary<string, T>? map, string propertyName)
    {
        if (map is null)
            return null;

        string prefix = propertyName + ".";
        Dictionary<string, T>? child = null;
        foreach (var (key, value) in map)
        {
            if (key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal))
                (child ??= new Dictionary<string, T>(StringComparer.Ordinal))[key[prefix.Length..]] = value;
        }

        return child;
    }

    private async Task PopulateLocalBlockAsync(
        object content, PropertyInfo prop, string language, NestedImages images, int depth, bool languageVersion, CancellationToken ct)
    {
        if (depth >= MaxBlockDepth)
            return;

        try
        {
            // The CMS creates a local block with its owner, so it is filled in place. One that is missing is created.
            object? block = prop.CanRead ? prop.GetValue(content) : null;
            bool created = block is null;
            block ??= _model.CreateBlock(prop.PropertyType);

            // In a language version, a local block's properties that are not culture-specific belong to the master.
            await PopulateCoreAsync(block, _model.WritableBlockProperties(prop.PropertyType, cultureSpecificOnly: languageVersion),
                language, prop.PropertyType, images.Sizes, images.Providers, depth + 1, languageVersion, ct).ConfigureAwait(false);

            if (created)
                prop.SetValue(content, block);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to populate local block {PropertyName} on {ContentType}. Skipping.",
                prop.Name, content.GetType().Name);
        }
    }

    private async Task PopulateBlockListAsync(
        object content, PropertyInfo prop, Type itemType, Faker faker, string language, NestedImages images, int depth, CancellationToken ct)
    {
        if (depth >= MaxBlockDepth)
            return;

        try
        {
            var (min, max) = BlockListBounds(prop);
            int count = faker.Random.Int(min, max);

            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            // The list as a whole belongs to one language version, so every property of its items is written.
            var itemProps = _model.WritableBlockProperties(itemType, cultureSpecificOnly: false);

            for (int i = 0; i < count; i++)
            {
                object item = _model.CreateBlock(itemType);
                await PopulateCoreAsync(item, itemProps, language, itemType, images.Sizes, images.Providers, depth + 1, languageVersion: false, ct)
                    .ConfigureAwait(false);
                list.Add(item);
            }

            prop.SetValue(content, list);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to create block list items for {PropertyName} on {ContentType}. Skipping.",
                prop.Name, content.GetType().Name);
        }
    }

    // The item count range for a block list: at least one item, and no more than DefaultMaxBlockListItems unless the
    // property's own bounds say otherwise. Bounds come from [MinElements]/[MaxElements] (matched by name, since they are
    // usually defined in the site's own code), or from [MinLength]/[MaxLength]/[Length].
    internal static (int Min, int Max) BlockListBounds(PropertyInfo prop)
    {
        int? min = null, max = null;

        foreach (var attr in prop.GetCustomAttributesData())
        {
            string name = attr.AttributeType.Name;
            int[] ints = attr.ConstructorArguments
                .Where(a => a.ArgumentType == typeof(int) && a.Value is int)
                .Select(a => (int)a.Value!)
                .ToArray();

            switch (name)
            {
                case "MinElementsAttribute" or "MinLengthAttribute" or "MinItemsAttribute" when ints.Length > 0:
                    min = ints[0];
                    break;
                case "MaxElementsAttribute" or "MaxLengthAttribute" or "MaxItemsAttribute" when ints.Length > 0:
                    max = ints[0];
                    break;
                case "LengthAttribute" when ints.Length > 1:
                    (min, max) = (ints[0], ints[1]);
                    break;
            }
        }

        int lo = Math.Max(1, min ?? 1);
        int hi = max is > 0 ? Math.Min(max.Value, Math.Max(lo, DefaultMaxBlockListItems)) : Math.Max(lo, DefaultMaxBlockListItems);
        return (lo, Math.Max(lo, hi));
    }

    // A rule's rich text becomes the CMS's rich text value. A rule may also return a plain string for a rich text
    // property, which is wrapped the same way. Rich text for any other property is skipped, and the property stays unset.
    private object? RuleValue(object? value, PropertyInfo prop)
    {
        if (value is RichTextValue rich)
        {
            if (_richText.IsRichText(prop.PropertyType))
                return _richText.FromHtml(rich.Html);

            logger.LogWarning(
                "Rich text rule for {PropertyName} on {ContentType} skipped: the property is not a rich text property.",
                prop.Name, prop.DeclaringType?.Name);
            return null;
        }

        return value is string text && _richText.IsRichText(prop.PropertyType)
            ? _richText.FromHtml(text)
            : value;
    }

    private object? ResolveValue(PropertyInfo prop, Faker faker, string? typeName)
    {
        // Rich text always gets a lorem paragraph regardless of property name.
        if (_richText.IsRichText(prop.PropertyType))
            return _richText.FromHtml($"<p>{faker.Lorem.Paragraph()}</p>");

        // Qualified check first ("HeroBlock.Title"), then unqualified, then suffix, then CLR type.
        var generator = propertyMap.Resolve(prop.Name, typeName);
        if (generator is not null)
            return generator(faker);

        return propertyMap.ResolveByType(prop.PropertyType, faker);
    }

    // The provider is scoped to one operation, so this reports each unmapped language once per run
    // rather than once per content item.
    private readonly ConcurrentDictionary<string, bool> _reportedLanguages = new(StringComparer.OrdinalIgnoreCase);

    private Faker CreateFaker(string language)
    {
        string locale = ResolveLocale(language);
        LocalizedLorem.Ensure(locale);
        return new Faker(locale);
    }

    /// <summary>
    /// The Bogus locale for a language: the exact tag if mapped, else the same language without its region or
    /// script ("ja-JP" → "ja", "zh-Hans" → "zh"), else the master language's locale, else "en".
    /// </summary>
    internal string ResolveLocale(string language)
    {
        var map = options.Value.LocaleMap;

        if (map.TryGetValue(language, out string? exact))
            return exact;

        foreach (string parent in ParentTags(language))
        {
            if (map.TryGetValue(parent, out string? parentLocale))
            {
                if (_reportedLanguages.TryAdd(language, true))
                    logger.LogInformation(
                        "Language '{Language}' is not in ContentSeederOptions.LocaleMap; using the locale for '{Parent}' ('{Locale}').",
                        language, parent, parentLocale);
                return parentLocale;
            }
        }

        string? master = definitionAccessor?.Current?.MasterLanguage ?? options.Value.MasterLanguage;
        if (!string.IsNullOrWhiteSpace(master))
        {
            foreach (string tag in ParentTags(master).Prepend(master))
            {
                if (map.TryGetValue(tag, out string? masterLocale))
                {
                    if (_reportedLanguages.TryAdd(language, true))
                        logger.LogWarning(
                            "Language '{Language}' (or its base language) is not in ContentSeederOptions.LocaleMap. Using the master language's locale '{Locale}'. Add an entry to LocaleMap to generate it in its own locale.",
                            language, masterLocale);
                    return masterLocale;
                }
            }
        }

        if (_reportedLanguages.TryAdd(language, true))
            logger.LogWarning(
                "Language '{Language}' not found in ContentSeederOptions.LocaleMap. Falling back to 'en'.",
                language);

        return "en";
    }

    // "zh-Hans-CN" → "zh-Hans", "zh"
    private static IEnumerable<string> ParentTags(string tag)
    {
        for (int dash = tag.LastIndexOf('-'); dash > 0; dash = tag.LastIndexOf('-'))
        {
            tag = tag[..dash];
            yield return tag;
        }
    }
}
