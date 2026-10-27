using EPiServer.Cms.UI.AspNetIdentity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo;

/// <summary>
/// Creates the first admin user on startup, when no users exist yet, so the CMS can be logged into.
/// <para>
/// The email comes from <c>Demo:Admin:Email</c>. The password comes from <c>Demo:Admin:Password</c>; set it with
/// <c>dotnet user-secrets</c>. If no password is configured, a random one is generated and logged once, when the
/// user is created.
/// </para>
/// </summary>
public sealed class AdminUserInitializer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdminUserInitializer> logger) : IHostedService
{
    private const string DefaultEmail = "admin@example.com";

    // Every class the default password policy asks for, so a generated password always passes it.
    private const string PasswordAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    // Standard EPiServer CMS roles required for full admin access.
    private static readonly string[] _requiredRoles = ["CmsAdmin", "WebEditors", "WebAdmins"];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // CMS 13's AddCmsAspNetIdentity() no longer seeds these roles automatically — create
        // any missing one before assigning the admin user to it.
        foreach (string roleName in _requiredRoles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
                await roleManager.CreateAsync(new IdentityRole(roleName));
        }

        if (userManager.Users.Any())
        {
            logger.LogDebug("Admin users already exist — skipping default admin creation.");
            return;
        }

        string email = configuration["Demo:Admin:Email"] is { Length: > 0 } configuredEmail ? configuredEmail : DefaultEmail;
        string? configuredPassword = configuration["Demo:Admin:Password"];
        bool generated = string.IsNullOrWhiteSpace(configuredPassword);
        string password = generated ? GeneratePassword() : configuredPassword!;

        var user = new ApplicationUser
        {
            UserName       = email,
            Email          = email,
            EmailConfirmed = true,
            IsApproved     = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Failed to create the first admin user: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRolesAsync(user, _requiredRoles);

        if (generated)
        {
            logger.LogWarning(
                "Created the first admin user. Log in at /util/Login with  Email: {Email}  Password: {Password}  " +
                "Set Demo:Admin:Password to choose your own.",
                email, password);
        }
        else
        {
            logger.LogInformation("Created the first admin user {Email} with the password from Demo:Admin:Password.", email);
        }
    }

    // A random password with a lowercase letter, an uppercase letter, a digit and a symbol, as the default policy requires.
    private static string GeneratePassword()
        => RandomNumberGenerator.GetString(PasswordAlphabet, 20) + "Aa1!";

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
