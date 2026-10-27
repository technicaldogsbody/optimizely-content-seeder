using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

// The Optimizely 13 part of the named rule set check: the sections a tree declares, their rows, columns and elements.
internal static partial class InstanceNames
{
    static partial void ValidateSections(SeedTree tree, ISeedDefinition definition)
    {
        foreach (var section in tree.SectionsOf())
        {
            Check(section.Instance, section.Type, definition, $"section '{section.Id}'");
            foreach (var source in section.Rows)
                CheckRow(source, definition, section.Id);
        }
    }

    private static void CheckRow(ISectionRowSource source, ISeedDefinition definition, string sectionId)
    {
        if (source is RowDefinition row)
        {
            foreach (var column in row.Columns)
                foreach (var element in column.Elements)
                    Check(element.Instance, element.Type, definition, $"an element of section '{sectionId}'");
        }
        else if (source is RepeatingElementDefinition repeating)
        {
            Check(repeating.Instance, repeating.Type, definition, $"a repeating element of section '{sectionId}'");
        }
    }
}
