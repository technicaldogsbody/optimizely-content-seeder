using Microsoft.Extensions.DependencyInjection;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Connector12.Tests;

/// <summary>
/// The CMS 12 connector's registration. A CMS 12 site has no sections, so the sections code must not be registered, and the
/// seeder must not depend on it. These tests build the registration without a CMS and read it back.
/// </summary>
public class Connector12Registration
{
    private static IServiceCollection Register()
    {
        var services = new ServiceCollection();
        services.AddContentSeeder();
        return services;
    }

    [Fact]
    public void SectionSeeder_IsTheNoSectionsOne()
    {
        var services = Register();

        var section = Assert.Single(services, d => d.ServiceType == typeof(ISectionSeeder));

        Assert.Equal(typeof(NoSectionSeeder), section.ImplementationType);
    }

    [Fact]
    public void Capabilities_SayTheCmsHasNoSections()
    {
        var services = Register();

        var capabilities = Assert.Single(services, d => d.ServiceType == typeof(IContentCapabilities));

        Assert.NotNull(capabilities.ImplementationInstance);
        Assert.False(((IContentCapabilities)capabilities.ImplementationInstance!).SupportsSections);
    }

    [Fact]
    public void SeedFactory_HasNoExperienceEntryPoint()
    {
        // Experience pages and their sections exist only in the CMS 13 connector.
        Assert.Null(typeof(SeedFactory).GetMethod("Experience"));
    }

    [Fact]
    public void SeedFactory_BuildsPagesAndTreesThatValidate()
    {
        var node = SeedFactory.Page(typeof(string));
        var tree = SeedFactory.Tree(blocks: [], nodes: [node]);

        Assert.IsType<SeedNode>(node);
        Assert.IsType<SeedTree>(tree);
        tree.Validate();
    }
}
