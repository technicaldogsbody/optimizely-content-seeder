using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Anthropic;
using EPiServer.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenAI;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.AI;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

/// <summary>
/// Runs the generic IChatClient provider through the real Anthropic and OpenAI SDK adapters, with the
/// network replaced by a fake handler. Verifies what each vendor would actually be sent, and how its
/// errors are treated — so the vendors don't need dedicated add-on packages.
/// </summary>
public class ChatClientVendorAdapterTests
{
    public class FakeHero : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual string? CtaUrl { get; set; }
    }

    private sealed class FakeHandler(Func<int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<JsonDocument> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(JsonDocument.Parse(request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(ct)));
            return reply(Requests.Count);
        }
    }

    private sealed class Accessor : ISeedDefinitionAccessor
    {
        public ISeedDefinition? Current { get; set; }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static IEnumerable<PropertyInfo> Props => typeof(FakeHero).GetProperties()
        .Where(p => p.Name is nameof(FakeHero.Heading) or nameof(FakeHero.CtaUrl));

    private static ChatClientContentGenerationProvider Provider(IChatClient client, Action<ChatClientProviderOptions> configure)
    {
        var options = new ChatClientProviderOptions { Model = "test-model", DisplayName = "Test" };
        configure(options);
        var bogus = new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions()),
            NullLogger<BogusContentGenerationProvider>.Instance, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
        return new ChatClientContentGenerationProvider(bogus, new Accessor(), new XhtmlRichTextConverter(), client, OptionsFactory.Create(options),
            NullLogger<ChatClientContentGenerationProvider>.Instance, new OptimizelyContentModelAdapter());
    }

    // ── Claude (Anthropic SDK adapter) ───────────────────────────────────────

    private const string ClaudeOk = """{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"{\"Heading\":\"Pack light, walk far\"}"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";

    private static (ChatClientContentGenerationProvider Provider, FakeHandler Handler) Claude(
        Func<int, HttpResponseMessage> reply, Action<ChatClientProviderOptions>? configure = null)
    {
        var handler = new FakeHandler(reply);
        var client = new AnthropicClient { ApiKey = "sk-ant-test", BaseUrl = "http://localhost", HttpClient = new HttpClient(handler), MaxRetries = 0 };
        return (Provider(client.AsIChatClient(), o => { o.Model = "claude-opus-5-5"; configure?.Invoke(o); }), handler);
    }

    [Fact]
    public async Task Claude_SendsModelNativeJsonSchemaAndEffort_AndAppliesTheReply()
    {
        var (provider, handler) = Claude(_ => Json(HttpStatusCode.OK, ClaudeOk), o => o.Effort = ReasoningEffort.Low);
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("Pack light, walk far", hero.Heading);
        var body = Assert.Single(handler.Requests).RootElement;
        Assert.Equal("claude-opus-5-5", body.GetProperty("model").GetString());
        var output = body.GetProperty("output_config");
        Assert.Equal("json_schema", output.GetProperty("format").GetProperty("type").GetString());
        Assert.True(output.GetProperty("format").GetProperty("schema").GetProperty("properties").TryGetProperty("Heading", out _));
        Assert.Equal("low", output.GetProperty("effort").GetString());
        Assert.Equal("adaptive", body.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Claude_WithoutEffort_SendsNoThinkingOrEffortSettings()
    {
        var (provider, handler) = Claude(_ => Json(HttpStatusCode.OK, ClaudeOk));

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        var body = handler.Requests.Single().RootElement;
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.False(body.GetProperty("output_config").TryGetProperty("effort", out _));
    }

    [Fact]
    public async Task Claude_Refusal_KeepsBogusValue_AndKeepsTryingOtherItems()
    {
        var (provider, handler) = Claude(_ => Json(HttpStatusCode.OK,
            """{"id":"msg_1","type":"message","role":"assistant","model":"m","content":[],"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber"},"usage":{"input_tokens":1,"output_tokens":0}}"""));
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.False(string.IsNullOrEmpty(hero.Heading));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""")]
    [InlineData(HttpStatusCode.PaymentRequired, """{"type":"error","error":{"type":"billing_error","message":"Your credit balance is too low"}}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"type":"error","error":{"type":"permission_error","message":"nope"}}""")]
    [InlineData(HttpStatusCode.NotFound, """{"type":"error","error":{"type":"not_found_error","message":"model: nope"}}""")]
    public async Task Claude_FatalError_StopsAskingForTheRestOfTheRun(HttpStatusCode status, string body)
    {
        var (provider, handler) = Claude(_ => Json(status, body));

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{"type":"error","error":{"type":"rate_limit_error","message":"slow down"}}""")]
    [InlineData((HttpStatusCode)529, """{"type":"error","error":{"type":"overloaded_error","message":"overloaded"}}""")]
    public async Task Claude_TransientError_OnlySkipsThatItem(HttpStatusCode status, string body)
    {
        var (provider, handler) = Claude(_ => Json(status, body));

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.Equal(2, handler.Requests.Count);
    }

    // ── OpenAI (Microsoft.Extensions.AI.OpenAI adapter) ──────────────────────

    private const string OpenAiOk = """{"id":"c1","object":"chat.completion","created":1,"model":"gpt-test","choices":[{"index":0,"message":{"role":"assistant","content":"{\"Heading\":\"Pack light, walk far\"}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";

    private static (ChatClientContentGenerationProvider Provider, FakeHandler Handler) OpenAi(
        Func<int, HttpResponseMessage> reply, Action<ChatClientProviderOptions>? configure = null)
    {
        var handler = new FakeHandler(reply);
        var client = new OpenAIClient(new ApiKeyCredential("sk-test"), new OpenAIClientOptions
        {
            Endpoint = new Uri("http://localhost/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            RetryPolicy = new ClientRetryPolicy(0),
        });
        return (Provider(client.GetChatClient("gpt-test").AsIChatClient(), o => { o.Model = "gpt-test"; configure?.Invoke(o); }), handler);
    }

    [Fact]
    public async Task OpenAi_SendsStrictJsonSchema_AndAppliesTheReply()
    {
        var (provider, handler) = OpenAi(_ => Json(HttpStatusCode.OK, OpenAiOk));
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));

        Assert.Equal("Pack light, walk far", hero.Heading);
        var body = Assert.Single(handler.Requests).RootElement;
        Assert.Equal("gpt-test", body.GetProperty("model").GetString());
        var format = body.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.True(format.GetProperty("json_schema").GetProperty("schema").GetProperty("properties").TryGetProperty("Heading", out _));
    }

    [Fact]
    public async Task OpenAi_StrictJsonSchemaOff_DoesNotSendStrict()
    {
        var (provider, handler) = OpenAi(_ => Json(HttpStatusCode.OK, OpenAiOk), o => o.StrictJsonSchema = false);

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        var schema = handler.Requests.Single().RootElement.GetProperty("response_format").GetProperty("json_schema");
        Assert.False(schema.TryGetProperty("strict", out _));
    }

    [Fact]
    public async Task OpenAi_Refusal_KeepsBogusValue_AndKeepsTryingOtherItems()
    {
        var (provider, handler) = OpenAi(_ => Json(HttpStatusCode.OK,
            """{"id":"c1","object":"chat.completion","created":1,"model":"gpt-test","choices":[{"index":0,"message":{"role":"assistant","content":null,"refusal":"I can't help with that"},"finish_reason":"stop"}]}"""));
        var hero = new FakeHero();

        await provider.PopulateAsync(hero, Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.False(string.IsNullOrEmpty(hero.Heading));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key","type":"invalid_request_error","code":"invalid_api_key"}}""")]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"message":"The model does not exist","type":"invalid_request_error","code":"model_not_found"}}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"message":"You exceeded your current quota","type":"insufficient_quota","code":"insufficient_quota"}}""")]
    public async Task OpenAi_FatalError_StopsAskingForTheRestOfTheRun(HttpStatusCode status, string body)
    {
        var (provider, handler) = OpenAi(_ => Json(status, body));

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"message":"Rate limit reached","type":"requests","code":"rate_limit_exceeded"}}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"overloaded","type":"server_error"}}""")]
    public async Task OpenAi_TransientError_OnlySkipsThatItem(HttpStatusCode status, string body)
    {
        var (provider, handler) = OpenAi(_ => Json(status, body));

        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));
        await provider.PopulateAsync(new FakeHero(), Props, "en", typeof(FakeHero));

        Assert.Equal(2, handler.Requests.Count);
    }
}
