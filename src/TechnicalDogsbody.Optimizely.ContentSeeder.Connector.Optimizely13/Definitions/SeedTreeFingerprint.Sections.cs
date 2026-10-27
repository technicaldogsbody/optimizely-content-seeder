using System.Globalization;
using System.Text;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

// The Optimizely 13 part of the fingerprint: the Visual Builder sections a tree declares, and the sections each page places.
public static partial class SeedTreeFingerprint
{
    static partial void AppendSections(StringBuilder sb, SeedTree tree)
    {
        foreach (var section in tree.SectionsOf())
        {
            sb.Append("S|").Append(section.Id).Append('|').Append(TypeName(section.Type)).Append('|').Append(Images(section.ImageSizes)).Append(Videos(section.Videos)).Append(Instance(section.Instance)).Append('\n');
            foreach (var source in section.Rows)
                AppendRowSource(sb, source);
        }
    }

    static partial void AppendSlots(StringBuilder sb, SeedNode node, string indent)
    {
        foreach (var slot in node.SectionsOf())
            sb.Append(indent).Append(" P|").Append(slot.SectionId).Append('|').Append(slot.Name).Append('|')
              .Append(Styles(slot.DisplayTemplateKey, slot.DisplaySettings)).Append('\n');
    }

    private static void AppendRowSource(StringBuilder sb, ISectionRowSource source)
        => source.Accept(new RowSourceWriter(sb));

    // Writes a row source as lines of its own, with a line for each column and element it holds, so any change to
    // the shape shows up in the hash.
    private sealed class RowSourceWriter(StringBuilder sb) : ISectionRowSourceVisitor
    {
        /// <inheritdoc/>
        public void Visit(RowDefinition row)
        {
            sb.Append(" R|").Append(row.Name).Append('|').Append(Styles(row.DisplayTemplateKey, row.DisplaySettings)).Append('\n');
            foreach (var column in row.Columns)
            {
                sb.Append("  C|").Append(column.Name).Append('|').Append(Styles(column.DisplayTemplateKey, column.DisplaySettings)).Append('\n');
                foreach (var element in column.Elements)
                {
                    sb.Append("   E|").Append(TypeName(element.Type)).Append('|').Append(element.Name).Append('|')
                      .Append(Images(element.ImageSizes)).Append('|').Append(Styles(element.DisplayTemplateKey, element.DisplaySettings)).Append(Videos(element.Videos)).Append(Instance(element.Instance)).Append('\n');
                }
            }
        }

        /// <inheritdoc/>
        public void Visit(RepeatingElementDefinition repeating)
        {
            sb.Append(" X|").Append(TypeName(repeating.Type)).Append('|').Append(repeating.Name).Append('|')
              .Append(repeating.MinColumns.ToString(CultureInfo.InvariantCulture)).Append('-')
              .Append(repeating.MaxColumns.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(Images(repeating.ImageSizes)).Append('|').Append(Styles(repeating.DisplayTemplateKey, repeating.DisplaySettings))
              .Append(repeating.ReuseContent ? "|reuse" : string.Empty).Append(Videos(repeating.Videos)).Append(Instance(repeating.Instance)).Append('\n');
        }
    }
}
