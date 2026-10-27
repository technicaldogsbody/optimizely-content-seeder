using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.AI;

/// <summary>
/// Extension methods for registering the <see cref="IChatClient"/> content generation provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the default Bogus <see cref="IContentGenerationProvider"/> with a provider that writes
    /// through the given <see cref="IChatClient"/>. Call after <c>AddContentSeeder()</c>.
    /// <example>
    /// Ollama (OllamaSharp):
    /// <code>
    /// services.UseAiForContentSeeder(
    ///     _ => new OllamaApiClient(new Uri("http://localhost:11434")),
    ///     o => { o.Model = "llama3.2:3b"; o.DisplayName = "Ollama"; });
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="clientFactory">Creates the client. It is registered as a keyed singleton, so it won't clash with an app's own <see cref="IChatClient"/>.</param>
    /// <param name="configure">Configures the provider. <see cref="ChatClientProviderOptions.Model"/> is required and validated at startup.</param>
    public static IServiceCollection UseAiForContentSeeder(
        this IServiceCollection services,
        Func<IServiceProvider, IChatClient> clientFactory,
        Action<ChatClientProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ChatClientProviderOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Model),
                "ContentSeeder chat client provider: Model is required (set ContentSeeder:ChatClient:Model, e.g. llama3.2:3b on Ollama).")
            .Validate(o => o.MaxOutputTokens > 0, "ContentSeeder chat client provider: MaxOutputTokens must be greater than zero.")
            .ValidateOnStart();

        services.TryAddKeyedSingleton<IChatClient>(ChatClientContentGenerationProvider.ClientKey, (sp, _) => clientFactory(sp));

        // Scoped: the provider depends on the scoped Bogus provider (images need scoped CMS services).
        services.RemoveAll<IContentGenerationProvider>();
        services.AddScoped<IContentGenerationProvider, ChatClientContentGenerationProvider>();

        return services;
    }

    /// <summary>
    /// As above, but uses the <see cref="IChatClient"/> the application has already registered
    /// (e.g. via <c>services.AddChatClient(...)</c>).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Configures the provider.</param>
    public static IServiceCollection UseAiForContentSeeder(
        this IServiceCollection services,
        Action<ChatClientProviderOptions> configure)
        => services.UseAiForContentSeeder(sp => sp.GetRequiredService<IChatClient>(), configure);
}
