using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;

/// <summary>
/// Dapper-backed implementation of ISeedTracker. Uses parameterised queries only.
/// Scoped lifetime.
/// </summary>
public sealed class ContentSeederRepository(
    IDbConnection db,
    IOptions<ContentSeederOptions> options) : ISeedTracker
{
    /// <summary>
    /// Inserts a tracking record before its content is created, so an interrupted run leaves a trace to resume from.
    /// </summary>
    public async Task InsertAsync(ContentSeederRecord record, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO ContentSeederRecord
                (Id, SeedDefinitionId, BlockDefinitionId, ContentGuid, ContentId,
                 ContentType, Language, CreatedUtc, UpdatedUtc, SeedRunId, IsPartial)
            VALUES
                (@Id, @SeedDefinitionId, @BlockDefinitionId, @ContentGuid, @ContentId,
                 @ContentType, @Language, @CreatedUtc, @UpdatedUtc, @SeedRunId, @IsPartial)
            """;
        await db.ExecuteAsync(sql, record).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the CMS content id of a saved item and clears its partial flag.
    /// </summary>
    public async Task MarkCompleteAsync(Guid id, int contentId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE ContentSeederRecord
            SET IsPartial = 0, ContentId = @ContentId, UpdatedUtc = GETUTCDATE()
            WHERE Id = @Id
            """;
        await db.ExecuteAsync(sql, new { Id = id, ContentId = contentId }).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the timestamp of a seed run's unfinished records, so an interrupted run shows as partial.
    /// </summary>
    public async Task MarkRunPartialAsync(Guid seedRunId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE ContentSeederRecord
            SET UpdatedUtc = GETUTCDATE()
            WHERE SeedRunId = @SeedRunId AND IsPartial = 1
            """;
        await db.ExecuteAsync(sql, new { SeedRunId = seedRunId }).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns every tracking record created by a seed definition.
    /// </summary>
    public async Task<IReadOnlyList<ContentSeederRecord>> GetByDefinitionAsync(
        string seedDefinitionId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT * FROM ContentSeederRecord
            WHERE SeedDefinitionId = @SeedDefinitionId
            """;
        var results = await db.QueryAsync<ContentSeederRecord>(
            sql, new { SeedDefinitionId = seedDefinitionId }).ConfigureAwait(false);
        return results.AsList();
    }

    /// <summary>
    /// Returns the tracking record for one block of a definition in a language, or null if there is none.
    /// </summary>
    public async Task<ContentSeederRecord?> GetBlockByDefinitionIdAsync(
        string blockDefinitionId, string language, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1 * FROM ContentSeederRecord
            WHERE BlockDefinitionId = @BlockDefinitionId
              AND Language = @Language
              AND IsPartial = 0
            """;
        return await db.QueryFirstOrDefaultAsync<ContentSeederRecord>(
            sql, new { BlockDefinitionId = blockDefinitionId, Language = language }).ConfigureAwait(false);
    }

    /// <summary>
    /// Summarises what has been seeded: page, block and section counts, when content was last seeded, and whether a run is partial.
    /// </summary>
    public async Task<ContentSeederStatus> GetStatusAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                SUM(CASE WHEN ContentType = 'Page'  AND IsPartial = 0 THEN 1 ELSE 0 END) AS TrackedPageCount,
                SUM(CASE WHEN ContentType = 'Block' AND IsPartial = 0 THEN 1 ELSE 0 END) AS TrackedBlockCount,
                SUM(CASE WHEN ContentType IN ('SectionBlueprint', 'SectionInstance') AND IsPartial = 0 THEN 1 ELSE 0 END) AS TrackedSectionCount,
                MAX(CASE WHEN IsPartial = 0 THEN UpdatedUtc END)                          AS LastSeededUtc,
                MAX(CASE WHEN IsPartial = 1 THEN 1 ELSE 0 END)                            AS HasPartialRun
            FROM ContentSeederRecord
            """;
        var row = await db.QueryFirstOrDefaultAsync<StatusRow>(sql).ConfigureAwait(false);

        var opts = options.Value;
        string[] languages = opts.MasterLanguage is null
            ? Array.Empty<string>()
            : new[] { opts.MasterLanguage }.Concat(opts.Languages).ToArray();

        return new ContentSeederStatus
        {
            TrackedPageCount  = row?.TrackedPageCount  ?? 0,
            TrackedBlockCount = row?.TrackedBlockCount ?? 0,
            TrackedSectionCount = row?.TrackedSectionCount ?? 0,
            // The column holds UTC (GETUTCDATE()), but Dapper returns it as Kind=Unspecified, which serialises without
            // a "Z" and makes the browser read it as local time. Tag it as UTC so the JSON carries the offset.
            LastSeededUtc     = row?.LastSeededUtc is { } utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : null,
            SeedOnStartup     = opts.SeedOnStartup,
            Languages         = languages,
            HasPartialRun     = (row?.HasPartialRun ?? 0) == 1,
        };
    }

    // Private typed DTO for the aggregate status query — avoids dynamic dispatch and runtime cast failures.
    private sealed class StatusRow
    {
        /// <summary>
        /// The number of complete page records.
        /// </summary>
        public int TrackedPageCount { get; set; }
        /// <summary>
        /// The number of complete block records.
        /// </summary>
        public int TrackedBlockCount { get; set; }
        /// <summary>
        /// The number of complete section records: blueprints and shared sections.
        /// </summary>
        public int TrackedSectionCount { get; set; }
        /// <summary>
        /// When a complete record was last updated. Null if nothing has been seeded.
        /// </summary>
        public DateTime? LastSeededUtc { get; set; }
        /// <summary>
        /// Non-zero when some record is still partial, which means a run was interrupted.
        /// </summary>
        public int HasPartialRun { get; set; }
    }

    /// <summary>
    /// Empties the tracking tables. Used when all seeded content has been deleted.
    /// </summary>
    public async Task TruncateAsync(CancellationToken ct = default)
    {
        await db.ExecuteAsync("TRUNCATE TABLE ContentSeederRecord").ConfigureAwait(false);
        await db.ExecuteAsync("TRUNCATE TABLE ContentSeederDefinition").ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteDefinitionAsync(string seedDefinitionId, CancellationToken ct = default)
    {
        var args = new { SeedDefinitionId = seedDefinitionId };
        await db.ExecuteAsync("DELETE FROM ContentSeederRecord WHERE SeedDefinitionId = @SeedDefinitionId", args).ConfigureAwait(false);
        await db.ExecuteAsync("DELETE FROM ContentSeederDefinition WHERE SeedDefinitionId = @SeedDefinitionId", args).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SeedDefinitionStatus>> GetDefinitionStatusesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                SeedDefinitionId,
                SUM(CASE WHEN ContentType = 'Page'  AND IsPartial = 0 THEN 1 ELSE 0 END) AS PageCount,
                SUM(CASE WHEN ContentType = 'Block' AND IsPartial = 0 THEN 1 ELSE 0 END) AS BlockCount,
                SUM(CASE WHEN ContentType IN ('SectionBlueprint', 'SectionInstance') AND IsPartial = 0 THEN 1 ELSE 0 END) AS SectionCount,
                MAX(CASE WHEN IsPartial = 0 THEN UpdatedUtc END)                          AS LastSeededUtc,
                MAX(CASE WHEN IsPartial = 1 THEN 1 ELSE 0 END)                            AS HasPartialRun
            FROM ContentSeederRecord
            GROUP BY SeedDefinitionId
            """;
        var rows = await db.QueryAsync<DefinitionStatusRow>(sql).ConfigureAwait(false);

        return rows
            .Select(r => new SeedDefinitionStatus(
                r.SeedDefinitionId,
                r.PageCount,
                r.BlockCount,
                r.SectionCount,
                r.LastSeededUtc,
                r.HasPartialRun != 0))
            .ToList();
    }

    private sealed class DefinitionStatusRow
    {
        public string SeedDefinitionId { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public int BlockCount { get; set; }
        public int SectionCount { get; set; }
        public DateTime? LastSeededUtc { get; set; }
        public int HasPartialRun { get; set; }
    }

    /// <summary>
    /// Returns the fingerprint recorded for a definition, or null if none was recorded.
    /// </summary>
    public async Task<string?> GetDefinitionFingerprintAsync(string seedDefinitionId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Fingerprint FROM ContentSeederDefinition
            WHERE SeedDefinitionId = @SeedDefinitionId
            """;
        return await db.QueryFirstOrDefaultAsync<string>(
            sql, new { SeedDefinitionId = seedDefinitionId }).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the fingerprint of a definition's shape, replacing any earlier one.
    /// </summary>
    public async Task SetDefinitionFingerprintAsync(string seedDefinitionId, string fingerprint, CancellationToken ct = default)
    {
        // Delete-then-insert rather than MERGE so the SQL stays portable (integration tests run on SQLite).
        var args = new { SeedDefinitionId = seedDefinitionId, Fingerprint = fingerprint, RecordedUtc = DateTime.UtcNow };
        await db.ExecuteAsync(
            "DELETE FROM ContentSeederDefinition WHERE SeedDefinitionId = @SeedDefinitionId", args).ConfigureAwait(false);
        await db.ExecuteAsync("""
            INSERT INTO ContentSeederDefinition (SeedDefinitionId, Fingerprint, RecordedUtc)
            VALUES (@SeedDefinitionId, @Fingerprint, @RecordedUtc)
            """, args).ConfigureAwait(false);
    }
}
