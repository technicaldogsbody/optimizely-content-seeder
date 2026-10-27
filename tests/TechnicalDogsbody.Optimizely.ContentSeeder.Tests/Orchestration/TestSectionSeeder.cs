using EPiServer.DataAbstraction;
using EPiServer.VisualBuilder;
using EPiServer.VisualBuilder.Compositions;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using EPiServer;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

/// <summary>
/// Builds the Optimizely 13 section seeder over test doubles, for tests that exercise Visual Builder sections.
/// </summary>
internal static class TestSectionSeeder
{
    public static SectionSeeder Create(
        IContentRepository contentRepository,
        IContentLoader contentLoader,
        IContentTypeRepository contentTypeRepository,
        IContentGenerationProvider contentGenerationProvider,
        IBlueprintRepository blueprintRepository,
        IBlueprintContentFactory blueprintContentFactory,
        ICompositionMapper compositionMapper,
        ISeedTracker seedTracker,
        ContentSeederOptions options,
        IVideoProvider videoProvider,
        ILogger logger)
        => new(
            contentRepository, contentLoader, contentTypeRepository, contentGenerationProvider,
            blueprintRepository, blueprintContentFactory, compositionMapper, seedTracker,
            new VideoApplier(videoProvider, logger), new GenerationRunner(contentGenerationProvider, options), logger);
}
