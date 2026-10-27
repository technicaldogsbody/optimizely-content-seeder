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
    public async Task PopulateAsync(
        object content,
        IEnumerable<PropertyInfo> properties,
        string language,
        Type? contentType = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        CancellationToken ct = default)
    {
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
                        var image = await imageContentFactory
                            .CreateImageAsync(size.Width, size.Height, ct)
                            .ConfigureAwait(false);

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
