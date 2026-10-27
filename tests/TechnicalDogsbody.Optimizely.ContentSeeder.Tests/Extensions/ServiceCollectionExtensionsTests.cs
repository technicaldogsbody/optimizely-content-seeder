using Microsoft.Extensions.DependencyInjection;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    // ── registration verification ─────────────────────────────────────────────

    [Fact]
    public void AddContentSeeder_RegistersIContentGenerationProvider()
    {
        var services = new ServiceCollection();
        services.AddContentSeeder();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IContentGenerationProvider));

        // Scoped, not singleton: it depends on IImageContentFactory, which wraps scoped CMS services.
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void AddContentSeeder_RegistersBogusProviderAsConcreteTypeForAiFallback()
    {
        var services = new ServiceCollection();
        services.AddContentSeeder();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(global::TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus.BogusContentGenerationProvider));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void AddContentSeeder_RegistersISeedOrchestratorAsScoped()
    {
        var services = new ServiceCollection();
        services.AddContentSeeder();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISeedOrchestrator));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void AddContentSeeder_RegistersISeedTrackerAsScoped()
    {
        var services = new ServiceCollection();
        services.AddContentSeeder();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISeedTracker));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void AddContentSeeder_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        services.AddContentSeeder(opts => opts.SeedOnStartup = true);

        // Services with options registered — options service should be present
        var optionsDescriptor = services.FirstOrDefault(
            d => d.ServiceType.Name.Contains("IOptions") || d.ServiceType.Name.Contains("IConfigureOptions"));
        Assert.NotNull(optionsDescriptor);
    }

    // ── ISeedDefinition registration ─────────────────────────────────────────

    [Fact]
    public void AddDefinition_RegistersDefinitionAsTransient()
    {
        var services = new ServiceCollection();
        services.AddContentSeeder()
                .AddDefinition<TestSeedDefinition>();

        var descriptors = services.Where(d => d.ServiceType == typeof(ISeedDefinition)).ToList();

        Assert.Single(descriptors);
        Assert.Equal(ServiceLifetime.Transient, descriptors[0].Lifetime);
    }

    [Fact]
    public void AddDefinition_MultipleDefinitions_AllRegistered()
    {
        var services = new ServiceCollection();
        services.AddContentSeeder()
                .AddDefinition<TestSeedDefinition>()
                .AddDefinition<TestSeedDefinition2>();

        int count = services.Count(d => d.ServiceType == typeof(ISeedDefinition));

        Assert.Equal(2, count);
    }

    // ── test helpers ─────────────────────────────────────────────────────────

    private sealed class TestSeedDefinition : ISeedDefinition
    {
        public string Id => "test-1";
        public SeedTree Build() => new();
    }

    private sealed class TestSeedDefinition2 : ISeedDefinition
    {
        public string Id => "test-2";
        public SeedTree Build() => new();
    }
}
