using Anthropic;
using Anthropic.Bedrock;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using EPiServer.Cms.UI.AspNetIdentity;
using EPiServer.Data;
using EPiServer.DependencyInjection;
using EPiServer.Web.Routing;
using TechnicalDogsbody.Optimizely.ContentSeeder.AI;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.Seeding;
using TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12;

public class Startup
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public Startup(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // CMS 12 does not create its database schema on its own (CMS 13 does), so a new demo database would fail on startup
        // with missing stored procedures. This creates the schema on first run, and updates it when the package version changes.
        services.Configure<DataAccessOptions>(options =>
        {
            options.CreateDatabaseSchema = true;
            options.UpdateDatabaseSchema = true;
        });

        services.AddCmsAspNetIdentity<ApplicationUser>()
                .AddCms()
                .AddCmsTagHelpers();

        services.ConfigureApplicationCookie(options =>
            options.LoginPath = "/util/Login");

        // Creates the first admin user if none exists. Its password comes from Demo:Admin:Password (user secrets),
        // or is generated and logged once.
        services.AddHostedService<AdminUserInitializer>();

        // Makes sure the demo has a site, with a home page of its own beside the seeded home pages. Seeding never changes it.
        services.AddScoped<DemoSiteInitializer>();
        services.AddHostedService<DemoSiteStartup>();

        services.AddContentSeeder()
                .AddDefinition<Demo12SeedDefinition>();

        services.Configure<ContentSeederOptions>(
            _configuration.GetSection("ContentSeeder"));

        // Optional AI writing. ContentSeeder:AiProvider picks one of the vendors in CreateChatClient; blank keeps the
        // default Bogus provider, so the demo runs with no keys or models. The provider accepts any IChatClient, so
        // switching vendors is only a different client here. The writing brief lives on DemoSeedDefinition.WritingBrief.
        var seeder = _configuration.GetSection("ContentSeeder");
        string? aiProvider = seeder["AiProvider"]?.Trim();
        if (!string.IsNullOrWhiteSpace(aiProvider))
        {
            var ai = seeder.GetSection(aiProvider);
            services.UseAiForContentSeeder(_ => CreateChatClient(aiProvider, ai), o =>
            {
                ai.Bind(o); // Model, Effort, MaxOutputTokens, UseJsonSchema…
                o.DisplayName = aiProvider;
            });
        }
    }

    // Keep keys out of source control: dotnet user-secrets set "ContentSeeder:Anthropic:ApiKey" "<key>"
    private static IChatClient CreateChatClient(string provider, IConfigurationSection settings)
    {
        string model = settings["Model"] ?? string.Empty;
        string? apiKey = settings["ApiKey"];

        switch (provider.ToLowerInvariant())
        {
            case "ollama":
                // Free and local: any model you've pulled, e.g. `ollama pull llama3.2:3b`.
                return new OllamaApiClient(new Uri(settings["Endpoint"] is { Length: > 0 } endpoint ? endpoint : "http://localhost:11434"));

            case "anthropic":
                // Blank ApiKey: the SDK uses ANTHROPIC_API_KEY or an `ant auth login` profile.
                return (string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey }).AsIChatClient();

            case "bedrock":
                // Claude on Amazon Bedrock. Credentials come from the standard AWS chain, or AwsProfile.
                return new AnthropicBedrockMantleClient(new MantleAwsClientOptions
                {
                    AwsRegion = settings["AwsRegion"],
                    AwsProfile = string.IsNullOrWhiteSpace(settings["AwsProfile"]) ? null : settings["AwsProfile"],
                }).AsIChatClient();

            case "openai":
                return new OpenAIClient(apiKey ?? throw new InvalidOperationException("ContentSeeder:OpenAI:ApiKey is required.")).GetChatClient(model).AsIChatClient();

            default:
                throw new InvalidOperationException(
                    $"Unknown ContentSeeder:AiProvider '{provider}'. Use Ollama, Anthropic, Bedrock or OpenAI, or leave it blank.");
        }
    }


    public void Configure(IApplicationBuilder app)
    {
        if (_environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapContent();
            endpoints.MapControllers();
            endpoints.MapRazorPages();
        });
    }
}
