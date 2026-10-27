using Microsoft.Extensions.DependencyInjection;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Notifications;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;

// The Optimizely 13 part of the registration: the notification bell.
public static partial class ServiceCollectionExtensions
{
    static partial void AddConnectorNotifications(IServiceCollection services)
    {
        services.AddScoped<ISeedCompletedHandler, SeedCompletionNotification>();
    }
}
