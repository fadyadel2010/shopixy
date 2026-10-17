using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Shipping;

internal static class CarrierCollectionMoneyAuthority
{
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // Explicit operator-configured account money denomination. Neither market geography nor order display currency supplies it.
    internal static string? ConfiguredCurrency(IReadOnlyDictionary<string, string> configuration)
    {
        var values = configuration
            .Where(pair => pair.Key.Equals("codCollectionCurrency", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (values.Length != 1 || values[0].Key != "codCollectionCurrency") return null;
        var code = values[0].Value;
        return code?.Length == 3 && code.All(c => c is >= 'A' and <= 'Z') ? code : null;
    }

    // Bosta's supported setup intentionally has no caller-entered external AccountId.
    // Bind money to the persisted credential version instead of inventing a provider
    // account or weakening account-change detection.
    internal static string? AccountScope(CarrierConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.AccountId)) return config.AccountId.Trim();
        if (string.IsNullOrWhiteSpace(config.ApiKey)) return null;
        return $"bosta-credential:{Hash($"{config.Id:D}:{config.ApiKey}")}";
    }

    internal static string BindingFingerprint(
        CarrierConfiguration config,
        Shipment shipment,
        string accountScope,
        string currency,
        string deliveryReference) =>
        Hash(JsonSerializer.Serialize(new
        {
            shipment.TenantId,
            ShipmentId = shipment.Id,
            ConfigurationId = config.Id,
            AccountScope = accountScope,
            config.UseSandbox,
            Currency = currency,
            ConfigurationCiphertext = config.Configuration,
            shipment.TrackingNumber,
            DeliveryReference = deliveryReference,
            shipment.ExternalReferenceId
        }));

    internal static async Task<string?> QualifyAsync(ApplicationDbContext db, Order order, Shipment shipment,
        CarrierConfiguration config, CarrierCodShipmentBinding? binding, CarrierMoneyObservation observation, CancellationToken ct)
    {
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == order.TenantId && t.IsActive && t.Status == TenantStatus.Active, ct))
            return "COD_TENANT_INACTIVE";
        if (order.IsDeleted || shipment.IsDeleted || shipment.OrderId != order.Id || shipment.TenantId != order.TenantId ||
            order.FulfillmentMode != FulfillmentMode.Carrier || order.FulfillmentCarrierCode != shipment.CarrierCode || shipment.CarrierCode != "bosta")
            return "COD_CARRIER_OWNERSHIP_MISMATCH";
        if (!shipment.IsCashOnDelivery || !(order.PaymentMethodCode.Equals("COD", StringComparison.OrdinalIgnoreCase) ||
            order.PaymentMethodCode.Equals("CASH_ON_DELIVERY", StringComparison.OrdinalIgnoreCase))) return "COD_ORDER_REQUIRED";
        if (config.IsDeleted || !config.IsEnabled || !config.SupportsCod || !config.SupportsWebhooks || config.TenantId != order.TenantId || config.CarrierCode != shipment.CarrierCode)
            return "COD_ACCOUNT_UNAVAILABLE";
        if (binding == null) return "COD_ACCOUNT_BINDING_REQUIRED";
        var currency = binding.Currency;
        var denomination = currency == null ? null : await db.Currencies.SingleOrDefaultAsync(c => c.Code == currency, ct);
        if (denomination == null || denomination.DecimalPlaces is < 0 or > 6) return "COD_CURRENCY_BINDING_UNSUPPORTED";
        var accountScope = AccountScope(config);
        if (accountScope == null) return "COD_ACCOUNT_UNAVAILABLE";
        if (binding.TenantId != order.TenantId || binding.OrderId != order.Id || binding.ShipmentId != shipment.Id ||
            binding.ConfigurationId != config.Id || binding.CarrierCode != shipment.CarrierCode || binding.AccountId != accountScope ||
            binding.Environment != (config.UseSandbox ? "Sandbox" : "Production") || binding.Currency != currency)
            return "COD_ACCOUNT_BINDING_CHANGED";
        if (binding.Fingerprint != BindingFingerprint(config, shipment, accountScope, currency, binding.DeliveryReference))
            return "COD_ACCOUNT_BINDING_CHANGED";
        if (binding.TrackingNumber != shipment.TrackingNumber || binding.ExternalReferenceId != shipment.ExternalReferenceId ||
            binding.DeliveryReference != observation.DeliveryReference) return "COD_SHIPMENT_IDENTITY_CHANGED";
        if (observation.Contract != "bosta:delivery-status-webhook:v1" || observation.DeliveryType != 10 || observation.State != 45)
            return "COD_COLLECTION_CONTEXT_UNSUPPORTED";
        if (observation.ConfirmedDelivery == false) return "COD_DELIVERY_UNCONFIRMED";
        if (observation.AmountCode != "COD_AMOUNT_OBSERVED" || !observation.Amount.HasValue) return observation.AmountCode;
        if (decimal.Round(observation.Amount.Value, denomination.DecimalPlaces) != observation.Amount.Value) return "COD_AMOUNT_PRECISION_UNSUPPORTED";
        var tenantCurrency = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == order.TenantId).Select(t => t.Currency).SingleAsync(ct);
        if (order.Currency != currency || shipment.Currency != currency || tenantCurrency != currency) return "COD_CURRENCY_MISMATCH";
        if (order.Total <= 0m || shipment.CodAmount != order.Total || observation.Amount != order.Total) return "COD_WHOLE_ORDER_AMOUNT_MISMATCH";
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded ||
            order.PaymentStatus is not (PaymentStatus.Pending or PaymentStatus.Collected) ||
            order.FulfillmentStatus != FulfillmentStatus.Delivered || shipment.Status != ShipmentStatus.Delivered)
            return "COD_LIFECYCLE_STALE";
        return null;
    }

    internal static async Task<(CarrierCollectionEvidence? Evidence, string? Code)> RevalidateAsync(
        ApplicationDbContext db, Order order, Guid? evidenceId, Guid? shipmentId, CancellationToken ct)
    {
        var evidence = await db.CarrierCollectionEvidence.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(e =>
            e.TenantId == order.TenantId && e.OrderId == order.Id && e.AdmissionOutcome == "Qualified" &&
            (!evidenceId.HasValue || e.Id == evidenceId) && (!shipmentId.HasValue || e.ShipmentId == shipmentId), ct);
        if (evidence == null) return (null, "COD_CARRIER_EVIDENCE_REQUIRED");
        var priorReview = await db.CarrierCollectionEvidence.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(e =>
            e.TenantId == order.TenantId && e.ReviewedEvidenceId == evidence.Id && e.AdmissionOutcome == "RevalidationReview", ct);
        if (priorReview != null) return (evidence, priorReview.AdmissionCode); // reverting mutable facts is not a reconciliation authority
        if (await db.CarrierCollectionEvidence.IgnoreQueryFilters().AnyAsync(e => e.TenantId == order.TenantId && e.OrderId == order.Id && e.AdmissionOutcome == "Review", ct))
            return (evidence, "COD_OBSERVATION_CONFLICT_REVIEW");
        var shipment = await (db.Database.IsNpgsql()
            ? db.Shipments.FromSqlInterpolated($"SELECT * FROM \"Shipments\" WHERE \"Id\"={evidence.ShipmentId} AND \"TenantId\"={order.TenantId} FOR UPDATE").IgnoreQueryFilters()
            : db.Shipments.IgnoreQueryFilters()).AsNoTracking().SingleOrDefaultAsync(s => s.Id == evidence.ShipmentId && s.TenantId == order.TenantId, ct);
        var config = await (db.Database.IsNpgsql()
            ? db.CarrierConfigurations.FromSqlInterpolated($"SELECT * FROM \"CarrierConfigurations\" WHERE \"Id\"={evidence.ConfigurationId} AND \"TenantId\"={order.TenantId} FOR UPDATE").IgnoreQueryFilters()
            : db.CarrierConfigurations.IgnoreQueryFilters()).AsNoTracking().SingleOrDefaultAsync(c => c.Id == evidence.ConfigurationId && c.TenantId == order.TenantId, ct);
        if (shipment == null || config == null) return (evidence, "COD_TARGET_UNAVAILABLE");
        var binding = await db.CarrierCodShipmentBindings.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(b => b.Id == evidence.ShipmentBindingId && b.TenantId == order.TenantId, ct);
        var code = await QualifyAsync(db, order, shipment, config, binding,
            new(evidence.DeliveryReference, 45, 10, evidence.ProviderEventAtUtc, evidence.ObservedAmount, "COD_AMOUNT_OBSERVED", null,
                evidence.SourceContract, evidence.ObservationFingerprint), ct);
        if (code == null && (evidence.ConfigurationId != binding!.ConfigurationId || evidence.AccountId != binding.AccountId ||
            evidence.Environment != binding.Environment || evidence.Currency != binding.Currency || evidence.TrackingNumber != binding.TrackingNumber))
            code = "COD_EVIDENCE_BINDING_MISMATCH";
        if (code == null && (evidence.ProviderEventAtUtc > evidence.ReceivedAtUtc.AddMinutes(5) || evidence.ProviderEventAtUtc < binding.CreatedAt.AddMinutes(-5)))
            code = "COD_EVENT_TIME_UNSUPPORTED";
        return (evidence, code);
    }

    internal static async Task StageReviewAsync(ApplicationDbContext db, CarrierCollectionEvidence evidence, string code, CancellationToken ct)
    {
        var fingerprint = Hash($"financial-review:{evidence.Id:D}:{code}");
        if (await db.CarrierCollectionEvidence.IgnoreQueryFilters().AnyAsync(e => e.TenantId == evidence.TenantId && e.CarrierCode == evidence.CarrierCode && e.ObservationFingerprint == fingerprint, ct)) return;
        db.CarrierCollectionEvidence.Add(new CarrierCollectionEvidence
        {
            Id = Guid.NewGuid(), TenantId = evidence.TenantId, OrderId = evidence.OrderId, ShipmentId = evidence.ShipmentId,
            ConfigurationId = evidence.ConfigurationId, ShipmentBindingId = evidence.ShipmentBindingId, ReviewedEvidenceId = evidence.Id,
            CarrierCode = evidence.CarrierCode, AccountId = evidence.AccountId, Environment = evidence.Environment,
            TrackingNumber = evidence.TrackingNumber, DeliveryReference = evidence.DeliveryReference,
            ObservedAmount = evidence.ObservedAmount, Currency = evidence.Currency, ProviderEventAtUtc = evidence.ProviderEventAtUtc,
            ReceivedAtUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, SourceContract = evidence.SourceContract,
            TrustMethod = evidence.TrustMethod, PayloadHash = evidence.PayloadHash, ObservationFingerprint = fingerprint,
            AdmissionOutcome = "RevalidationReview", AdmissionCode = code, AuditIdentity = "system:carrier-collection-financial-admission"
        });
    }
}
