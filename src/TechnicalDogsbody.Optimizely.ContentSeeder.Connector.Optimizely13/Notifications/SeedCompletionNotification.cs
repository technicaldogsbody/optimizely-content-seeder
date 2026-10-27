using EPiServer.Notification;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.UI;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Notifications;

/// <summary>
/// Posts a notification to the user who started a seed or reseed, so it appears in the CMS notification bell when it
/// completes. It posts nothing when the run was started without a signed-in user. A failure is logged and never fails the seed.
/// </summary>
internal sealed class SeedCompletionNotification(
    SeedOperationMonitor monitor,
    INotifier notifier,
    INotificationChannelOptionsRegistry channels,
    ILogger<SeedCompletionNotification> logger) : ISeedCompletedHandler
{
    private const string ChannelName = "ContentSeeder";

    /// <inheritdoc/>
    public async Task HandleAsync(CancellationToken ct = default)
    {
        string? user = monitor.RequesterName;
        if (string.IsNullOrWhiteSpace(user))
            return;

        try
        {
            // A notification needs a registered channel. Register it the first time it is needed, and keep it.
            if (channels.Get(ChannelName) is null)
                channels.Add(ChannelName, new NotificationChannelOptions(immediate: true));

            bool seeded = !string.Equals(monitor.Current.Name, "Reseed", StringComparison.OrdinalIgnoreCase);
            string verb = seeded ? "seed" : "reseed";

            var message = new NotificationMessage
            {
                Sender = new NotificationUser("content-seeder") { DisplayName = "Content Seeder" },
                Recipients = [new NotificationUser(user)],
                ChannelName = ChannelName,
                Category = new Uri("https://www.technicaldogsbody.com/content-seeder"),
                TypeName = "ContentSeeder.SeedCompleted",
                Subject = $"Content Seeder: {verb} complete",
                Content = $"The content {verb} you started has finished. Open Content Seeder to see what was created.",
            };

            await notifier.PostNotificationAsync(message).ConfigureAwait(false);
            logger.LogInformation("ContentSeeder: completion notification posted for {User}.", user);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ContentSeeder: the completion notification for {User} could not be posted. The seed itself succeeded.", user);
        }
    }
}
