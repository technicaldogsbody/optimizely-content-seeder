using System.Globalization;
using System.Net;
using System.Text;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.UI;

/// <summary>
/// Builds the completion email: an HTML body styled to match the admin page, with a plain-text version for mail clients that
/// do not show HTML. Pure, so the report can be tested without sending mail.
/// </summary>
internal static class SeedCompletionEmailBuilder
{
    // The admin page's palette (see UI/client/src/styles.css), inlined because mail clients ignore stylesheets.
    private const string Navy = "#080736";
    private const string Blue = "#0037ff";
    private const string BlueTint = "#e6ebff";
    private const string BlueLine = "#b3c3ff";
    private const string Text = "#262626";
    private const string Muted = "#707070";
    private const string Line = "#d6d6d6";
    private const string Canvas = "#f5f5f5";
    private const string Font = "Inter,'Segoe UI',system-ui,-apple-system,sans-serif";
    private const string Mono = "Hack,'JetBrains Mono',Consolas,monospace";

    /// <summary>The subject, HTML body and plain-text body of the email for a completed run.</summary>
    public readonly record struct Email(string Subject, string Html, string Text);

    /// <summary>
    /// Builds the email. <paramref name="status"/> holds the totals now in the CMS, and <paramref name="definitions"/> the same
    /// per definition, so the report says what each definition has seeded.
    /// </summary>
    public static Email Build(
        string operation,
        DateTime completedUtc,
        ContentSeederStatus status,
        IReadOnlyList<SeedDefinitionStatus> definitions,
        string pageUrl)
    {
        string verb = operation.Equals("Reseed", StringComparison.OrdinalIgnoreCase) ? "reseeded" : "seeded";
        string when = completedUtc.ToString("d MMM yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);
        string subject = $"Content Seeder: {verb} at {completedUtc.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC";

        var totals = new List<(string Label, int Count)>
        {
            ("Pages", status.TrackedPageCount),
            ("Blocks", status.TrackedBlockCount),
        };
        if (status.SupportsSections)
            totals.Add(("Sections", status.TrackedSectionCount));

        return new Email(
            subject,
            BuildHtml(verb, when, totals, status.SupportsSections, definitions, pageUrl),
            BuildText(verb, when, totals, status.SupportsSections, definitions, pageUrl));
    }

    private static string BuildHtml(
        string verb,
        string when,
        List<(string Label, int Count)> totals,
        bool supportsSections,
        IReadOnlyList<SeedDefinitionStatus> definitions,
        string pageUrl)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"></head>");
        sb.Append($"<body style=\"margin:0;padding:0;background:{Canvas};font-family:{Font};color:{Text};\">");
        sb.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Canvas};\"><tr>");
        sb.Append("<td align=\"center\" style=\"padding:24px 12px;\">");
        sb.Append($"<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#ffffff;border:1px solid {Line};\">");

        // Header, the same navy bar as the admin page's title.
        sb.Append($"<tr><td style=\"background:{Navy};padding:20px 24px;\">");
        sb.Append($"<div style=\"font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:{BlueLine};\">Add-on</div>");
        sb.Append("<div style=\"font-size:22px;font-weight:600;color:#ffffff;margin-top:4px;\">Content Seeder</div>");
        sb.Append("</td></tr>");

        // Summary: what finished and when.
        sb.Append("<tr><td style=\"padding:24px;\">");
        sb.Append("<p style=\"margin:0 0 8px;font-size:16px;\">The content seed you started has finished.</p>");
        sb.Append($"<p style=\"margin:0 0 20px;font-size:14px;color:{Muted};\">{Enc(Capitalise(verb))} {Enc(when)}.</p>");

        // Totals as tiles.
        int width = 100 / Math.Max(totals.Count, 1);
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"8\"><tr>");
        foreach (var (label, count) in totals)
        {
            sb.Append($"<td width=\"{width}%\" align=\"center\" style=\"background:{BlueTint};border:1px solid {BlueLine};padding:12px 8px;\">");
            sb.Append($"<div style=\"font-size:24px;font-weight:600;color:{Blue};\">{count.ToString("N0", CultureInfo.InvariantCulture)}</div>");
            sb.Append($"<div style=\"font-size:12px;color:{Muted};text-transform:uppercase;letter-spacing:.05em;\">{Enc(label)}</div></td>");
        }
        sb.Append("</tr></table>");

        // Per-definition report.
        sb.Append("<p style=\"margin:24px 0 8px;font-size:14px;font-weight:600;\">By seed definition</p>");
        if (definitions.Count == 0)
        {
            sb.Append($"<p style=\"margin:0;font-size:14px;color:{Muted};\">No seed definitions are registered.</p>");
        }
        else
        {
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"8\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px;\">");
            sb.Append($"<tr style=\"background:{Canvas};color:{Muted};text-align:left;\">");
            sb.Append($"<th style=\"border-bottom:1px solid {Line};\">Definition</th>");
            sb.Append($"<th style=\"border-bottom:1px solid {Line};text-align:right;\">Pages</th>");
            sb.Append($"<th style=\"border-bottom:1px solid {Line};text-align:right;\">Blocks</th>");
            if (supportsSections)
                sb.Append($"<th style=\"border-bottom:1px solid {Line};text-align:right;\">Sections</th>");
            sb.Append($"<th style=\"border-bottom:1px solid {Line};\">Last seeded</th></tr>");

            foreach (var d in definitions)
            {
                sb.Append($"<tr><td style=\"border-bottom:1px solid {Line};font-family:{Mono};\">{Enc(d.SeedDefinitionId)}</td>");
                sb.Append($"<td style=\"border-bottom:1px solid {Line};text-align:right;\">{Num(d.PageCount)}</td>");
                sb.Append($"<td style=\"border-bottom:1px solid {Line};text-align:right;\">{Num(d.BlockCount)}</td>");
                if (supportsSections)
                    sb.Append($"<td style=\"border-bottom:1px solid {Line};text-align:right;\">{Num(d.SectionCount)}</td>");
                sb.Append($"<td style=\"border-bottom:1px solid {Line};color:{Muted};\">{Enc(LastSeen(d))}</td></tr>");
            }
            sb.Append("</table>");
        }

        // Call to action.
        if (!string.IsNullOrWhiteSpace(pageUrl))
        {
            sb.Append($"<p style=\"margin:28px 0 0;\"><a href=\"{Enc(pageUrl)}\" style=\"display:inline-block;background:{Blue};color:#ffffff;" +
                      "text-decoration:none;padding:10px 18px;font-size:14px;font-weight:600;\">Open Content Seeder</a></p>");
        }
        sb.Append("</td></tr>");

        // Footer.
        sb.Append($"<tr><td style=\"background:{Canvas};border-top:1px solid {Line};padding:14px 24px;font-size:12px;color:{Muted};\">");
        sb.Append("Sent by the Content Seeder because you started this seed. Totals are the content tracked now, not only what this run created.");
        sb.Append($"<br><br>Made by <a href=\"https://www.technicaldogsbody.com\" style=\"color:{Blue};text-decoration:none;\">TechnicalDogsbody</a>.");
        sb.Append("</td></tr>");

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static string BuildText(
        string verb,
        string when,
        List<(string Label, int Count)> totals,
        bool supportsSections,
        IReadOnlyList<SeedDefinitionStatus> definitions,
        string pageUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Content Seeder");
        sb.AppendLine();
        sb.AppendLine($"The content seed you started has finished. {Capitalise(verb)} {when}.");
        sb.AppendLine();
        foreach (var (label, count) in totals)
            sb.AppendLine($"{label}: {count.ToString("N0", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("By seed definition:");
        foreach (var d in definitions)
        {
            string sections = supportsSections ? $", {Num(d.SectionCount)} sections" : string.Empty;
            sb.AppendLine($"  {d.SeedDefinitionId}: {Num(d.PageCount)} pages, {Num(d.BlockCount)} blocks{sections} (last seeded {LastSeen(d)})");
        }
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(pageUrl))
        {
            sb.AppendLine($"Open Content Seeder: {pageUrl}");
            sb.AppendLine();
        }
        sb.AppendLine("Totals are the content tracked now, not only what this run created.");
        sb.AppendLine();
        sb.AppendLine("Made by TechnicalDogsbody: https://www.technicaldogsbody.com");
        return sb.ToString();
    }

    private static string LastSeen(SeedDefinitionStatus d) =>
        d.LastSeededUtc?.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) ?? "never";

    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Capitalise(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
