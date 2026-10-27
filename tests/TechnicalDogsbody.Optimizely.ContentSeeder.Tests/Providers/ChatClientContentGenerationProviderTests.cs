using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using System.Net;
using System.Reflection;
using System.Text.Json;
using EPiServer.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;
using TechnicalDogsbody.Optimizely.ContentSeeder.AI;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class ChatClientContentGenerationProviderTests
{
    public class FakeHero : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual string? CtaUrl { get; set; }
    }

    // In-memory IChatClient: records each request and replies via the test's responder.
    private sealed class FakeChatClient(Func<ChatOptions?, ChatResponse> responder) : IChatClient
    {
        public List<ChatOptions?> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(options);
            return Task.FromResult(responder(options));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class Accessor : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; }
    }

    private static ChatResponse Reply(string text, ChatFinishReason? finish = null)
        => new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = finish ?? ChatFinishReason.Stop };

    private static ChatClientContentGenerationProvider Provider(FakeChatClient client, Action<ChatClientProviderOptions>? configure = null)
    {
        var options = new ChatClientProviderOptions { Model = "llama3.2:3b", DisplayName = "Test" };
        configure?.Invoke(options);
        var bogus = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions()),
            NullLogger<BogusContentGenerationProvider>.Instance, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
        return new ChatClientContentGenerationProvider(bogus, new Accessor(), new XhtmlRichTextConverter(), client, OptionsFactory.Create(options),
            NullLogger<ChatClientContentGenerationProvider>.Instance, new OptimizelyContentModelAdapter());
    }

    private static IEnumerable<PropertyInfo> Props => typeof(FakeHero).GetProperties()
        .Where(p => p.Name is nameof(FakeHero.Heading) or nameof(FakeHero.CtaUrl));

    [Fact]
    public async Task PopulateAsync_SendsModelAndJsonSchema_AndAppliesReply()
    {
        var client = new FakeChatClient(_ => Reply("""{"Heading":"Find your next adventure"}"""));
        var hero = new FakeHero();

        await Provider(client).PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("Find your next adventure", hero.Heading);
        var options = Assert.Single(client.Requests);
        Assert.Equal("llama3.2:3b", options!.ModelId);
        var schema = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat).Schema;
        Assert.NotNull(schema);
        Assert.True(schema!.Value.GetProperty("properties").TryGetProperty("Heading", out _));
    }

    [Fact]
    public async Task PopulateAsync_UseJsonSchemaFalse_UsesPlainJsonMode()
    {
        var client = new FakeChatClient(_ => Reply("""{"Heading":"x"}"""));

        await Provider(client, o => o.UseJsonSchema = false).PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.Null(Assert.IsType<ChatResponseFormatJson>(client.Requests.Single()!.ResponseFormat).Schema);
    }

    [Fact]
    public async Task PopulateAsync_ReplyWrappedInCodeFence_IsStillParsed()
    {
        var client = new FakeChatClient(_ => Reply("Here you go:\n```json\n{\"Heading\":\"Pack light, walk far\"}\n```"));
        var hero = new FakeHero();

        await Provider(client).PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("Pack light, walk far", hero.Heading);
    }

    [Fact]
    public async Task PopulateAsync_TruncatedReply_KeepsBogusValue()
    {
        var client = new FakeChatClient(_ => Reply("""{"Heading":"cut of""", ChatFinishReason.Length));
        var hero = new FakeHero();

        await Provider(client).PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.NotEqual("cut of", hero.Heading);
        Assert.False(string.IsNullOrEmpty(hero.Heading));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]      // unknown model (what Ollama returns)
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    public async Task PopulateAsync_ConfigurationError_StopsCallingForTheRestOfTheRun(HttpStatusCode status)
    {
        var client = new FakeChatClient(_ => throw new HttpRequestException("nope", null, status));
        var provider = Provider(client);

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task PopulateAsync_ServerError_OnlySkipsThatItem()
    {
        var client = new FakeChatClient(_ => throw new HttpRequestException("busy", null, HttpStatusCode.ServiceUnavailable));
        var provider = Provider(client);

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public void UseAiForContentSeeder_WithoutModel_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddContentSeeder();
        services.UseAiForContentSeeder(_ => new FakeChatClient(_ => Reply("{}")), _ => { });
        using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<ChatClientProviderOptions>>().Value);
        Assert.Contains("Model is required", ex.Message);
    }

    [Fact]
    public void UseAiForContentSeeder_ReplacesProvider_AndKeysTheClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddContentSeeder();
        var client = new FakeChatClient(_ => Reply("{}"));

        services.UseAiForContentSeeder(_ => client, o => o.Model = "llama3.2:3b");
        using var sp = services.BuildServiceProvider();

        Assert.Equal(typeof(ChatClientContentGenerationProvider),
            services.Single(d => d.ServiceType == typeof(IContentGenerationProvider)).ImplementationType);
        Assert.Same(client, sp.GetRequiredKeyedService<IChatClient>(ChatClientContentGenerationProvider.ClientKey));
        Assert.Null(sp.GetService<IChatClient>());
    }
}
