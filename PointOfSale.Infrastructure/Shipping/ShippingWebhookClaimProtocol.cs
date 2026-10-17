using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Shipping;

public sealed class ShippingWebhookClaimOptions
{
    // Intentionally off. Enabling requires migration + relational certification + fleet rollout.
    public bool Enabled { get; set; }
}

/// <summary>
/// PostgreSQL-only owner fencing. All operations lock inbox; business callers retain that
/// lock until the SAME transaction commits effects and outbox. Lease expiry alone is not liveness.
/// No InMemory fallback: tests must execute these SQL statements to certify the protocol.
/// </summary>
public sealed class ShippingWebhookClaimProtocol(ApplicationDbContext db)
{
    public const int LeaseSeconds = 60;
    private readonly HashSet<(Guid Log, long Generation, Guid Owner, Guid Transaction)> businessFences = [];

    private static bool Bound(ShippingWebhookLog log, ShippingWebhookClaim claim) =>
        claim.LogId == log.Id && claim.TenantId == log.TenantId && claim.Owner != Guid.Empty && claim.Generation > 0;

    private void RequireTransaction()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Webhook ownership requires an owned PostgreSQL transaction.");
    }

    public async Task<ShippingWebhookLog?> LockAsync(ShippingWebhookLog identity, CancellationToken ct)
    {
        RequireTransaction();
        // NOWAIT is important: do not steal even an expired claim from a paused business transaction.
        return await db.ShippingWebhookLogs.FromSqlInterpolated($"""
            SELECT * FROM "ShippingWebhookLogs"
            WHERE "Id"={identity.Id} AND "TenantId"={identity.TenantId}
              AND "CarrierCode"={identity.CarrierCode} AND "EventId"={identity.EventId}
              AND "PayloadHash"={identity.PayloadHash} AND "ShipmentId"={identity.ShipmentId}
              AND "OrderId"={identity.OrderId} AND NOT "IsDeleted" FOR UPDATE NOWAIT
            """).IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(ct);
    }

    private Task<ShippingWebhookClaim?> LatestAsync(ShippingWebhookLog log, CancellationToken ct) =>
        db.Set<ShippingWebhookClaim>().AsNoTracking().Where(c => c.LogId == log.Id && c.TenantId == log.TenantId)
            .OrderByDescending(c => c.Generation).FirstOrDefaultAsync(ct);

    // Own the INSERT as well: no caller can opt an existing legacy row into generation 1.
    public async Task<ShippingWebhookClaim> CreateFirstAsync(ShippingWebhookLog log, CancellationToken ct)
    {
        RequireTransaction();
        if (db.Entry(log).State != EntityState.Added || log.ProcessingStatus != WebhookProcessingStatus.Processing ||
            log.TenantId == Guid.Empty || log.Id == Guid.Empty)
            throw new InvalidOperationException("Generation 1 requires a new Processing inbox inserted in this transaction.");
        if (db.ChangeTracker.Entries().Any(e => !ReferenceEquals(e.Entity, log) &&
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Claim admission cannot flush unrelated or unfenced business changes.");
        await db.SaveChangesAsync(ct);
        var owner = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ShippingWebhookClaims" ("LogId","TenantId","Generation","Owner","AcquiredAt","LeaseUntil","Outcome")
            VALUES ({log.Id},{log.TenantId},1,{owner},clock_timestamp(),clock_timestamp()+make_interval(secs => {LeaseSeconds}),'Processing')
            """, ct);
        return (await LatestAsync(log, ct))!;
    }

    public async Task<ShippingWebhookClaim?> AcquireAsync(ShippingWebhookLog identity, CancellationToken ct)
    {
        RequireTransaction();
        var log = await LockAsync(identity, ct);
        if (log == null || log.ProcessingStatus is not (WebhookProcessingStatus.Processing or WebhookProcessingStatus.Failed)) return null;
        var previous = await LatestAsync(log, ct);
        // Missing metadata is legacy, not generation zero. Inconsistent audit is never replayable.
        if (previous == null || previous.Generation == long.MaxValue ||
            (log.ProcessingStatus == WebhookProcessingStatus.Processing && previous.Outcome != "Processing") ||
            (log.ProcessingStatus == WebhookProcessingStatus.Failed && previous.Outcome != "Failed")) return null;
        var owner = Guid.NewGuid();
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ShippingWebhookClaims" ("LogId","TenantId","Generation","Owner","AcquiredAt","LeaseUntil","Outcome")
            SELECT "LogId","TenantId","Generation"+1,{owner},clock_timestamp(),clock_timestamp()+make_interval(secs => {LeaseSeconds}),'Processing'
            FROM "ShippingWebhookClaims" c WHERE "LogId"={log.Id} AND "TenantId"={log.TenantId}
              AND "Generation"={previous.Generation} AND "Owner"={previous.Owner}
              AND ("Outcome"='Failed' OR ("Outcome"='Processing' AND "LeaseUntil"<=clock_timestamp()))
              AND NOT EXISTS (SELECT 1 FROM "ShippingWebhookClaims" n WHERE n."LogId"=c."LogId" AND n."Generation">c."Generation")
            """, ct);
        if (count != 1) return null;
        if (log.ProcessingStatus == WebhookProcessingStatus.Failed)
        {
            var changed = await db.ShippingWebhookLogs.IgnoreQueryFilters()
                .Where(l => l.Id == log.Id && l.TenantId == log.TenantId && l.ProcessingStatus == WebhookProcessingStatus.Failed)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ProcessingStatus, WebhookProcessingStatus.Processing), ct);
            if (changed != 1) throw new InvalidOperationException("Inbox acquisition lost its status fence.");
        }
        return await LatestAsync(log, ct);
    }

    public async Task<bool> RenewAsync(ShippingWebhookLog log, ShippingWebhookClaim claim, CancellationToken ct)
    {
        RequireTransaction();
        if (!Bound(log, claim)) return false;
        var current = await LockAsync(log, ct);
        if (current?.ProcessingStatus != WebhookProcessingStatus.Processing) return false;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ShippingWebhookClaims" c SET "LeaseUntil"=clock_timestamp()+make_interval(secs => {LeaseSeconds})
            WHERE "LogId"={log.Id} AND "TenantId"={log.TenantId} AND "Generation"={claim.Generation}
              AND "Owner"={claim.Owner} AND "Outcome"='Processing' AND "LeaseUntil">clock_timestamp()
              AND NOT EXISTS (SELECT 1 FROM "ShippingWebhookClaims" n WHERE n."LogId"=c."LogId" AND n."Generation">c."Generation")
            """, ct) == 1;
    }

    public async Task<bool> FenceBusinessAsync(ShippingWebhookLog log, ShippingWebhookClaim claim, CancellationToken ct)
    {
        RequireTransaction();
        var current = await LockAsync(log, ct);
        if (current?.ProcessingStatus != WebhookProcessingStatus.Processing) return false;
        // A conditional renewal is also the owner/expiry check at transaction entry.
        if (!await RenewAsync(log, claim, ct)) return false;
        businessFences.Add((log.Id, claim.Generation, claim.Owner, db.Database.CurrentTransaction!.TransactionId));
        return true;
    }

    // The caller must already hold the business fence; lease may expire while the row lock is held.
    public async Task FinishAsync(ShippingWebhookLog log, ShippingWebhookClaim claim, bool failed, string? error, CancellationToken ct)
    {
        RequireTransaction();
        if (!Bound(log, claim)) throw new InvalidOperationException("Claim does not bind to this inbox and tenant.");
        if (!failed && !businessFences.Contains((log.Id, claim.Generation, claim.Owner, db.Database.CurrentTransaction!.TransactionId)))
            throw new InvalidOperationException("Completion requires the business fence in this transaction.");
        var current = await LockAsync(log, ct);
        if (current == null || (failed && current.ProcessingStatus != WebhookProcessingStatus.Processing))
            throw new InvalidOperationException("Inbox identity/status fence lost; terminal audit cannot be failed.");
        var outcome = failed ? "Failed" : "Completed";
        var boundedError = error == null ? null : error[..Math.Min(error.Length, 1000)];
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ShippingWebhookClaims" c SET "Outcome"={outcome},"FinishedAt"=clock_timestamp(),"Error"={boundedError}
            WHERE "LogId"={log.Id} AND "TenantId"={log.TenantId} AND "Generation"={claim.Generation}
              AND "Owner"={claim.Owner} AND "Outcome"='Processing'
              AND NOT EXISTS (SELECT 1 FROM "ShippingWebhookClaims" n WHERE n."LogId"=c."LogId" AND n."Generation">c."Generation")
            """, ct);
        if (count != 1) throw new InvalidOperationException("Webhook owner superseded; transaction must roll back.");
    }

    public async Task FailAsync(ShippingWebhookLog log, ShippingWebhookClaim claim, string error, long duration, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Failure audit must follow disposal of the business transaction.");
        if (!Bound(log, claim)) return;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var current = await LockAsync(log, ct);
        var latest = current == null ? null : await LatestAsync(current, ct);
        if (current?.ProcessingStatus != WebhookProcessingStatus.Processing || latest?.Generation != claim.Generation ||
            latest.Owner != claim.Owner || latest.Outcome != "Processing") return;
        await FinishAsync(log, claim, true, error, ct);
        var bounded = error[..Math.Min(error.Length, 1000)];
        var updated = await db.ShippingWebhookLogs.IgnoreQueryFilters().Where(l => l.Id == log.Id && l.TenantId == log.TenantId && l.ProcessingStatus == WebhookProcessingStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ProcessingStatus, WebhookProcessingStatus.Failed)
                .SetProperty(l => l.ProcessingError, bounded).SetProperty(l => l.ProcessedAt, DateTime.UtcNow)
                .SetProperty(l => l.ProcessingDurationMs, duration), ct);
        if (updated != 1) throw new InvalidOperationException("Failure audit lost inbox status fence; transaction must roll back.");
        await tx.CommitAsync(ct);
    }
}
