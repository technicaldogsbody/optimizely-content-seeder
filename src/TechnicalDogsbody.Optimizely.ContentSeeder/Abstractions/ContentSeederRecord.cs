namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// POCO mapping to the ContentSeederRecord SQL table.
/// Owned entirely by the package — never modified by consumer code.
/// </summary>
public sealed class ContentSeederRecord
{
    /// <summary>Valid values for <see cref="ContentType"/>.</summary>
    public static class ContentTypes
    {
        /// <summary>
        /// A page created by the seeder.
        /// </summary>
        public const string Page = "Page";
        /// <summary>
        /// A shared block created by the seeder.
        /// </summary>
        public const string Block = "Block";

        /// <summary>An Optimizely Blueprint created from a SectionDefinition. Guid-keyed (via ContentGuid); ContentId is unused (0).</summary>
        public const string SectionBlueprint = "SectionBlueprint";

        /// <summary>
        /// A shared, standalone content instance stamped from a leaf BlockData-as-section blueprint
        /// (e.g. HeroSection) via a SectionSlotAssignment placement. Structural SectionData sections
        /// (e.g. BlankSection) are placed inline instead and have no corresponding record — see
        /// SeedOrchestrator.SectionBlueprintRef.IsStructural.
        /// </summary>
        public const string SectionInstance = "SectionInstance";
    }

    /// <summary>
    /// The record's own identifier. It is not the CMS content identifier, which is ContentId.
    /// </summary>
    public Guid Id { get; set; }
    /// <summary>
    /// The seed definition that created the record.
    /// </summary>
    public string SeedDefinitionId { get; set; } = string.Empty;
    /// <summary>
    /// The id of the block or section within the seed definition. Null for pages.
    /// </summary>
    public string? BlockDefinitionId { get; set; }
    /// <summary>
    /// The CMS content GUID. For a section blueprint, the blueprint's id.
    /// </summary>
    public Guid ContentGuid { get; set; }
    /// <summary>
    /// The CMS content id, set once the item is saved. Zero while the record is still being created.
    /// </summary>
    public int ContentId { get; set; }

    /// <summary>'Page' or 'Block'.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// The language of this version of the content.
    /// </summary>
    public string Language { get; set; } = string.Empty;
    /// <summary>
    /// When the record was first created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
    /// <summary>
    /// When the record was last changed.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }
    /// <summary>
    /// The seed run that created the record. Records from one run share the id, which lets an interrupted run be found and resumed.
    /// </summary>
    public Guid SeedRunId { get; set; }

    /// <summary>True while the run is in-flight or was cancelled. False on successful save.</summary>
    public bool IsPartial { get; set; }
}
