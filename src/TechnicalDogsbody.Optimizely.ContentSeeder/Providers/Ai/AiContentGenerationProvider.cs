using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Ai;

/// <summary>
/// Base class for AI content generation providers. SDK-free, so AI SDKs stay in the add-on packages.
/// <para>
/// Per content item: the Bogus provider populates every property first (images, numbers, dates,
/// URLs and any <c>ExtendBogusPropertyMap</c> overrides), then one structured request asks the
/// model for all of the item's prose properties at once — so a heading, summary and body read as
/// one coherent piece — written to the <see cref="AiWritingBrief"/> of the definition being seeded
/// (see <see cref="IAiSeedDefinition"/>; plain definitions get an empty brief).
/// If a request fails or is declined the item simply keeps its Bogus values.
/// </para>
/// <para>
/// Additional languages are <b>translated</b> from the saved master-language content
/// (<see cref="IContentTranslationProvider"/>) rather than written independently, so every language
/// version says the same thing. Values that aren't prose — images, numbers, dates, URLs — are shared
/// with the master.
/// </para>
/// Derived classes implement only <see cref="GenerateJsonAsync"/>.
/// </summary>
public abstract class AiContentGenerationProvider(
    BogusContentGenerationProvider fallback,
    ISeedDefinitionAccessor definitionAccessor,
    IRichTextConverter richText,
    ILogger logger,
    IContentModelAdapter? contentModel = null) : IContentGenerationProvider, IContentTranslationProvider, IDescribesProvider
{
    private static readonly AiWritingBrief _emptyBrief = new();

    private readonly IContentModelAdapter _model = contentModel ?? new NullContentModelAdapter();

    // Property-name suffixes that hold machine values, not prose — left to Bogus. Matched
    // case-sensitively on the PascalCase boundary, so "ButtonUrl" matches but "Paid" doesn't.
    private static readonly string[] _nonProseSuffixes =
    [
        "Url", "Uri", "Href", "Link", "Email", "Phone", "Telephone", "Color", "Colour",
        "Id", "Key", "Code", "Sku", "Slug", "Path", "Icon", "CssClass", "Class", "Target", "Template",
    ];

    // Set after a configuration error (bad key, unknown model…) so a run doesn't make hundreds of
    // doomed calls. Providers are scoped per operation, so the next run tries again.
    private string? _disabledReason;

    /// <summary>Display name used in log messages and the admin UI, e.g. "OpenAI".</summary>
    protected abstract string ProviderName { get; }

    /// <summary>The model in use, shown in the admin UI. Optional.</summary>
    protected virtual string? ModelName => null;

    /// <inheritdoc/>
    public ProviderInfo Describe() => new(ProviderName, ModelName, IsAi: true);

    /// <summary>
    /// Sends the request and returns the model's JSON object as text, or null if it produced none
    /// (e.g. the request was declined). Throw on transport/API errors.
    /// </summary>
    protected abstract Task<string?> GenerateJsonAsync(AiContentRequest request, CancellationToken ct);

    /// <summary>
    /// True for errors that will fail every request (authentication, permissions, unknown model,
    /// invalid request). AI generation is then switched off for the rest of the run.
    /// </summary>
    protected virtual bool IsConfigurationError(Exception exception) => false;

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
    {
        var props = properties as IReadOnlyList<PropertyInfo> ?? properties.ToList();
        var brief = (definitionAccessor.Current as IAiSeedDefinition)?.WritingBrief ?? _emptyBrief;
        var type = contentType ?? content.GetType();
        var typeBrief = brief.Resolve(type, SeedInstanceScope.Current);

        // The Bogus fallback applies the definition's generator rules too, so they set the placeholder values that an
        // item keeps if the AI request fails. The AI itself is driven by the writing brief, not by those rules. Fields the
        // brief leaves blank are cleared again, whether or not the AI runs.
        await fallback.PopulateAsync(content, props, language, contentType, imageSizes, imageProviders, ct).ConfigureAwait(false);
        ClearBlank(content, props, typeBrief.BlankProperties);

        if (_disabledReason is not null)
            return;

        // The item's own prose, and the prose of its local blocks and block list items, in one request, so they read as
        // one piece.
        var targets = CollectTargets(content, source: null, props, type, brief, typeBrief, prefix: "", depth: 0, LanguageVersionScope.IsActive);
        if (targets.Count == 0)
            return;

        string typeName = type.Name;
        var request = AiPromptBuilder.BuildRequest(typeName, language, targets.Select(t => t.Field).ToList(), brief, typeBrief);

        string? json = await RequestJsonAsync(request, typeName, ct).ConfigureAwait(false);
        if (json is not null)
            await ApplyAsync(targets, json, language, brief, typeName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task TranslateAsync(
        object source,
        object target,
        IEnumerable<PropertyInfo> properties,
        string sourceLanguage,
        string targetLanguage,
        Type? contentType = null,
        CancellationToken ct = default)
    {
        var props = properties as IReadOnlyList<PropertyInfo> ?? properties.ToList();
        var brief = (definitionAccessor.Current as IAiSeedDefinition)?.WritingBrief ?? _emptyBrief;
        var type = contentType ?? target.GetType();
        var typeBrief = brief.Resolve(type, SeedInstanceScope.Current);
        var ownTargets = SelectTextProperties(props, typeBrief, definitionAccessor.Current?.Generators, type);
        var proseNames = ownTargets.Select(t => t.Property.Name).ToHashSet(StringComparer.Ordinal);

        // Everything except the prose being translated: share the master's value where that is safe
        // (the same image, number, date, URL…), and generate the rest in the target locale. Local blocks and block
        // lists take the master's structure (the same items, in the same order), so their prose can be translated too.
        var placeholders = new List<PropertyInfo>();
        foreach (var prop in props)
        {
            if (!prop.CanWrite || proseNames.Contains(prop.Name))
                continue;

            if (IsShareable(prop.PropertyType))
                prop.SetValue(target, prop.GetValue(source));
            else if (prop.CanRead && IsBlockProperty(prop.PropertyType))
                CopyBlockProperty(source, target, prop, depth: 0, LanguageVersionScope.IsActive);
            else
                placeholders.Add(prop);
        }

        // Placeholder text in the target locale for the prose too, so if the translation fails the item is
        // still in the right language (not a copy of the master's) rather than left empty.
        placeholders.AddRange(ownTargets.Select(t => t.Property));
        await fallback.PopulateAsync(target, placeholders, targetLanguage, type, imageSizes: null, ct).ConfigureAwait(false);
        ClearBlank(target, props, typeBrief.BlankProperties);

        var targets = CollectTargets(target, source, props, type, brief, typeBrief, prefix: "", depth: 0, LanguageVersionScope.IsActive);
        foreach (var nested in targets.Where(t => !ReferenceEquals(t.Owner, target)))
        {
            await fallback.PopulateAsync(nested.Owner, [nested.Property], targetLanguage, _model.GetOriginalType(nested.Owner), imageSizes: null, ct)
                .ConfigureAwait(false);
        }

        if (_disabledReason is not null)
            return;

        var fields = new List<AiTextField>();
        var translatable = new List<Target>();
        foreach (var t in targets)
        {
            if (t.Source is null)
                continue;

            object? current = t.Property.GetValue(t.Source);
            string? text = current as string ?? richText.ReadHtml(current);
            if (string.IsNullOrWhiteSpace(text))
                continue;
            fields.Add(t.Field with { SourceText = text });
            translatable.Add(t);
        }

        if (fields.Count == 0)
            return;

        string typeName = type.Name;
        var request = AiPromptBuilder.BuildTranslationRequest(typeName, sourceLanguage, targetLanguage, fields, brief);

        string? json = await RequestJsonAsync(request, typeName, ct).ConfigureAwait(false);
        if (json is not null)
            await ApplyAsync(translatable, json, targetLanguage, brief, typeName, ct).ConfigureAwait(false);
    }

    // Sends a request and returns the reply, or null if there isn't a usable one. Never throws except
    // for cancellation; a configuration error also switches AI off for the rest of the run.
    private async Task<string?> RequestJsonAsync(AiContentRequest request, string typeName, CancellationToken ct)
    {
        string? json;
        try
        {
            logger.LogDebug("{Provider}: writing {ContentType} ({Language})…", ProviderName, typeName, request.Language);
            var started = System.Diagnostics.Stopwatch.StartNew();
            json = await GenerateJsonAsync(request, ct).ConfigureAwait(false);
            double seconds = started.Elapsed.TotalSeconds;
            if (seconds >= 20)
                logger.LogInformation("{Provider}: {ContentType} ({Language}) took {Seconds:0.0}s. That includes time spent queued when several requests share a model that answers one at a time (see MaxParallelGeneration and OLLAMA_NUM_PARALLEL); a reasoning model thinking before it answers also adds to it (see ChatClientProviderOptions.Effort).", ProviderName, typeName, request.Language, seconds);
            else
                logger.LogDebug("{Provider}: wrote {ContentType} ({Language}) in {Seconds:0.0}s.", ProviderName, typeName, request.Language, seconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsConfigurationError(ex))
        {
            _disabledReason = ex.Message;
            logger.LogError(ex,
                "{Provider} rejected the request for {ContentType}. AI generation is switched off for the rest of this run and generated placeholder values are used instead. Check the provider's API key, model and settings.",
                ProviderName, typeName);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "{Provider} failed to generate content for {ContentType}. Keeping generated placeholder values for this item.",
                ProviderName, typeName);
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("{Provider} returned no content for {ContentType}. Keeping generated placeholder values for this item.",
                ProviderName, typeName);
            return null;
        }

        return json;
    }

    // Values that are safe to hand to another language version unchanged. Reference types such as
    // ContentArea or rich text are not: sharing one instance between two versions is asking for trouble.
    private bool IsShareable(Type type)
        => type.IsValueType || type == typeof(string) || _model.IsContentReference(type);

    // ── property selection ───────────────────────────────────────────────────

    // Empties the properties a brief leaves blank: null for a reference or nullable value, the type's default otherwise.
    private static void ClearBlank(object content, IEnumerable<PropertyInfo> properties, IReadOnlySet<string> blank)
    {
        if (blank.Count == 0)
            return;

        foreach (var prop in properties)
        {
            if (!prop.CanWrite || !blank.Contains(prop.Name))
                continue;

            bool nonNullableValue = prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) is null;
            prop.SetValue(content, nonNullableValue ? Activator.CreateInstance(prop.PropertyType) : null);
        }
    }

    private sealed record Target(PropertyInfo Property, AiTextField Field)
    {
        /// <summary>The object the property is set on: the item itself, or one of its local blocks or list items.</summary>
        public object Owner { get; init; } = null!;

        /// <summary>For translation, the matching object in the master language version.</summary>
        public object? Source { get; init; }
    }

    // The prose targets of an item and, recursively, of its local blocks and block list items. A nested field is named
    // by its path, so it is unique in the request: "Hero_Heading", "FooterColumns_2_Heading". Each nested block uses the
    // writing brief for its own type, and the fields that brief leaves blank are cleared.
    private List<Target> CollectTargets(
        object owner, object? source, IReadOnlyList<PropertyInfo> props, Type type,
        AiWritingBrief brief, ResolvedContentTypeBrief typeBrief, string prefix, int depth, bool languageVersion)
    {
        if (depth > 0)
            ClearBlank(owner, props, typeBrief.BlankProperties);

        var targets = SelectTextProperties(props, typeBrief, definitionAccessor.Current?.Generators, type)
            .Select(t => t with { Field = t.Field with { Name = prefix + t.Field.Name }, Owner = owner, Source = source })
            .ToList();

        if (depth >= BogusContentGenerationProvider.MaxBlockDepth)
            return targets;

        foreach (var prop in props)
        {
            if (!prop.CanRead || typeBrief.BlankProperties.Contains(prop.Name))
                continue;

            if (_model.TryGetBlockListItemType(prop.PropertyType, out var itemType))
            {
                if (prop.GetValue(owner) is not System.Collections.IList items)
                    continue;

                var sourceItems = source is null ? null : prop.GetValue(source) as System.Collections.IList;
                var itemProps = _model.WritableBlockProperties(itemType, cultureSpecificOnly: false);
                var itemBrief = brief.Resolve(itemType, SeedInstanceScope.Current);
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] is not { } item)
                        continue;
                    object? sourceItem = sourceItems is not null && i < sourceItems.Count ? sourceItems[i] : null;
                    targets.AddRange(CollectTargets(item, sourceItem, itemProps, itemType, brief, itemBrief, $"{prefix}{prop.Name}_{i + 1}_", depth + 1, languageVersion: false));
                }
            }
            else if (_model.IsLocalBlock(prop.PropertyType) && prop.GetValue(owner) is { } block)
            {
                object? sourceBlock = source is null ? null : prop.GetValue(source);
                var blockBrief = brief.Resolve(prop.PropertyType, SeedInstanceScope.Current);
                targets.AddRange(CollectTargets(block, sourceBlock, _model.WritableBlockProperties(prop.PropertyType, languageVersion), prop.PropertyType,
                    brief, blockBrief, $"{prefix}{prop.Name}_", depth + 1, languageVersion));
            }
        }

        return targets;
    }

    private bool IsBlockProperty(Type type) => _model.IsLocalBlock(type) || _model.TryGetBlockListItemType(type, out _);

    // Gives a language version the master's local block or block list: new blocks with the master's values. Rich text
    // gets its own instance, and content areas are left alone, so nothing is shared between the two versions. A local
    // block in a language version gets only its culture-specific values; the rest are the master's already.
    private void CopyBlockProperty(object source, object target, PropertyInfo prop, int depth, bool languageVersion)
    {
        object? value = prop.GetValue(source);

        if (_model.TryGetBlockListItemType(prop.PropertyType, out var itemType))
        {
            if (value is not System.Collections.IList items)
            {
                prop.SetValue(target, null);
                return;
            }

            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            foreach (object? item in items)
            {
                if (item is not null)
                    list.Add(CopyBlock(item, itemType, into: null, depth + 1, languageVersion: false));
            }
            prop.SetValue(target, list);
            return;
        }

        if (value is null)
            return;

        object? existing = prop.GetValue(target);
        object copy = CopyBlock(value, prop.PropertyType, existing, depth + 1, languageVersion);
        if (existing is null)
            prop.SetValue(target, copy);
    }

    private object CopyBlock(object source, Type type, object? into, int depth, bool languageVersion)
    {
        object block = into ?? _model.CreateBlock(type);
        foreach (var prop in _model.WritableBlockProperties(type, languageVersion))
        {
            if (!prop.CanRead || _model.IsContentArea(prop.PropertyType))
                continue;

            if (IsBlockProperty(prop.PropertyType))
            {
                if (depth < BogusContentGenerationProvider.MaxBlockDepth)
                    CopyBlockProperty(source, block, prop, depth, languageVersion);
                continue;
            }

            object? value = prop.GetValue(source);
            if (value is not null && richText.IsRichText(prop.PropertyType))
                value = richText.FromHtml(richText.ReadHtml(value) ?? "");
            prop.SetValue(block, value);
        }
        return block;
    }

    private List<Target> SelectTextProperties(
        IReadOnlyList<PropertyInfo> properties, ResolvedContentTypeBrief typeBrief, ContentGenerators? generators, Type type)
    {
        var targets = new List<Target>();
        foreach (var prop in properties)
        {
            if (!prop.CanWrite)
                continue;

            // A kitchen-sink rich text property keeps its fixed structure, so the model does not write it.
            if (generators?.IsKitchenSink(type, prop.Name, SeedInstanceScope.Current) == true)
                continue;

            AiTextFieldKind kind;
            if (prop.PropertyType == typeof(string)) kind = AiTextFieldKind.ShortText;
            else if (richText.IsRichText(prop.PropertyType)) kind = AiTextFieldKind.RichText;
            else continue;

            // Blank (Blank) and Bogus-filled (UseGenerator) fields are not the model's to write.
            if (typeBrief.BlankProperties.Contains(prop.Name) || typeBrief.GeneratedProperties.Contains(prop.Name))
                continue;

            string? guidance = typeBrief.PropertyGuidance.GetValueOrDefault(prop.Name);

            // Explicit guidance opts a machine-looking property back in.
            if (guidance is null && IsNonProse(prop.Name))
                continue;

            var limit = typeBrief.PropertyLimits.GetValueOrDefault(prop.Name);
            targets.Add(new Target(prop, new AiTextField(prop.Name, kind, guidance, MaxWords: limit?.MaxWords, MaxCharacters: limit?.MaxCharacters)));
        }
        return targets;
    }

    private static bool IsNonProse(string propertyName)
        => _nonProseSuffixes.Any(s => propertyName.EndsWith(s, StringComparison.Ordinal));

    // ── applying the reply ───────────────────────────────────────────────────

    private async Task ApplyAsync(
        List<Target> targets, string json, string language, AiWritingBrief brief, string typeName, CancellationToken ct)
    {
        var values = ParseReply(json, targets, typeName);
        if (values is null)
            return;

        await EnforceLimitsAsync(values, targets, language, typeName, ct).ConfigureAwait(false);

        foreach (var target in targets)
        {
            if (!values.TryGetValue(target.Field.Name, out string? text))
                continue;

            try
            {
                target.Property.SetValue(target.Owner, target.Field.Kind == AiTextFieldKind.RichText
                    ? richText.FromHtml(AiTextFormatting.ToHtml(text, brief.MaxParagraphs))
                    : text);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to set {PropertyName} on {ContentType} from {Provider}. Keeping the generated placeholder value.",
                    target.Field.Name, typeName, ProviderName);
            }
        }
    }

    // The reply's text per field (plain-text fields already cleaned of markdown), or null if unusable.
    private Dictionary<string, string>? ParseReply(string json, IReadOnlyList<Target> targets, string typeName)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "{Provider} returned invalid JSON for {ContentType}. Keeping generated placeholder values for this item.",
                ProviderName, typeName);
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var target in targets)
            {
                if (!document.RootElement.TryGetProperty(target.Field.Name, out var value)
                    || value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(value.GetString()))
                    continue;

                string text = value.GetString()!.Trim();
                // Rich text keeps its HTML for ToHtml to sanitise. A string property never holds markup.
                values[target.Field.Name] = target.Field.Kind == AiTextFieldKind.RichText
                    ? text
                    : AiTextFormatting.StripTags(AiTextFormatting.StripMarkdown(text));
            }
            return values;
        }
    }

    // Hard limits (maxWords / maxCharacters) on plain-text fields. A model told "two or three words" will
    // sometimes write a sentence, so a reply that breaks a limit is sent back once to be shortened, and
    // anything still too long is trimmed to fit.
    private async Task EnforceLimitsAsync(
        Dictionary<string, string> values, List<Target> targets, string language, string typeName, CancellationToken ct)
    {
        bool Breaks(Target t) => t.Field.Kind == AiTextFieldKind.ShortText && values.TryGetValue(t.Field.Name, out string? v) && AiTextFormatting.Exceeds(t.Field, v);

        var tooLong = targets.Where(Breaks).ToList();
        if (tooLong.Count == 0)
            return;

        if (_disabledReason is null)
        {
            var fields = tooLong.Select(t => t.Field with { SourceText = values[t.Field.Name] }).ToList();
            string? json = await RequestJsonAsync(AiPromptBuilder.BuildShortenRequest(typeName, language, fields), typeName, ct).ConfigureAwait(false);
            if (json is not null && ParseReply(json, tooLong, typeName) is { } shortened)
                foreach (var (name, text) in shortened)
                    values[name] = text;
        }

        foreach (var target in targets.Where(Breaks))
        {
            logger.LogWarning("{Provider} wrote {Property} on {ContentType} longer than its limit; trimming it to fit.",
                ProviderName, target.Field.Name, typeName);
            values[target.Field.Name] = AiTextFormatting.TrimToLimit(values[target.Field.Name], target.Field);
        }
    }
}
