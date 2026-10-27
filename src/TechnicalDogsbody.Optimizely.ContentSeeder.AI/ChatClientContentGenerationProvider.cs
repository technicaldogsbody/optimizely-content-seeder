using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Ai;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.AI;

/// <summary>
/// Generates content through any Microsoft.Extensions.AI <see cref="IChatClient"/> — Ollama, Azure
/// OpenAI, OpenAI, Gemini and more — one structured request per content item. Non-prose properties,
/// images and failed items fall back to the Bogus provider; see <see cref="AiContentGenerationProvider"/>.
/// Scoped lifetime.
/// </summary>
public sealed class ChatClientContentGenerationProvider(
    BogusContentGenerationProvider fallback,
    ISeedDefinitionAccessor definitionAccessor,
    IRichTextConverter richText,
    [FromKeyedServices(ChatClientContentGenerationProvider.ClientKey)] IChatClient client,
    IOptions<ChatClientProviderOptions> options,
    ILogger<ChatClientContentGenerationProvider> logger,
    IContentModelAdapter? contentModel = null)
    : AiContentGenerationProvider(fallback, definitionAccessor, richText, logger, contentModel)
{
    /// <summary>DI key of the <see cref="IChatClient"/> this provider uses, so it never clashes with an app's own client.</summary>
    public const string ClientKey = "TechnicalDogsbody.Optimizely.ContentSeeder.AI";

    private string? _providerName;

    /// <inheritdoc/>
    protected override string ProviderName => _providerName ??= ResolveProviderName();

    /// <inheritdoc/>
    protected override string? ModelName => options.Value.Model;

    private string ResolveProviderName()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.DisplayName))
            return options.Value.DisplayName;

        string? reported = client.GetService<ChatClientMetadata>()?.ProviderName;
        return string.IsNullOrWhiteSpace(reported) ? "AI" : FriendlyName(reported);
    }

    // The provider names the SDK adapters report, as people write them.
    private static string FriendlyName(string reported) => reported.ToLowerInvariant() switch
    {
        "openai" => "OpenAI",
        "az.ai.openai" or "azure.ai.openai" => "Azure OpenAI",
        "anthropic" => "Claude",
        "ollama" => "Ollama",
        var other => char.ToUpperInvariant(other[0]) + other[1..],
    };

    /// <inheritdoc/>
    protected override async Task<string?> GenerateJsonAsync(AiContentRequest request, CancellationToken ct)
    {
        var opts = options.Value;

        var chatOptions = new ChatOptions
        {
            ModelId = opts.Model,
            MaxOutputTokens = opts.MaxOutputTokens,
            ResponseFormat = opts.UseJsonSchema
                ? ChatResponseFormat.ForJsonSchema(JsonDocument.Parse(request.JsonSchema).RootElement.Clone(), "content_item")
                : ChatResponseFormat.Json,
            Reasoning = opts.Effort is { } effort ? new ReasoningOptions { Effort = effort } : null,
        };

        // OpenAI-style services only enforce the schema strictly when asked; others ignore the flag.
        if (opts.UseJsonSchema && opts.StrictJsonSchema)
            chatOptions.AdditionalProperties = new AdditionalPropertiesDictionary { ["strict"] = true };

        ChatMessage[] messages =
        [
            new(ChatRole.System, request.SystemPrompt),
            new(ChatRole.User, request.UserPrompt),
        ];

        ChatResponse response = await client.GetResponseAsync(messages, chatOptions, ct).ConfigureAwait(false);

        // OpenAI reports a refusal as error content on an otherwise normal reply.
        bool refused = response.Messages.SelectMany(m => m.Contents).OfType<ErrorContent>()
            .Any(c => string.Equals(c.ErrorCode, "Refusal", StringComparison.OrdinalIgnoreCase));

        if (refused || response.FinishReason == ChatFinishReason.ContentFilter)
        {
            logger.LogWarning("{Provider} declined to write {ContentType} (content filter).", ProviderName, request.ContentTypeName);
            return null;
        }

        if (response.FinishReason == ChatFinishReason.Length)
        {
            logger.LogWarning("{Provider} hit the {MaxTokens}-token limit writing {ContentType}; raise ChatClientProviderOptions.MaxOutputTokens.",
                ProviderName, opts.MaxOutputTokens, request.ContentTypeName);
            return null;
        }

        return ExtractJsonObject(response.Text);
    }

    // Some models wrap JSON in a code fence or add a sentence around it, especially without schema
    // enforcement. Take the outermost object; the base class rejects anything that still isn't JSON.
    private static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    /// <inheritdoc/>
    protected override bool IsConfigurationError(Exception exception)
        => ChatClientErrorClassifier.IsConfigurationError(exception);
}
