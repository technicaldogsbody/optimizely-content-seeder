using System.Linq.Expressions;
using System.Reflection;
using Bogus;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Bogus generator rules for a seed definition, built with <see cref="ForContentType{T}"/>. A rule says how to generate
/// one property of one content type, in strongly typed form. It replaces the string keys of <c>ExtendBogusPropertyMap</c>
/// for this definition only. See <see cref="ISeedDefinition.Generators"/>.
/// <example>
/// <code>
/// public ContentGenerators Generators { get; } = new ContentGenerators()
///     .ForContentType&lt;HeroBlock&gt;(t => t
///         .Property(x => x.Heading, f => f.Commerce.ProductAdjective() + " " + f.Commerce.Product())
///         .Property(x => x.Heading, "zh", f => f.PickRandom("适合远足的装备", "长途穿越的装备")))
///     .ForContentType&lt;PromoBlock&gt;(t => t.Property(x => x.Title, f => f.Commerce.Department()));
/// </code>
/// </example>
/// </summary>
public sealed class ContentGenerators
{
    private readonly List<ContentTypeGenerators> _contentTypes = [];

    /// <summary>
    /// Adds rules for a content type. They also apply to types that derive from <typeparamref name="T"/>, and a rule on a
    /// more derived type wins over one on its base type.
    /// </summary>
    public ContentGenerators ForContentType<T>(Action<ContentTypeGenerators<T>> configure) where T : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        var rules = new ContentTypeGenerators<T>();
        configure(rules);
        _contentTypes.Add(rules);
        return this;
    }

    /// <summary>
    /// Adds a named set of rules for a content type, used by the items that name it (see the <c>instance</c> argument on the
    /// seed tree's nodes and blocks). For any property the named set has a rule for, that rule is used; otherwise the unnamed rule
    /// applies. The same inheritance by base type applies.
    /// </summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public ContentGenerators ForContentType<T>(string instance, Action<ContentTypeGenerators<T>> configure) where T : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        var rules = new ContentTypeGenerators<T>(DefinitionInstance.Normalise(instance));
        configure(rules);
        _contentTypes.Add(rules);
        return this;
    }

    /// <summary>
    /// The rule for one property of a content type, or null if none applies.
    /// <para>
    /// <paramref name="language"/> is the CMS language tag being generated, such as <c>zh</c> or <c>ar-AE</c>. A rule for
    /// that language wins over the language-neutral rule; for <c>zh-Hans</c> the rule for <c>zh</c> is used if there is no
    /// rule for <c>zh-Hans</c> itself. Where several content types match, the most derived type wins, and for the same type
    /// the rule added last wins.
    /// </para>
    /// </summary>
    public Func<Faker, object>? Resolve(Type contentType, string propertyName, string? language = null, string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        // A named set's rule for the property wins; otherwise the unnamed set's rule applies.
        if (instance is not null && FindRule(contentType, propertyName, language, instance) is { } named)
            return named;

        return FindRule(contentType, propertyName, language, instance: null);
    }

    /// <summary>
    /// True when the rule that applies to a property (in a named set, failing that the unnamed set) is the kitchen-sink block.
    /// Used by the AI provider, which leaves such a property alone.
    /// </summary>
    internal bool IsKitchenSink(Type contentType, string propertyName, string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        if (instance is not null && FindKitchenSink(contentType, propertyName, instance) is { } named)
            return named;

        return FindKitchenSink(contentType, propertyName, instance: null) ?? false;
    }

    /// <summary>
    /// True when a named set has rules for a content type (or a base type of it). Used to check that a node's
    /// <c>instance</c> names a set that exists.
    /// </summary>
    internal bool HasInstance(Type contentType, string instance)
        => _contentTypes.Any(r => r.Instance == instance && r.ContentType.IsAssignableFrom(contentType));

    // The rule for a property from one set (null for the unnamed set): the most derived type wins, and for the same type
    // the rule added last wins.
    private Func<Faker, object>? FindRule(Type contentType, string propertyName, string? language, string? instance)
    {
        Func<Faker, object>? best = null;
        int bestDepth = -1;
        foreach (var rules in _contentTypes)
        {
            if (rules.Instance != instance || !rules.ContentType.IsAssignableFrom(contentType))
                continue;

            var generate = rules.Find(propertyName, language);
            if (generate is null)
                continue;

            // >= so that a later registration of the same type wins over an earlier one.
            int depth = InheritanceDepth(rules.ContentType);
            if (depth >= bestDepth)
            {
                best = generate;
                bestDepth = depth;
            }
        }

        return best;
    }

    // The kitchen-sink flag of the rule for a property from one set, or null when that set has no rule for the property.
    // Only language-neutral rules are considered, as before.
    private bool? FindKitchenSink(Type contentType, string propertyName, string? instance)
    {
        ContentTypeGenerators? best = null;
        int bestDepth = -1;
        foreach (var rules in _contentTypes)
        {
            if (rules.Instance != instance || !rules.ContentType.IsAssignableFrom(contentType) || !rules.Rules.ContainsKey(propertyName))
                continue;

            int depth = InheritanceDepth(rules.ContentType);
            if (depth >= bestDepth)
            {
                best = rules;
                bestDepth = depth;
            }
        }

        return best?.IsKitchenSink(propertyName);
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

/// <summary>Generator rules for one content type, built through <see cref="ContentTypeGenerators{T}"/>.</summary>
public abstract class ContentTypeGenerators
{
    private protected ContentTypeGenerators(string? instance = null)
    {
        Instance = instance;
    }

    /// <summary>
    /// The named set these rules belong to, or null for the unnamed set.
    /// </summary>
    internal string? Instance { get; }

    // Language-neutral rules: property name → generator.
    private protected readonly Dictionary<string, Func<Faker, object>> _rules = new(StringComparer.Ordinal);

    // Per-language rules: property name → language tag → generator.
    private protected readonly Dictionary<string, Dictionary<string, Func<Faker, object>>> _localized =
        new(StringComparer.Ordinal);

    // Properties whose rule is the kitchen-sink block. The AI provider leaves these alone, so the fixed structure survives.
    private protected readonly HashSet<string> _kitchenSink = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the rule for a property is the kitchen-sink block, which the AI provider does not overwrite.
    /// </summary>
    internal bool IsKitchenSink(string propertyName) => _kitchenSink.Contains(propertyName);

    /// <summary>
    /// The content type these rules apply to.
    /// </summary>
    internal abstract Type ContentType { get; }
    /// <summary>
    /// The language-neutral generator for each property, keyed by property name.
    /// </summary>
    internal IReadOnlyDictionary<string, Func<Faker, object>> Rules => _rules;

    /// <summary>
    /// The generator for a property in a language: the rule for the exact tag, then for its base language, then the
    /// language-neutral rule. Null if there is none.
    /// </summary>
    internal Func<Faker, object>? Find(string propertyName, string? language)
    {
        if (language is not null && _localized.TryGetValue(propertyName, out var byLanguage))
        {
            for (string tag = language; tag.Length > 0; tag = TrimLastSubtag(tag))
            {
                if (byLanguage.TryGetValue(tag, out var localized))
                    return localized;
            }
        }

        return _rules.GetValueOrDefault(propertyName);
    }

    // "zh-Hans-CN" → "zh-Hans" → "zh" → "" (which ends the loop).
    private static string TrimLastSubtag(string tag)
    {
        int dash = tag.LastIndexOf('-');
        return dash > 0 ? tag[..dash] : string.Empty;
    }
}

/// <summary>Fluent, strongly typed generator rules for content type <typeparamref name="T"/>.</summary>
public sealed class ContentTypeGenerators<T> : ContentTypeGenerators where T : class
{
    /// <summary>Rules for the unnamed set of content type <typeparamref name="T"/>.</summary>
    public ContentTypeGenerators() : this(null) { }

    internal ContentTypeGenerators(string? instance) : base(instance) { }

    /// <inheritdoc/>
    internal override Type ContentType => typeof(T);

    /// <summary>
    /// The generator for one property in every language, e.g. <c>.Property(x => x.Heading, f => f.Lorem.Sentence(4))</c>.
    /// The result must be the property's type, or a string for a rich text property.
    /// </summary>
    public ContentTypeGenerators<T> Property(Expression<Func<T, object?>> property, Func<Faker, object> generate)
    {
        ArgumentNullException.ThrowIfNull(generate);
        string name = PropertyName(property);
        _rules[name] = generate;
        _kitchenSink.Remove(name);
        return this;
    }

    /// <summary>
    /// Leaves a property blank: the seeder does not set it, so the field is empty rather than given a generated value. Use it
    /// for a field that should be empty. A global <c>ExtendBogusPropertyMap</c> entry for the same property does not apply.
    /// </summary>
    public ContentTypeGenerators<T> Blank(Expression<Func<T, object?>> property)
    {
        // A rule that produces nothing: the provider sees a null value and leaves the property unset.
        string name = PropertyName(property);
        _rules[name] = _ => null!;
        _kitchenSink.Remove(name);
        return this;
    }

    /// <summary>
    /// Fills a rich text property with generated HTML, as set by <paramref name="configure"/>: which tags
    /// may be added to words at random, how often, and how many paragraphs. For example
    /// <c>.RichText(x =&gt; x.Body, o =&gt; o.Tags("strong", "em").Probability(0.2))</c>. Bogus only: the AI provider writes its own formatting.
    /// <para>
    /// The property is checked when a value is seeded, not here: a rich text rule on a property that is not rich text is
    /// skipped with a warning.
    /// </para>
    /// </summary>
    public ContentTypeGenerators<T> RichText(Expression<Func<T, object?>> property, Action<RichTextOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        string name = PropertyName(property);

        var options = new RichTextOptions();
        configure(options);

        _rules[name] = faker => RichTextGenerator.Build(faker, options);
        if (options.IsKitchenSink)
            _kitchenSink.Add(name);
        else
            _kitchenSink.Remove(name);
        return this;
    }

    /// <summary>
    /// Fills a rich text property with a fixed block that uses every common editor tag, in a set order, for
    /// checking how a front end renders them. The AI provider leaves this property alone.
    /// </summary>
    public ContentTypeGenerators<T> KitchenSink(Expression<Func<T, object?>> property)
        => RichText(property, o => o.KitchenSink());

    /// <summary>
    /// The generator for one property in one language, given as a CMS language tag such as <c>"zh"</c>, <c>"ar-AE"</c> or
    /// <c>"sv"</c>. It is used only when that language is generated; other languages use the language-neutral rule, if any.
    /// </summary>
    public ContentTypeGenerators<T> Property(Expression<Func<T, object?>> property, string language, Func<Faker, object> generate)
    {
        ArgumentNullException.ThrowIfNull(generate);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        string name = PropertyName(property);
        if (!_localized.TryGetValue(name, out var byLanguage))
            _localized[name] = byLanguage = new Dictionary<string, Func<Faker, object>>(StringComparer.OrdinalIgnoreCase);

        byLanguage[language.Trim()] = generate;
        return this;
    }

    // Same rule as ContentTypeBrief<T>: x => x.Heading names "Heading". A value-type property is boxed by a Convert node.
    private static string PropertyName(Expression<Func<T, object?>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = expression.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : expression.Body;
        return body is MemberExpression { Member: PropertyInfo property }
            ? property.Name
            : throw new ArgumentException($"Expression '{expression}' must select a property, e.g. x => x.Heading.", nameof(expression));
    }
}
