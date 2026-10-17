using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Shipping;

public partial class CarrierShipmentService
{
    private async Task CaptureCollectionBindingAsync(Shipment shipment, CreateShipmentResult result,
        CarrierCredentialsResolution resolution, CancellationToken ct)
    {
        if (shipment.CarrierCode != "bosta" || !shipment.IsCashOnDelivery || resolution.Configuration == null ||
            resolution.Credentials == null || string.IsNullOrWhiteSpace(resolution.Credentials.ApiKey) ||
            string.IsNullOrWhiteSpace(shipment.TrackingNumber)) return;
        var config = await _dbContext.CarrierConfigurations.AsNoTracking().SingleOrDefaultAsync(c =>
            c.Id == resolution.Configuration.Id && c.TenantId == shipment.TenantId && !c.IsDeleted, ct);
        if (config == null || config.CarrierCode != shipment.CarrierCode || !config.IsEnabled || !config.SupportsCod ||
            config.UseSandbox != resolution.Credentials.UseSandbox || config.Configuration != resolution.Configuration.Configuration) return;
        if (!string.IsNullOrWhiteSpace(config.AccountId) && config.AccountId != resolution.Credentials.AccountId) return;
        var accountScope = CarrierCollectionMoneyAuthority.AccountScope(config);
        if (accountScope == null) return;
        var currency = CarrierCollectionMoneyAuthority.ConfiguredCurrency(resolution.Credentials.ExtraConfig);
        if (currency == null || !await _dbContext.Currencies.AnyAsync(c => c.Code == currency && c.DecimalPlaces <= 6, ct)) return;
        string? deliveryReference = null;
        try
        {
            using var metadata = JsonDocument.Parse(result.CarrierMetadata ?? "{}");
            if (metadata.RootElement.ValueKind == JsonValueKind.Object && metadata.RootElement.TryGetProperty("bostaId", out var id) && id.ValueKind == JsonValueKind.String)
                deliveryReference = id.GetString();
        }
        catch (JsonException) { }
        // Only a supplied provider delivery reference qualifies, never manufacture it from a tracking number.
        deliveryReference ??= result.ExternalReferenceId != result.TrackingNumber ? result.ExternalReferenceId : null;
        if (string.IsNullOrWhiteSpace(deliveryReference) || deliveryReference.Length > 200 || accountScope.Length > 200) return;
        if (await _dbContext.CarrierCodShipmentBindings.AnyAsync(b => b.TenantId == shipment.TenantId && b.ShipmentId == shipment.Id, ct)) return;
        _dbContext.CarrierCodShipmentBindings.Add(new CarrierCodShipmentBinding
        {
            Id = Guid.NewGuid(), TenantId = shipment.TenantId, OrderId = shipment.OrderId, ShipmentId = shipment.Id,
            CarrierCode = shipment.CarrierCode, ConfigurationId = config.Id, AccountId = accountScope,
            Environment = config.UseSandbox ? "Sandbox" : "Production", Currency = currency,
            TrackingNumber = shipment.TrackingNumber!, DeliveryReference = deliveryReference,
            ExternalReferenceId = shipment.ExternalReferenceId, CreatedAt = DateTime.UtcNow,
            Fingerprint = CarrierCollectionMoneyAuthority.BindingFingerprint(config, shipment, accountScope, currency, deliveryReference)
        });
    }

    private async Task<WebhookProcessResult> ProcessObservedMoneyWebhookAsync(Shipment initial, WebhookParseResult parsed,
        string payload, string? signature, string? sourceIp, CancellationToken ct)
    {
        var receivedAt = DateTime.UtcNow;
        var observation = parsed.MoneyObservation!;
        using var authorityScope = CarrierMoneyPersistenceAuthority.Begin();
        // This narrow observed-money path commits log/evidence/state/event together. No pre-transaction Processing acknowledgement.
        await using var tx = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        var authority = await ReloadCarrierPersistenceAuthorityAsync(initial.TenantId, initial.Id, initial.OrderId, ct);
        if (authority == null || authority.Shipment.TrackingNumber != parsed.TrackingNumber || authority.Shipment.CarrierCode != "bosta")
            return new() { Success = false, StatusCode = 409, ErrorCode = "WEBHOOK_TARGET_CHANGED" };
        var shipment = authority.Shipment;
        var order = authority.Order;
        var config = await (_dbContext.Database.IsNpgsql()
            ? _dbContext.CarrierConfigurations.FromSqlInterpolated($"SELECT * FROM \"CarrierConfigurations\" WHERE \"TenantId\"={shipment.TenantId} AND \"CarrierCode\"={shipment.CarrierCode} AND NOT \"IsDeleted\" FOR UPDATE")
            : _dbContext.CarrierConfigurations.Where(c => !c.IsDeleted))
            .SingleOrDefaultAsync(c => c.TenantId == shipment.TenantId && c.CarrierCode == shipment.CarrierCode, ct);
        if (config == null) return new() { Success = false, StatusCode = 403, ErrorCode = "WEBHOOK_CONFIGURATION_INVALID" };
        var trust = await _carrierIntegrationAuthority.ResolveWebhookTrustForMutationAsync(shipment.TenantId, shipment.CarrierCode, ct);
        var validated = ValidateWebhookTrust(payload, signature, sourceIp, trust);
        if (!trust.Succeeded || !validated.Success)
            return new() { Success = false, StatusCode = 401, ErrorCode = "WEBHOOK_SIGNATURE_INVALID" };
        var eventId = "observation:" + observation.Fingerprint;
        if (await _dbContext.ShippingWebhookLogs.IgnoreQueryFilters().AnyAsync(l => l.TenantId == shipment.TenantId && l.CarrierCode == shipment.CarrierCode && l.EventId == eventId, ct))
            return new() { Success = true, StatusCode = 200, ShipmentFound = true, ShipmentId = shipment.Id, IsDuplicate = true };
        var binding = await _dbContext.CarrierCodShipmentBindings.AsNoTracking().SingleOrDefaultAsync(b => b.TenantId == shipment.TenantId && b.ShipmentId == shipment.Id, ct);
        var previous = shipment.Status;
        var canApplyStatus = order.AllowsCarrierWebhooks && order.FulfillmentCarrierCode == shipment.CarrierCode &&
            observation.DeliveryType == 10 && observation.ConfirmedDelivery != false &&
            (binding == null ? shipment.ExternalReferenceId == observation.DeliveryReference :
                binding.DeliveryReference == observation.DeliveryReference && binding.TrackingNumber == shipment.TrackingNumber &&
                binding.ExternalReferenceId == shipment.ExternalReferenceId && binding.ConfigurationId == config.Id &&
                binding.AccountId == CarrierCollectionMoneyAuthority.AccountScope(config) &&
                binding.Fingerprint == CarrierCollectionMoneyAuthority.BindingFingerprint(config, shipment, binding.AccountId, binding.Currency, binding.DeliveryReference) &&
                binding.Environment == (config.UseSandbox ? "Sandbox" : "Production")) &&
            (previous == parsed.Status || IsValidStatusProgression(previous, parsed.Status));
        var changed = canApplyStatus && previous != parsed.Status;
        if (changed)
        {
            shipment.Status = parsed.Status;
            UpdateStatusTimestamps(shipment, parsed.Status);
            var context = await PrepareShipmentAuthorityContextAsync(shipment, parsed.Status, "webhook", ct);
            await SynchronizeFulfillmentAuthorityAsync(shipment, parsed.Status, "webhook", context, ct);
            await EmitStatusChangeEventAsync(shipment, previous, parsed.Status, "webhook", context, ct);
        }
        var payloadHash = ComputePayloadHash(payload);
        string? reviewCode = null;
        if (observation.State == 45)
        {
            reviewCode = await CarrierCollectionMoneyAuthority.QualifyAsync(_dbContext, order, shipment, config, binding, observation, ct);
            if (reviewCode == null && observation.ProviderEventAtUtc < binding!.CreatedAt.AddMinutes(-5)) reviewCode = "COD_EVENT_TIME_UNSUPPORTED";
            if (reviewCode == null && await _dbContext.CarrierCollectionEvidence.AnyAsync(e => e.TenantId == order.TenantId && e.OrderId == order.Id && e.AdmissionOutcome == "Qualified", ct))
                reviewCode = "COD_COLLECTION_CONFLICT";
            if (reviewCode == null && await _dbContext.CarrierCollectionEvidence.AnyAsync(e => e.TenantId == order.TenantId && e.OrderId == order.Id && e.AdmissionOutcome == "Review", ct))
                reviewCode = "COD_OBSERVATION_CONFLICT_REVIEW";
            var evidence = new CarrierCollectionEvidence
            {
                Id = Guid.NewGuid(), TenantId = shipment.TenantId, OrderId = order.Id, ShipmentId = shipment.Id,
                ConfigurationId = config.Id, ShipmentBindingId = binding?.Id, CarrierCode = shipment.CarrierCode,
                AccountId = CarrierCollectionMoneyAuthority.AccountScope(config), Environment = config.UseSandbox ? "Sandbox" : "Production",
                TrackingNumber = parsed.TrackingNumber, DeliveryReference = observation.DeliveryReference,
                ObservedAmount = observation.Amount, Currency = binding?.Currency,
                ProviderEventAtUtc = observation.ProviderEventAtUtc, ReceivedAtUtc = receivedAt, CreatedAt = receivedAt,
                SourceContract = observation.Contract, TrustMethod = trust.ValidationMethod, PayloadHash = payloadHash,
                ObservationFingerprint = observation.Fingerprint, AdmissionOutcome = reviewCode == null ? "Qualified" : "Review",
                AdmissionCode = reviewCode ?? "COD_WHOLE_ORDER_QUALIFIED", AuditIdentity = "system:authenticated-carrier-webhook"
            };
            _dbContext.CarrierCollectionEvidence.Add(evidence);
            if (reviewCode == null)
            {
                shipment.CodCollected = true;
                shipment.CodCollectedAtUtc = observation.ProviderEventAtUtc;
                await PublishShipmentCodCollectedEventAsync(shipment, ct, evidence);
            }
        }
        _dbContext.ShippingWebhookLogs.Add(new ShippingWebhookLog
        {
            Id = Guid.NewGuid(), TenantId = shipment.TenantId, CarrierCode = shipment.CarrierCode,
            EventId = eventId, PayloadHash = payloadHash, EventType = $"bosta:state:{observation.State}",
            ShipmentId = shipment.Id, OrderId = order.Id, TrackingNumber = parsed.TrackingNumber,
            PayloadSummary = "Official delivery observation; payload retained as SHA-256 only.",
            SignatureVerified = true, ValidationMethod = trust.ValidationMethod,
            ProcessingStatus = WebhookProcessingStatus.Completed, ProcessingError = reviewCode, ProcessedAt = receivedAt
        });
        await _dbContext.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return new() { Success = true, StatusCode = 200, ShipmentFound = true, ShipmentId = shipment.Id, StatusChanged = changed };
    }
}
