using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Computes a stable hash of a seed definition's shape, used to tell when a definition has changed
/// since its content was seeded (Seed only fills gaps, so a changed shape needs a Reseed).
/// <para>
/// Covers everything that determines what gets created: types, counts, IDs, assignments, image sizes,
/// rows/columns/elements, repeating ranges, styles and languages. <c>configure</c> callbacks are not
/// covered — delegates can't be compared — so changing only a callback is not detected.
/// </para>
/// </summary>
public static partial class SeedTreeFingerprint
{
    /// <summary>
    /// A stable hash of a tree's shape, used to tell whether a definition has changed since it was seeded. The languages are part of the hash, so changing them also counts as a change.
    /// </summary>
    public static string Compute(SeedTree tree, string? masterLanguage, IReadOnlyList<string>? languages)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var sb = new StringBuilder();
        sb.Append("v1\n");
        sb.Append("L|").Append(masterLanguage).Append('|').AppendJoin(',', languages ?? []).Append('\n');

        foreach (var block in tree.Blocks)
            sb.Append("B|").Append(block.Id).Append('|').Append(TypeName(block.Type)).Append('|').Append(Images(block.ImageSizes)).Append(Videos(block.Videos)).Append(Instance(block.Instance)).Append('\n');

        AppendSections(sb, tree);

        foreach (var node in tree.Nodes)
            AppendNode(sb, node, depth: 0);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Writes a connector's sections, with their rows, columns and elements, into the hash. Nothing when the CMS has none.
    static partial void AppendSections(StringBuilder sb, SeedTree tree);

    // Writes the sections a page places into the hash. Nothing when the CMS has none.
    static partial void AppendSlots(StringBuilder sb, SeedNode node, string indent);

    private static void AppendNode(StringBuilder sb, SeedNode node, int depth)
    {
        string indent = new(' ', depth);
        sb.Append(indent).Append("N|").Append(TypeName(node.Type)).Append('|')
          .Append(node.Count.ToString(CultureInfo.InvariantCulture)).Append('|').Append(Images(node.ImageSizes)).Append(Videos(node.Videos)).Append(Instance(node.Instance)).Append('\n');

        foreach (var assignment in node.ContentAreaAssignments)
            sb.Append(indent).Append(" A|").Append(assignment.PropertyName).Append('|').AppendJoin(',', assignment.BlockIds).Append('\n');

        AppendSlots(sb, node, indent);

        foreach (var child in node.Children)
            AppendNode(sb, child, depth + 1);
    }

    internal static string TypeName(Type type) => type.FullName ?? type.Name;

    // Empty when no named rule set is used, so a definition without one keeps the fingerprint it always had.
    internal static string Instance(string? instance)
        => instance is null ? string.Empty : "|I:" + instance;

    internal static string Images(IReadOnlyDictionary<string, (int Width, int Height)>? sizes)
        => sizes is null
            ? string.Empty
            : string.Join(',', sizes.OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => string.Create(CultureInfo.InvariantCulture, $"{k.Key}={k.Value.Width}x{k.Value.Height}")));

    // Empty when no videos are declared, so a definition without videos keeps the fingerprint it always had.
    internal static string Videos(IReadOnlyDictionary<string, VideoSource> videos)
        => videos.Count == 0
            ? string.Empty
            : "|V:" + string.Join(';', videos.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={k.Value.Describe()}"));

    internal static string Styles(string? templateKey, IReadOnlyDictionary<string, string>? settings)
        => templateKey + "{" + (settings is null
            ? string.Empty
            : string.Join(',', settings.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={k.Value}"))) + "}";
}
