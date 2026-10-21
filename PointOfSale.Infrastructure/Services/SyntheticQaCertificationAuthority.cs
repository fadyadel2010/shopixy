using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

public sealed class SyntheticQaOptions
{
    public bool Enabled { get; set; }
    public string Database { get; set; } = "";
    public string Server { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public Guid TenantId { get; set; }
    public Guid[] VerifierIds { get; set; } = [];
}

/// <summary>Shared fail-closed evaluator for checkout enrollment, fulfillment and commission.
/// No endpoint accepts a caller-supplied amount, rate, delivered timestamp or signature.</summary>
public sealed class SyntheticQaCertificationAuthority(
    ApplicationDbContext db, IHostEnvironment environment, IOptionsMonitor<SyntheticQaOptions> options)
{
    public static readonly Guid AhramStoreId = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
    public static readonly Guid TargetProductId = Guid.Parse("15e044bc-eaa1-c905-31a4-658aac4ad14b");
    public static readonly Guid[] ProtectedOrders = [
        Guid.Parse("d1102ed5-4256-4cef-80ca-41fe57c6163e"), Guid.Parse("32a82d41-2ae6-47f9-aa4f-cd5d17b1e8d6"),
        Guid.Parse("40b22f7f-09ac-4c60-b779-f14cf3734fe2"), Guid.Parse("2654c8f2-c859-41ad-b637-212affd7b1b4")];

    public static bool IsRuntimeAllowed(string environmentName, SyntheticQaOptions value) =>
        value.Enabled && environmentName is "Development" or "Test" &&
        value.TenantId == AhramStoreId && value.SigningKey.Length >= 64 &&
        !string.IsNullOrWhiteSpace(value.Database) && !string.IsNullOrWhiteSpace(value.Server);

    public async Task EnsureEnabledAsync(CancellationToken ct = default)
    {
        var value = options.CurrentValue;
        if (!IsRuntimeAllowed(environment.EnvironmentName, value) || !db.Database.IsNpgsql())
            throw new InvalidOperationException("SYNTHETIC_QA_UNAVAILABLE");
        // Check actual connected server identity, not just an app-supplied connection string label.
        var identity = await db.Database.SqlQueryRaw<DatabaseIdentity>(
            "SELECT current_database() AS \"Database\", host(inet_server_addr()) AS \"Server\"").SingleAsync(ct);
        if (identity.Database != value.Database || identity.Server != value.Server)
            throw new InvalidOperationException("SYNTHETIC_QA_DATABASE_NOT_APPROVED");
    }

    public async Task EnsureVerifierAsync(Guid tenantId, Guid verifierId, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        if (tenantId != AhramStoreId || !options.CurrentValue.VerifierIds.Contains(verifierId))
            throw new UnauthorizedAccessException("SYNTHETIC_QA_VERIFIER_REQUIRED");
        if (!await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == verifierId && x.TenantId == tenantId && !x.IsDeleted && x.IsActive, ct))
            throw new UnauthorizedAccessException("SYNTHETIC_QA_VERIFIER_INACTIVE");
    }

    public static string OrderMarker(Guid runId) => $"{SyntheticQaEvidence.ProvenanceLabel} | QA-RUN:{runId:D}";

    public async Task<SyntheticQaRun> CreateRunAsync(Guid tenantId, Guid verifierId, Guid linkId, Guid customerId, CancellationToken ct)
    {
        await EnsureVerifierAsync(tenantId, verifierId, ct);
        if (!await db.AffiliatePromotionLinks.IgnoreQueryFilters().AnyAsync(x => x.Id == linkId && x.TenantId == tenantId && x.ProductId == TargetProductId && !x.IsDeleted && x.IsActive, ct) ||
            !await db.OnlineCustomers.IgnoreQueryFilters().AnyAsync(x => x.Id == customerId && x.TenantId == tenantId && x.IsActive, ct))
            throw new InvalidOperationException("SYNTHETIC_QA_RUN_TARGET_INVALID");
        var now = DateTime.UtcNow;
        var run = new SyntheticQaRun { Id = Guid.NewGuid(), TenantId = tenantId, VerifierId = verifierId,
            ProductId = TargetProductId, PromotionLinkId = linkId, CustomerId = customerId,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(2) };
        db.SyntheticQaRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    // Called ONLY inside the ordinary checkout order transaction. No historical enrollment endpoint exists.
    public async Task EnrollCheckoutAsync(Order order, CancellationToken ct)
    {
        if (!(order.CustomerNotes ?? "").StartsWith(SyntheticQaEvidence.ProvenanceLabel, StringComparison.Ordinal)) return;
        await EnsureEnabledAsync(ct);
        var marker = order.CustomerNotes!.Split("QA-RUN:", StringSplitOptions.None);
        if (marker.Length != 2 || !Guid.TryParse(marker[1], out var runId)) throw new InvalidOperationException("SYNTHETIC_QA_MARKER_INVALID");
        var run = await db.SyntheticQaRuns.SingleOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new InvalidOperationException("SYNTHETIC_QA_RUN_INVALID");
        ValidateRunOrder(run, order, DateTime.UtcNow);
        if (await db.SyntheticQaOrders.CountAsync(x => x.RunId == runId, ct) >= 2)
            throw new InvalidOperationException("SYNTHETIC_QA_RUN_ORDER_LIMIT");
        db.SyntheticQaOrders.Add(new SyntheticQaOrder { OrderId = order.Id, RunId = runId,
            TenantId = order.TenantId, EnrolledAtUtc = DateTime.UtcNow });
    }

    public static void ValidateRunOrder(SyntheticQaRun run, Order order, DateTime now)
    {
        if (ProtectedOrders.Contains(order.Id) || order.OrderNumber is "ORD-83B3-2026-01091" or "ORD-C43A-2026-01093" or "ORD-83B3-2026-01094" or "ORD-C43A-2026-01095" ||
            run.TenantId != AhramStoreId || order.TenantId != run.TenantId || order.OnlineCustomerId != run.CustomerId ||
            order.CreatedAt < run.CreatedAtUtc || order.CreatedAt > now || run.ExpiresAtUtc <= now || run.CreatedAtUtc > now ||
            order.CustomerNotes != OrderMarker(run.Id) || order.PromotionLinkId != run.PromotionLinkId ||
            order.FulfillmentMode != FulfillmentMode.Manual || order.Items.Count != 1 ||
            order.Items.Any(x => x.ProductId != run.ProductId || x.ProductId != TargetProductId || x.Quantity != 1) ||
            order.PaymentMethodCode is not ("COD" or "MANUALWALLET") || order.Total <= 0 ||
            order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
            throw new InvalidOperationException("SYNTHETIC_QA_ORDER_NOT_IN_ACTIVE_RUN");
    }

    public async Task<SyntheticQaRun> ValidateOrderAsync(Order order, Guid runId, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        var binding = await db.SyntheticQaOrders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == order.Id && x.RunId == runId && x.TenantId == order.TenantId, ct)
            ?? throw new InvalidOperationException("SYNTHETIC_QA_ORDER_NOT_ENROLLED");
        var run = await db.SyntheticQaRuns.AsNoTracking().SingleAsync(x => x.Id == binding.RunId, ct);
        // Reuse scoped EF identities: collection/inventory share this DbContext.
        // Replacing tracked navigation members with detached clones conflicts when
        // the inventory authority attaches the same immutable order lines.
        order.Items = await db.OrderItems.IgnoreQueryFilters().Where(x => x.OrderId == order.Id && !x.IsDeleted).ToListAsync(ct);
        ValidateRunOrder(run, order, DateTime.UtcNow);
        return run;
    }

    public async Task<SyntheticQaEvidence> IssueAsync(Order order, Guid runId, Guid verifierId, Guid shipmentId, string kind, CancellationToken ct)
    {
        await EnsureVerifierAsync(order.TenantId, verifierId, ct);
        var run = await ValidateOrderAsync(order, runId, ct);
        if (run.VerifierId != verifierId || kind is not ("Collection" or "Delivery"))
            throw new InvalidOperationException("SYNTHETIC_QA_EVIDENCE_INVALID");
        if ((kind == "Delivery" || shipmentId != Guid.Empty) && !await db.Shipments.IgnoreQueryFilters().AnyAsync(x => x.Id == shipmentId && x.OrderId == order.Id &&
                x.TenantId == order.TenantId && !x.IsDeleted && x.CarrierCode == "manual", ct))
            throw new InvalidOperationException("SYNTHETIC_QA_SHIPMENT_INVALID");
        var existing = await db.SyntheticQaEvidence.SingleOrDefaultAsync(x => x.OrderId == order.Id && x.Kind == kind, ct);
        if (existing != null)
        {
            ValidateEvidence(existing, order, run, shipmentId == Guid.Empty ? null : shipmentId, kind);
            return existing;
        }
        var evidence = new SyntheticQaEvidence { Id = Guid.NewGuid(), RunId = runId, OrderId = order.Id,
            TenantId = order.TenantId, ShipmentId = shipmentId == Guid.Empty ? null : shipmentId, VerifierId = verifierId, Kind = kind,
            Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), IssuedAtUtc = MicrosecondUtcNow(),
            Amount = kind == "Collection" ? order.Total : 0, Currency = order.Currency,
            Debit = kind == "Collection" ? "QA-VIRTUAL-CLEARING-NO-MONEY" : "",
            Credit = kind == "Collection" ? "QA-VIRTUAL-REVENUE-NO-MONEY" : "" };
        evidence.Signature = Sign(evidence);
        db.SyntheticQaEvidence.Add(evidence);
        await db.SaveChangesAsync(ct); // Durable BEFORE consumption, including on later delivery failure.
        return evidence;
    }

    public void ValidateEvidence(SyntheticQaEvidence evidence, Order order, SyntheticQaRun run, Guid? shipmentId, string kind)
    {
        if (evidence.OrderId != order.Id || evidence.TenantId != order.TenantId || evidence.RunId != run.Id ||
            evidence.VerifierId != run.VerifierId || evidence.ShipmentId != shipmentId || evidence.Kind != kind ||
            evidence.IssuedAtUtc < run.CreatedAtUtc || evidence.IssuedAtUtc >= run.ExpiresAtUtc ||
            evidence.IssuedAtUtc > DateTime.UtcNow || evidence.Provenance != SyntheticQaEvidence.ProvenanceLabel ||
            evidence.Currency != order.Currency || evidence.Amount != (kind == "Collection" ? order.Total : 0) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(evidence.Signature), Encoding.UTF8.GetBytes(Sign(evidence))))
            throw new InvalidOperationException("SYNTHETIC_QA_EVIDENCE_FORGED_OR_MISMATCHED");
    }

    public async Task<SyntheticQaEvidence> RequireEvidenceAsync(Order order, Guid evidenceId, string kind, CancellationToken ct)
    {
        var evidence = await db.SyntheticQaEvidence.SingleOrDefaultAsync(x => x.Id == evidenceId, ct)
            ?? throw new InvalidOperationException("SYNTHETIC_QA_EVIDENCE_MISSING");
        var run = await ValidateOrderAsync(order, evidence.RunId, ct);
        ValidateEvidence(evidence, order, run, evidence.ShipmentId, kind);
        if ((kind == "Delivery" || evidence.ShipmentId.HasValue) && !await db.Shipments.IgnoreQueryFilters().AnyAsync(x => x.Id == evidence.ShipmentId && x.OrderId == order.Id &&
            x.TenantId == order.TenantId && x.CarrierCode == "manual" && !x.IsDeleted, ct))
            throw new InvalidOperationException("SYNTHETIC_QA_SHIPMENT_INVALID");
        return evidence;
    }

    public async Task<bool> HasDeliveryAuthorityAsync(Order order, string? reference, string? proof, CancellationToken ct, bool requireConsumed = false)
    {
        if (reference == null || !reference.StartsWith("SYNTHETIC-QA:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(reference[13..], "N", out var id)) return false;
        var evidence = await RequireEvidenceAsync(order, id, "Delivery", ct);
        return evidence.DeliveryReference == reference && evidence.ProofUrl == proof && (!requireConsumed || evidence.ConsumedAtUtc.HasValue);
    }

    public async Task EnsureSyntheticCollectionAsync(Order order, CancellationToken ct)
    {
        var evidence = await db.SyntheticQaEvidence.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == order.Id && x.Kind == "Collection", ct)
            ?? throw new InvalidOperationException("SYNTHETIC_QA_COLLECTION_MISSING");
        await RequireEvidenceAsync(order, evidence.Id, "Collection", ct);
        if (!evidence.ConsumedAtUtc.HasValue || order.PaymentGateway != "SYNTHETIC_QA_NO_MONEY" ||
            order.IsPaid || order.TotalPaidAmount != 0 || order.FinancialPostingStatus != FinancialPostingStatus.NotRequired)
            throw new InvalidOperationException("SYNTHETIC_QA_COLLECTION_AUTHORITY_INVALID");
    }

    private string Sign(SyntheticQaEvidence e) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(options.CurrentValue.SigningKey), Encoding.UTF8.GetBytes(string.Join("|",
            e.Id, e.RunId, e.TenantId, e.OrderId, e.ShipmentId, e.VerifierId, e.Kind, e.Nonce,
            e.IssuedAtUtc.ToUniversalTime().ToString("O"), e.Amount.ToString("F6", CultureInfo.InvariantCulture),
            e.Currency, e.Debit, e.Credit, e.Provenance))));

    private static DateTime MicrosecondUtcNow() { var now = DateTime.UtcNow; return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc); }

    private sealed class DatabaseIdentity
    {
        public string Database { get; set; } = "";
        public string Server { get; set; } = "";
    }
}
