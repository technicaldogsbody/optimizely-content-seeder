using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.UI;

/// <summary>
/// Emails the user who started a seed or reseed when it completes: an HTML report styled like the admin page, with a plain-text
/// part. It sends nothing when the run was started without a signed-in user (a startup run), when that user has no email
/// address, or when <see cref="SmtpOptions.Host"/> is blank. A mail failure is logged and never fails the seed.
/// <para>
/// Uses <see cref="SmtpClient"/> from the framework rather than a package, so the CMS 12 and CMS 13 connectors do not
/// need different versions of a mail library.
/// </para>
/// </summary>
internal sealed class SeedCompletionEmail(
    SeedOperationMonitor monitor,
    ISeedTracker tracker,
    IContentCapabilities capabilities,
    IOptions<ContentSeederOptions> options,
    ILogger<SeedCompletionEmail> logger) : ISeedCompletedHandler
{
    /// <inheritdoc/>
    public async Task HandleAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        string? to = monitor.RequesterEmail;
        if (string.IsNullOrWhiteSpace(settings.Smtp.Host))
        {
            logger.LogInformation("ContentSeeder: completion email not sent. ContentSeeder:Smtp:Host is blank.");
            return;
        }
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogInformation("ContentSeeder: completion email not sent. The run was not started by a user with an email address.");
            return;
        }

        try
        {
            var status = await tracker.GetStatusAsync(ct).ConfigureAwait(false);
            var definitions = await tracker.GetDefinitionStatusesAsync(ct).ConfigureAwait(false);
            status = status with { SupportsSections = capabilities.SupportsSections };

            var email = SeedCompletionEmailBuilder.Build(
                monitor.Current.Name ?? "Seed",
                DateTime.UtcNow,
                status,
                definitions,
                settings.AdminPageUrl);

            using var message = new MailMessage
            {
                From = new MailAddress(settings.Smtp.FromAddress, settings.Smtp.FromName),
                Subject = email.Subject,
            };
            message.To.Add(to);
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.Text, null, MediaTypeNames.Text.Plain));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.Html, null, MediaTypeNames.Text.Html));

            using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
            {
                EnableSsl = settings.Smtp.UseStartTls,
                Credentials = string.IsNullOrWhiteSpace(settings.Smtp.UserName)
                    ? null
                    : new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password),
            };
            await client.SendMailAsync(message, ct).ConfigureAwait(false);
            logger.LogInformation("ContentSeeder: completion email sent to {Recipient} via {Host}.", to, settings.Smtp.Host);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ContentSeeder: the completion email to {Recipient} could not be sent. The seed itself succeeded.", to);
        }
    }
}
