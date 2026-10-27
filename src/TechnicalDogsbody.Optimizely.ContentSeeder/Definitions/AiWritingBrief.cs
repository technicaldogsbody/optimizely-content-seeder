using System.Linq.Expressions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// A seed definition that also tells AI content generation providers how to write its content.
/// Definitions that only implement <see cref="ISeedDefinition"/> are written with an empty brief.
/// The default Bogus provider ignores the brief.
/// </summary>
public interface IAiSeedDefinition : ISeedDefinition
{
    /// <summary>The writing brief: how the AI should write this definition's content.</summary>
    AiWritingBrief WritingBrief { get; }
}

/// <summary>
/// The writing brief for an <see cref="IAiSeedDefinition"/>: what the site is about, how it should
/// sound, and per-content-type / per-property guidance, all strongly typed.
/// <example>
/// <code>
/// public AiWritingBrief WritingBrief { get; } = new AiWritingBrief
/// {
///     Prompt = "An online shop for independent coffee roasters.",
///     ToneOfVoice = "Warm and knowledgeable. Plain English.",
/// }
/// .ForContentType&lt;HeroSection&gt;(t => t
///     .Prompt("Seasonal campaign heroes.")
///     .Property(x => x.Heading, "Five words maximum.")
///     .UseGenerator(x => x.CtaButtonText)   // Bogus fills it; the AI does not
///     .Blank(x => x.Tagline));            // left blank
/// </code>
/// </example>
/// </summary>
public sealed class AiWritingBrief
{
    private readonly List<ContentTypeBrief> _contentTypes = [];

    /// <summary>What the site is and what its content is about — the main prompt.</summary>
    public string? Prompt { get; init; }

    /// <summary>How the copy should sound, e.g. "Warm, knowledgeable and a little playful."</summary>
    public string? ToneOfVoice { get; init; }

    /// <summary>Who the content is written for.</summary>
    public string? Audience { get; init; }

    /// <summary>Any other house-style rules, e.g. "British spelling. Sentence case for headings."</summary>
    public string? Instructions { get; init; }

    /// <summary>Words or phrases the copy must never use.</summary>
    public IReadOnlyList<string> AvoidPhrases { get; init; } = [];

    /// <summary>Upper bound on paragraphs for rich-text (XhtmlString) properties. Default: 4.</summary>
    public int MaxParagraphs { get; init; } = 4;

    /// <summary>
    /// Adds guidance for a content type. Also applies to types deriving from <typeparamref name="T"/>,
    /// so <c>ForContentType&lt;BlockData&gt;</c> covers every block; more-derived guidance wins.
    /// </summary>
    public AiWritingBrief ForContentType<T>(Action<ContentTypeBrief<T>> configure) where T : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        var brief = new ContentTypeBrief<T>();
        configure(brief);
        _contentTypes.Add(brief);
        return this;
    }

    /// <summary>
    /// Adds a named set of guidance for a content type, used by the items that name it (see the <c>instance</c> argument on
    /// the seed tree's nodes and blocks). It applies on top of the unnamed guidance: anything it sets replaces the default's
    /// for that property, and anything it leaves alone is inherited. The same inheritance by base type applies.
    /// </summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public AiWritingBrief ForContentType<T>(string instance, Action<ContentTypeBrief<T>> configure) where T : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        var brief = new ContentTypeBrief<T>(DefinitionInstance.Normalise(instance));
        configure(brief);
        _contentTypes.Add(brief);
        return this;
    }

    /// <summary>Resolves the combined guidance for a content type (base types first, most derived last).</summary>
    public ResolvedContentTypeBrief Resolve(Type contentType) => Resolve(contentType, instance: null);

    /// <summary>
    /// Resolves the guidance for a content type as used by one named set. The unnamed guidance is folded first, then the
    /// named guidance on top: a property the named set gives guidance for takes that guidance and drops any inherited length
    /// limit, unless the named set gives its own. A null <paramref name="instance"/> resolves the unnamed guidance alone.
    /// </summary>
    public ResolvedContentTypeBrief Resolve(Type contentType, string? instance)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        var defaults = Applicable(contentType, instance: null);
        var named = instance is null ? new List<ContentTypeBrief>() : Applicable(contentType, instance);

        var prompts = new List<string>();
        var guidance = new Dictionary<string, string>(StringComparer.Ordinal);
        var limits = new Dictionary<string, PropertyLimit>(StringComparer.Ordinal);
        var blank = new HashSet<string>(StringComparer.Ordinal);
        var generated = new HashSet<string>(StringComparer.Ordinal);

        foreach (var brief in defaults)
        {
            AddPrompt(prompts, brief);
            foreach (var (name, text) in brief.PropertyGuidance)
                guidance[name] = text;
            foreach (var (name, limit) in brief.PropertyLimits)
                limits[name] = limit;
            Fold(brief, blank, generated);
        }

        foreach (var brief in named)
        {
            AddPrompt(prompts, brief);
            foreach (var (name, text) in brief.PropertyGuidance)
            {
                guidance[name] = text;
                limits.Remove(name);
            }
            foreach (var (name, limit) in brief.PropertyLimits)
                limits[name] = limit;
            Fold(brief, blank, generated);
        }

        return new ResolvedContentTypeBrief(prompts, guidance, limits, blank, generated);
    }

    /// <summary>
    /// True when a named set has guidance for a content type (or a base type of it). Used to check that a node's
    /// <c>instance</c> names a set that exists.
    /// </summary>
    internal bool HasInstance(Type contentType, string instance)
        => _contentTypes.Any(b => b.Instance == instance && b.ContentType.IsAssignableFrom(contentType));

    // The briefs for a content type, base types first, for one set (null for the unnamed set).
    private List<ContentTypeBrief> Applicable(Type contentType, string? instance)
        => _contentTypes
            .Where(b => b.Instance == instance && b.ContentType.IsAssignableFrom(contentType))
            .OrderBy(b => InheritanceDepth(b.ContentType))
            .ToList();

    private static void AddPrompt(List<string> prompts, ContentTypeBrief brief)
    {
        if (!string.IsNullOrWhiteSpace(brief.PromptText))
            prompts.Add(brief.PromptText);
    }

    // The later declaration for a property wins: Blank and UseGenerator are mutually exclusive.
    private static void Fold(ContentTypeBrief brief, HashSet<string> blank, HashSet<string> generated)
    {
        foreach (var name in brief.BlankProperties)
        {
            blank.Add(name);
            generated.Remove(name);
        }
        foreach (var name in brief.GeneratedProperties)
        {
            generated.Add(name);
            blank.Remove(name);
        }
    }

    private static int InheritanceDepth(Type type)
    {
        if (type.IsInterface)
            return 0;
        int depth = 0;
        for (var t = type; t is not null; t = t.BaseType)
            depth++;
        return depth;
    }
}

/// <summary>Guidance for one content type, built through <see cref="ContentTypeBrief{T}"/>.</summary>
public abstract class ContentTypeBrief
{
    private protected readonly Dictionary<string, string> _guidance = new(StringComparer.Ordinal);
    private protected readonly Dictionary<string, PropertyLimit> _limits = new(StringComparer.Ordinal);
    private protected readonly HashSet<string> _blank = new(StringComparer.Ordinal);
    private protected readonly HashSet<string> _generated = new(StringComparer.Ordinal);

    private protected ContentTypeBrief(string? instance = null)
    {
        Instance = instance;
    }

    /// <summary>
    /// The named set this guidance belongs to, or null for the unnamed set.
    /// </summary>
    internal string? Instance { get; }

    /// <summary>
    /// The content type these rules apply to.
    /// </summary>
    internal abstract Type ContentType { get; }
    /// <summary>
    /// The prompt text written for this content type, if any.
    /// </summary>
    internal string? PromptText { get; private protected set; }
    /// <summary>
    /// The guidance written for each property, keyed by property name.
    /// </summary>
    internal IReadOnlyDictionary<string, string> PropertyGuidance => _guidance;
    /// <summary>
    /// The word and character limits for each property, keyed by property name.
    /// </summary>
    internal IReadOnlyDictionary<string, PropertyLimit> PropertyLimits => _limits;

    /// <summary>Properties left blank under the AI provider (see <see cref="ContentTypeBrief{T}.Blank"/>).</summary>
    internal IReadOnlyCollection<string> BlankProperties => _blank;

    /// <summary>Properties filled by Bogus under the AI provider (see <see cref="ContentTypeBrief{T}.UseGenerator"/>).</summary>
    internal IReadOnlyCollection<string> GeneratedProperties => _generated;
}

/// <summary>Fluent, strongly typed guidance for content type <typeparamref name="T"/>.</summary>
public sealed class ContentTypeBrief<T> : ContentTypeBrief where T : class
{
    /// <summary>Guidance for the unnamed set of content type <typeparamref name="T"/>.</summary>
    public ContentTypeBrief() : this(null) { }

    internal ContentTypeBrief(string? instance) : base(instance) { }

    /// <inheritdoc/>
    internal override Type ContentType => typeof(T);

    /// <summary>What items of this type are, e.g. "In-depth buying guides, 600–900 words."</summary>
    public ContentTypeBrief<T> Prompt(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        PromptText = prompt;
        return this;
    }

    /// <summary>
    /// Guidance for one property, e.g. <c>.Property(x => x.Heading, "A punchy headline.", maxWords: 5)</c>.
    /// Also opts in properties the provider would otherwise leave to Bogus (e.g. names ending in "Url").
    /// </summary>
    /// <param name="maxWords">
    /// A hard limit, enforced for plain-text properties: a reply that breaks it is sent back once to be
    /// shortened, then trimmed to fit. For rich text it is only passed to the model as a requirement.
    /// </param>
    /// <param name="maxCharacters">As <paramref name="maxWords"/>, in characters.</param>
    /// <param name="property">The property to write, for example x =&gt; x.Heading.</param>
    /// <param name="guidance">What the property should say, in plain words.</param>
    public ContentTypeBrief<T> Property(
        Expression<Func<T, object?>> property, string guidance, int? maxWords = null, int? maxCharacters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guidance);
        if (maxWords is < 1)
            throw new ArgumentOutOfRangeException(nameof(maxWords), maxWords, "maxWords must be at least 1.");
        if (maxCharacters is < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCharacters), maxCharacters, "maxCharacters must be at least 1.");

        string name = PropertyName(property);
        _guidance[name] = guidance;
        if (maxWords is not null || maxCharacters is not null)
            _limits[name] = new PropertyLimit(maxWords, maxCharacters);
        else
            _limits.Remove(name);
        return this;
    }

    /// <summary>
    /// Leaves a property blank under the AI provider: the model does not write it, and the Bogus placeholder is removed
    /// too, so the field is empty. Use it for a field that should be empty. If the property is <c>[Required]</c>, a blank
    /// value means the item is saved as a draft (unpublished) rather than published.
    /// </summary>
    public ContentTypeBrief<T> Blank(Expression<Func<T, object?>> property)
    {
        string name = PropertyName(property);
        _blank.Add(name);
        _generated.Remove(name);
        return this;
    }

    /// <summary>
    /// Under the AI provider, has Bogus fill a property (its generator rule, or Bogus's default) instead of the model.
    /// Use it for a field that should not be prose, such as a fixed title or a code.
    /// </summary>
    public ContentTypeBrief<T> UseGenerator(Expression<Func<T, object?>> property)
    {
        string name = PropertyName(property);
        _generated.Add(name);
        _blank.Remove(name);
        return this;
    }

    private static string PropertyName(Expression<Func<T, object?>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = expression.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : expression.Body;
        return body is MemberExpression { Member: System.Reflection.PropertyInfo property }
            ? property.Name
            : throw new ArgumentException($"Expression '{expression}' must select a property, e.g. x => x.Heading.", nameof(expression));
    }
}

/// <summary>The guidance that applies to one content type once inheritance is taken into account.</summary>
public sealed record ResolvedContentTypeBrief(
    IReadOnlyList<string> Prompts,
    IReadOnlyDictionary<string, string> PropertyGuidance,
    IReadOnlyDictionary<string, PropertyLimit> PropertyLimits,
    IReadOnlySet<string> BlankProperties,
    IReadOnlySet<string> GeneratedProperties);

/// <summary>A hard limit on the length of a text property's value. See <see cref="ContentTypeBrief{T}.Property"/>.</summary>
public sealed record PropertyLimit(int? MaxWords = null, int? MaxCharacters = null);
