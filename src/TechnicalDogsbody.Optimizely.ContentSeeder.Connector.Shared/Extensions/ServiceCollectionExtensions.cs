using System.Data;
using Bogus;
using EPiServer.Shell.Modules;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechnicalDogsbody.MicroMediator;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Application;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using TechnicalDogsbody.Optimizely.ContentSeeder.UI;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;

/// <summary>
/// Registers all ContentSeeder services into the ASP.NET Core DI container.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the ContentSeeder services. Call this from <c>Program.cs</c>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configure">Optional action to configure <see cref="ContentSeederOptions"/>.</param>
    /// <returns>A <see cref="ContentSeederBuilder"/> for fluent further configuration.</returns>
    public static ContentSeederBuilder AddContentSeeder(
        this IServiceCollection services,
        Action<ContentSeederOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<ContentSeederOptions>(_ => { });

        // IBogusPropertyMap — singleton; the map dictionary is immutable after startup.
        services.Configure<BogusGeneratorMapOptions>(_ => { });
        services.TryAddSingleton<IBogusPropertyMap, DefaultBogusPropertyMap>();

        // BogusContentGenerationProvider is scoped (not singleton) because it holds
        // IImageContentFactory which is also scoped (needs IContentRepository etc.).
        // Also registered as its concrete type so AI providers can use it as their fallback.
        services.TryAddScoped<BogusContentGenerationProvider>();
        services.TryAddScoped<IContentGenerationProvider>(sp => sp.GetRequiredService<BogusContentGenerationProvider>());

        // Image asset factory — scoped, caches ContentReferences within a single seed operation.
        services.TryAddScoped<IImageSource, PicsumImageSource>();
        services.TryAddScoped<SeededMediaWriter>();
        services.TryAddScoped<IVideoProvider, VideoProvider>();
        services.TryAddScoped<IImageContentFactory, OptimizelyImageContentFactory>();
        services.TryAddSingleton<IRichTextConverter, XhtmlRichTextConverter>();
        services.TryAddSingleton<IContentModelAdapter, OptimizelyContentModelAdapter>();

        // HttpClient — required by OptimizelyImageContentFactory to download placeholder images.
        // Named client "ContentSeeder" with a sensible timeout; AddHttpClient is idempotent.
        services.AddHttpClient(ContentSeederNames.HttpClient, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // IDbConnection — scoped, resolved from the EPiServerDB connection string
        services.TryAddScoped<IDbConnection>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            string cs = config.GetConnectionString("EPiServerDB")
                        ?? throw new InvalidOperationException(
                            "ContentSeeder requires a 'EPiServerDB' connection string to be configured.");
            return new SqlConnection(cs);
        });

        // Core services — scoped
        services.TryAddScoped<ISeedOrchestrator, SeedOrchestrator>();
        AddSectionSeeder(services);
        AddConnectorNotifications(services);
        // A connector without sections registers nothing above, so it gets the seeder that does nothing.
        services.TryAddScoped<ISectionSeeder, NoSectionSeeder>();
        // The connector registers its capabilities in AddSectionSeeder; a connector without sections gets none, so this is the default.
        services.TryAddSingleton<IContentCapabilities>(new ContentCapabilities(false));
        services.TryAddScoped<ISeedTracker, ContentSeederRepository>();
        services.TryAddScoped<ContentSeederMigration>();
        // Emails the user who started a run when it completes. Sends nothing unless Smtp:Host is set.
        services.AddScoped<ISeedCompletedHandler, SeedCompletionEmail>();

        services.AddContentSeederMediator();

        // Lets providers see which definition is being seeded (e.g. an IAiSeedDefinition writing brief).
        services.TryAddScoped<ISeedDefinitionAccessor, SeedDefinitionAccessor>();

        // Singleton so every request (and every admin tab) shares one operation lock and state.
        services.TryAddSingleton<UI.SeedOperationMonitor>();

        // Startup migration + seeder
        services.AddHostedService<ContentSeederHostedService>();

        // Register the shell module so EPiServer discovers [MenuProvider] on this assembly.
        // ProtectedModuleOptions.AutoDiscovery defaults to Minimal — modules must be
        // explicitly registered; they are NOT auto-discovered from modules/_protected/.
        services.Configure<ProtectedModuleOptions>(o =>
        {
            if (!o.Items.Any(m => m.Name == ContentSeederNames.ShellModule))
                o.Items.Add(new ModuleDetails { Name = ContentSeederNames.ShellModule });
        });

        // Register this assembly as an MVC application part so ContentSeederController
        // is discovered by the consuming project's MVC pipeline, and register the
        // compiled Razor views assembly part so Views/ContentSeeder/Index.cshtml is found.
        // In .NET 6+, Microsoft.NET.Sdk.Razor compiles views into the main assembly —
        // there is no separate *.Views.dll. CompiledRazorAssemblyPart on thisAssembly
        // is what tells the view engine to look inside it.
        services.AddMvcCore()
            .ConfigureApplicationPartManager(manager =>
            {
                var thisAssembly = typeof(ContentSeederMenuProvider).Assembly;
                if (!manager.ApplicationParts.OfType<AssemblyPart>()
                        .Any(p => p.Assembly == thisAssembly))
                {
                    manager.ApplicationParts.Add(new AssemblyPart(thisAssembly));
                }

                if (!manager.ApplicationParts.OfType<CompiledRazorAssemblyPart>()
                        .Any(p => p.Assembly == thisAssembly))
                {
                    manager.ApplicationParts.Add(new CompiledRazorAssemblyPart(thisAssembly));
                }
            });

        return new ContentSeederBuilder(services);
    }

    /// <summary>
    /// Registers MicroMediator and every ContentSeeder command and query handler. The library does no
    /// assembly scanning, so each handler is registered explicitly. Handlers are singletons: each opens its own
    /// scope per call, so scoped services are never captured by the singleton mediator (see the handlers).
    /// </summary>
    // A connector that supports sections registers its ISectionSeeder here (the Optimizely 13 connector does). A connector without
    // sections implements nothing, and AddContentSeeder registers NoSectionSeeder for it.
    static partial void AddSectionSeeder(IServiceCollection services);

    // A connector with a CMS-specific notification (such as the Optimizely 13 notification bell) registers it here.
    static partial void AddConnectorNotifications(IServiceCollection services);

    internal static void AddContentSeederMediator(this IServiceCollection services)
    {
        services.AddMediator()
            .AddDefaultLoggingPipeline()
            .AddSingletonHandler<SeedCommandHandler>()
            .AddSingletonHandler<ReseedCommandHandler>()
            .AddSingletonHandler<DeleteAllCommandHandler>()
            .AddSingletonHandler<DryRunQueryHandler>()
            .AddSingletonHandler<GetChangedDefinitionsQueryHandler>()
            .AddSingletonHandler<GetDefinitionIdsQueryHandler>();
    }
}

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddContentSeeder"/>.
/// Allows chaining optional configuration such as registering seed definitions and overriding providers.
/// </summary>
public sealed class ContentSeederBuilder(IServiceCollection services)
{
    /// <summary>
    /// Registers a concrete <typeparamref name="TDefinition"/> as a transient <see cref="ISeedDefinition"/>.
    /// </summary>
    public ContentSeederBuilder AddDefinition<TDefinition>()
        where TDefinition : class, ISeedDefinition
    {
        services.AddTransient<ISeedDefinition, TDefinition>();
        return this;
    }

    /// <summary>
    /// Registers a handler that runs after every successful seed or reseed, from the admin UI or at startup.
    /// </summary>
    public ContentSeederBuilder AddSeedCompletedHandler<THandler>()
        where THandler : class, ISeedCompletedHandler
    {
        services.AddScoped<ISeedCompletedHandler, THandler>();
        return this;
    }

    /// <summary>
    /// Replaces the default <see cref="BogusContentGenerationProvider"/> with a custom implementation.
    /// </summary>
    public ContentSeederBuilder UseContentGenerationProvider<TProvider>()
        where TProvider : class, IContentGenerationProvider
    {
        return Replace<IContentGenerationProvider, TProvider>(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// Replaces the default <see cref="OptimizelyImageContentFactory"/> with a custom implementation.
    /// </summary>
    public ContentSeederBuilder UseImageContentFactory<TFactory>()
        where TFactory : class, IImageContentFactory
    {
        return Replace<IImageContentFactory, TFactory>(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// Replaces the default <see cref="DefaultBogusPropertyMap"/> with a custom
    /// <see cref="IBogusPropertyMap"/> implementation.
    /// Use this when you want full control over how property names are resolved.
    /// To add entries without replacing the whole map, prefer <see cref="ExtendBogusPropertyMap"/>.
    /// </summary>
    public ContentSeederBuilder UseBogusPropertyMap<TMap>()
        where TMap : class, IBogusPropertyMap
    {
        return Replace<IBogusPropertyMap, TMap>(ServiceLifetime.Singleton);
    }

    /// <summary>
    /// Adds or overrides entries in the default <see cref="DefaultBogusPropertyMap"/> for every seed definition.
    /// Entries added here take priority over built-in ones with the same key. For a rule that belongs to one
    /// definition, use <see cref="ISeedDefinition.Generators"/> instead.
    /// <example>
    /// <code>
    /// builder.ExtendBogusPropertyMap(map =>
    /// {
    ///     map["JobTitle"]     = f => f.Name.JobTitle();
    ///     map["CompanyName"]  = f => f.Company.CompanyName();
    /// });
    /// </code>
    /// </example>
    /// </summary>
    public ContentSeederBuilder ExtendBogusPropertyMap(
        Action<Dictionary<string, Func<Faker, object>>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure<BogusGeneratorMapOptions>(opts => configure(opts.AdditionalGenerators));
        return this;
    }

    /// <summary>
    /// Removes every registration of <typeparamref name="TService"/> and registers
    /// <typeparamref name="TImplementation"/> in its place with the given lifetime.
    /// </summary>
    private ContentSeederBuilder Replace<TService, TImplementation>(ServiceLifetime lifetime)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.Add(ServiceDescriptor.Describe(typeof(TService), typeof(TImplementation), lifetime));
        return this;
    }
}
