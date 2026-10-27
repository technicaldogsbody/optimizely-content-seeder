using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Security;
using EPiServer.VisualBuilder;
using EPiServer.VisualBuilder.Compositions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;

// The Optimizely 13 part of the registration: the section seeder for Visual Builder sections.
public static partial class ServiceCollectionExtensions
{
    static partial void AddSectionSeeder(IServiceCollection services)
    {
        // Optimizely CMS 13 has sections, so the capability says so. Registered before the default, which then does nothing.
        services.TryAddSingleton<IContentCapabilities>(new ContentCapabilities(true));

        services.TryAddScoped<ISectionSeeder>(sp =>
        {
            var contentGeneration = sp.GetRequiredService<IContentGenerationProvider>();
            var options = sp.GetRequiredService<IOptions<ContentSeederOptions>>();
            var logger = sp.GetRequiredService<ILogger<SectionSeeder>>();

            return new SectionSeeder(
                sp.GetRequiredService<IContentRepository>(),
                sp.GetRequiredService<IContentLoader>(),
                sp.GetRequiredService<IContentTypeRepository>(),
                contentGeneration,
                sp.GetRequiredService<IBlueprintRepository>(),
                sp.GetRequiredService<IBlueprintContentFactory>(),
                sp.GetRequiredService<ICompositionMapper>(),
                sp.GetRequiredService<ISeedTracker>(),
                new VideoApplier(sp.GetRequiredService<IVideoProvider>(), logger),
                new GenerationRunner(contentGeneration, options.Value),
                logger);
        });
    }
}
