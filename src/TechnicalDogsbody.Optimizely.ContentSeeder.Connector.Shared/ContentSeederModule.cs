using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using ILogger = EPiServer.Logging.ILogger;

namespace TechnicalDogsbody.Optimizely.ContentSeeder;

/// <summary>
/// Registers the ContentSeeder assembly with EPiServer's module system.
/// This enables the [MenuProvider] scan to find ContentSeederMenuProvider
/// and causes the assembly to appear in the CMS Plug-in Manager.
/// </summary>
[InitializableModule]
[ModuleDependency(typeof(EPiServer.Web.InitializationModule))]
public sealed class ContentSeederModule : IConfigurableModule
{
    private static readonly ILogger _log = LogManager.GetLogger(typeof(ContentSeederModule));

    /// <summary>
    /// Logs that EPiServer has discovered the assembly. The seeder's services are registered by AddContentSeeder(), not here.
    /// </summary>
    public void ConfigureContainer(ServiceConfigurationContext context)
    {
        _log.Information("ContentSeeder: ConfigureContainer called — assembly discovered by EPiServer.");
    }

    /// <summary>
    /// Logs that the module is active. Seeding runs from the seeder's hosted service, not from module initialization.
    /// </summary>
    public void Initialize(InitializationEngine context)
    {
        _log.Information("ContentSeeder: Initialize called — module is active.");
    }

    /// <summary>
    /// Nothing to release when the module shuts down.
    /// </summary>
    public void Uninitialize(InitializationEngine context)
    {
    }
}
