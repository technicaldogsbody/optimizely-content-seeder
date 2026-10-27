using Microsoft.Extensions.AI;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.AI;

/// <summary>
/// Settings for the <c>IChatClient</c> content generation provider.
/// What to write (prompt, tone of voice…) is part of the seed definition — see <c>IAiSeedDefinition</c>.
/// </summary>
public sealed class ChatClientProviderOptions
{
    /// <summary>
    /// The model to use, sent with every request. Required — there is deliberately no default.
    /// e.g. <c>llama3.2:3b</c> on Ollama, <c>claude-opus-5-5</c> on the Claude API,
    /// <c>anthropic.claude-opus-5-5</c> on Bedrock, or the deployment name on Azure OpenAI.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Name shown in the admin UI and log messages, e.g. "Ollama". When not set it is taken from the
    /// client itself (OpenAI, Claude, Ollama…), or shown as "AI" if the client doesn't say.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>Maximum output tokens per request (one request per content item). Default: 16000.</summary>
    public int MaxOutputTokens { get; set; } = 16000;

    /// <summary>
    /// How hard a reasoning model thinks per request. Not sent when null (the default). Short marketing
    /// copy rarely benefits from deep reasoning, so <see cref="ReasoningEffort.Low"/> is a good choice for
    /// Claude and OpenAI reasoning models: faster and cheaper. Leave null for models that don't reason
    /// (e.g. most Ollama models), as some services reject the setting.
    /// </summary>
    public ReasoningEffort? Effort { get; set; }

    /// <summary>
    /// Constrain replies to the exact JSON schema of the item's fields. Default: true. Set false for
    /// services that only support plain JSON mode; replies are then guided by the prompt alone.
    /// </summary>
    public bool UseJsonSchema { get; set; } = true;

    /// <summary>
    /// Ask services that support it (OpenAI, Azure OpenAI) to enforce the schema strictly. Default: true.
    /// Ignored when <see cref="UseJsonSchema"/> is false, and by services without a strict mode.
    /// </summary>
    public bool StrictJsonSchema { get; set; } = true;
}
