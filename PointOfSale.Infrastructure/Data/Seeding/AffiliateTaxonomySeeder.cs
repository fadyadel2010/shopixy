using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Data.Seeding;

/// <summary>
/// AF-30B: Ensures the source-defined Affiliate taxonomy baseline
/// (<see cref="AffiliateTaxonomyCatalog"/>, provenance:
/// DB/sql/add_affiliate_category_system.sql) exists in PostgreSQL.
///
/// SEMANTICS — strictly INSERT-ONLY:
/// - Missing baseline rows are inserted once, parents before children.
/// - Existing rows are NEVER updated (renamed/reordered/deactivated/soft-deleted
///   rows are preserved exactly as an operator left them).
/// - Unexpected/future-managed rows are NEVER deleted.
/// - AffiliateProductPolicies rows are NEVER rewritten.
/// - The taxonomy stays global/platform master data: no TenantId is ever written.
///
/// CONCURRENCY & ATOMICITY:
/// - The whole pass runs in ONE transaction: a failure commits nothing, so no
///   partial hierarchy can become visible.
/// - Inserts are database-safe under simultaneous instances: deterministic
///   primary keys plus PostgreSQL `ON CONFLICT DO NOTHING` arbitrate through the
///   existing table constraints (PK, globally unique Slug, unique Parent+Name).
///   No application-memory check is trusted.
/// - Each insert is confirmed afterwards; if an insert was silently blocked by a
///   conflicting constraint (e.g., an operator-managed row squatting a canonical
///   slug), seeding throws immediately with the blocking identities listed —
///   BEFORE any child of that row is attempted, so canonical children can never
///   attach to the wrong parent. A final pass verifies that every canonical GUID
///   exists with its immutable level/parent linkage. Mutable operator-owned
///   fields (name, slug, order, active state, and audit metadata) are preserved.
///   All failures roll back the transaction visibly.
/// </summary>
public sealed class AffiliateTaxonomySeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AffiliateTaxonomySeeder> _logger;

    public AffiliateTaxonomySeeder(
        ApplicationDbContext dbContext,
        ILogger<AffiliateTaxonomySeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Insert-only ensure of every <see cref="AffiliateTaxonomyCatalog.Entries"/>
    /// row. Idempotent: repeated calls leave data byte-for-byte unchanged.
    /// Exceptions propagate to the caller (startup aborts visibly; tests assert).
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var entries = AffiliateTaxonomyCatalog.Entries;
        var createdAt = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Parents are strictly before children in the catalog, so the
        // self-referencing FK is satisfied within this single forward sweep.
        foreach (var entry in entries)
        {
            if (await CanonicalRowExistsAsync(entry.Id, cancellationToken))
            {
                // Insert-only: existing rows (however modified) are never touched.
                continue;
            }

            await _dbContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "AffiliateCategories"
                    ("Id", "Name", "Slug", "ParentId", "Level", "IsActive", "IsDeleted", "DisplayOrder", "CreatedAt")
                VALUES
                    ({entry.Id}, {entry.Name}, {entry.Slug}, {entry.ParentId}, {entry.Level}, TRUE, FALSE, {entry.DisplayOrder}, {createdAt})
                ON CONFLICT DO NOTHING
                """,
                cancellationToken);

            if (!await CanonicalRowExistsAsync(entry.Id, cancellationToken))
            {
                // The insert was silently blocked by a unique-constraint conflict
                // on operator-managed data. Fail visibly BEFORE any child of this
                // row is inserted — a canonical child must never attach elsewhere.
                var blockers = await FindBlockingRowsAsync(entry, cancellationToken);
                throw new InvalidOperationException(
                    $"Affiliate taxonomy bootstrap aborted before commit: baseline category {entry.Id} " +
                    $"('{entry.Name}', '{entry.Slug}') could not be inserted because conflicting rows already exist: " +
                    $"{blockers}. No changes were committed (transaction rolled back). The seeder never updates or " +
                    "deletes existing rows; resolve the collision manually.");
            }
        }

        await EnsureBaselineIdentityAsync(entries, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "[AF-30B] Affiliate taxonomy baseline ensured: {Count} canonical categories verified (insert-only).",
            entries.Count);
    }

    private async Task<bool> CanonicalRowExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.AffiliateCategories
            .AsNoTracking()
            .AnyAsync(row => row.Id == id, cancellationToken);
    }

    private async Task<string> FindBlockingRowsAsync(
        AffiliateTaxonomyEntry entry,
        CancellationToken cancellationToken)
    {
        var blockers = await _dbContext.AffiliateCategories
            .AsNoTracking()
            .Where(row => row.Slug == entry.Slug ||
                          (row.ParentId == entry.ParentId && row.Name == entry.Name))
            .Select(row => new { row.Id, row.Slug })
            .ToListAsync(cancellationToken);

        return string.Join(", ", blockers.Select(row => $"{row.Id} ('{row.Slug}')"));
    }

    private async Task EnsureBaselineIdentityAsync(
        IReadOnlyList<AffiliateTaxonomyEntry> entries,
        CancellationToken cancellationToken)
    {
        var catalogIds = entries.Select(entry => entry.Id).ToArray();
        var rows = await _dbContext.AffiliateCategories
            .AsNoTracking()
            .Where(row => catalogIds.Contains(row.Id))
            .Select(row => new { row.Id, row.ParentId, row.Level })
            .ToListAsync(cancellationToken);

        var problems = new List<string>();

        var rowsById = rows.ToDictionary(row => row.Id);

        foreach (var entry in entries)
        {
            if (!rowsById.TryGetValue(entry.Id, out var row))
            {
                problems.Add(
                    $"Canonical row {entry.Id} ('{entry.Name}', '{entry.Slug}') is missing; its insert was blocked by a conflicting constraint.");
                continue;
            }

            if (row.Level != entry.Level)
            {
                problems.Add(
                    $"Canonical row {entry.Id} ('{entry.Name}') has Level {row.Level} instead of canonical Level {entry.Level}.");
            }

            if (row.ParentId != entry.ParentId)
            {
                problems.Add(
                    $"Canonical row {entry.Id} ('{entry.Name}') has ParentId {row.ParentId?.ToString() ?? "<null>"} instead of canonical ParentId {entry.ParentId?.ToString() ?? "<null>"}.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Affiliate taxonomy bootstrap aborted before commit: deterministic category identity is corrupted. " +
                "No baseline rows were committed (transaction rolled back). Resolve the collisions below manually " +
                "(they are operator-managed data and the seeder never updates or deletes existing rows):\n" +
                string.Join("\n", problems));
        }
    }
}
