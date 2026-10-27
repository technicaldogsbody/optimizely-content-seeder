using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;

/// <summary>
/// Idempotent startup migration that creates the ContentSeederRecord table and its indexes
/// if they do not already exist. Uses Dapper — no EF Core dependency.
/// Called once from ContentSeederHostedService.StartAsync via IServiceScopeFactory.
/// </summary>
public sealed class ContentSeederMigration(IDbConnection db, ILogger<ContentSeederMigration> logger)
{
    private const string CreateTableSql = """
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = 'ContentSeederRecord'
        )
        BEGIN
            CREATE TABLE ContentSeederRecord (
                Id                UNIQUEIDENTIFIER  NOT NULL  DEFAULT NEWID()  PRIMARY KEY,
                SeedDefinitionId  NVARCHAR(256)     NOT NULL,
                BlockDefinitionId NVARCHAR(256)     NULL,
                ContentGuid       UNIQUEIDENTIFIER  NOT NULL,
                ContentId         INT               NOT NULL,
                ContentType       NVARCHAR(16)      NOT NULL,
                Language          NVARCHAR(16)      NOT NULL,
                CreatedUtc        DATETIME2         NOT NULL  DEFAULT GETUTCDATE(),
                UpdatedUtc        DATETIME2         NOT NULL  DEFAULT GETUTCDATE(),
                SeedRunId         UNIQUEIDENTIFIER  NOT NULL,
                IsPartial         BIT               NOT NULL  DEFAULT 0
            );
            CREATE INDEX IX_ContentSeederRecord_SeedDefinitionId
                ON ContentSeederRecord (SeedDefinitionId);
            CREATE INDEX IX_ContentSeederRecord_SeedRunId
                ON ContentSeederRecord (SeedRunId);
            CREATE INDEX IX_ContentSeederRecord_BlockDefinitionId
                ON ContentSeederRecord (BlockDefinitionId)
                WHERE BlockDefinitionId IS NOT NULL;
        END
        """;

    // One row per seed definition: the shape fingerprint it was seeded with (see SeedTreeFingerprint).
    private const string CreateDefinitionTableSql = """
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = 'ContentSeederDefinition'
        )
        BEGIN
            CREATE TABLE ContentSeederDefinition (
                SeedDefinitionId  NVARCHAR(256)  NOT NULL  PRIMARY KEY,
                Fingerprint       NVARCHAR(64)   NOT NULL,
                RecordedUtc       DATETIME2      NOT NULL
            );
        END
        """;

    /// <summary>
    /// Creates the tracking tables and their indexes if they are missing. Safe to run on every startup.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Running ContentSeederRecord migration check.");
        await db.ExecuteAsync(CreateTableSql).ConfigureAwait(false);
        await db.ExecuteAsync(CreateDefinitionTableSql).ConfigureAwait(false);
        logger.LogInformation("ContentSeederRecord migration check complete.");
    }
}
