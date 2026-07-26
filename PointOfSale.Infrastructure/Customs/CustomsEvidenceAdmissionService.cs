using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.DTOs.Customs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Customs;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Customs;

public sealed class CustomsEvidenceAdmissionService : ICustomsEvidenceAdmissionService
{
    private static readonly HashSet<string> SupportedWeightUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "kg",
        "g",
        "lb",
        "oz"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CustomsEvidenceAdmissionService> _logger;

    public CustomsEvidenceAdmissionService(
        ApplicationDbContext dbContext,
        ILogger<CustomsEvidenceAdmissionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CustomsEvidenceAdmissionResult> AdmitEvidenceAsync(
        CustomsEvidenceAdmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var failure = await ValidateEvidenceAdmissionAsync(request, cancellationToken);
        if (failure is not null)
        {
            await AddAuditAsync(
                request.TenantId,
                CustomsAdmissionAuditAction.EvidenceRejected,
                request.Actor,
                failure,
                request.CorrelationId,
                productId: request.ProductId,
                productVariantId: request.ProductVariantId,
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new CustomsEvidenceAdmissionResult(false, null, null, null, failure, failure);
        }

        var normalized = NormalizeEvidenceRequest(request);
        var fingerprint = ComputeSha256(JsonSerializer.Serialize(normalized));
        var now = DateTime.UtcNow;

        var evidence = await _dbContext.CustomsEvidence
            .Where(candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.SourceKind == request.SourceKind &&
                candidate.SourceId == request.SourceId &&
                candidate.Status == CustomsEvidenceStatus.Admitted)
            .OrderByDescending(candidate => candidate.CurrentVersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var versionId = Guid.NewGuid();
        if (evidence is null)
        {
            evidence = new CustomsEvidence
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                SourceKind = request.SourceKind,
                SourceId = request.SourceId,
                ProductId = request.ProductId,
                ProductVariantId = request.ProductVariantId,
                Status = CustomsEvidenceStatus.Admitted,
                CurrentVersionId = versionId,
                CurrentVersionNumber = 1,
                AdmittedAtUtc = now,
                AdmittedBy = request.Actor,
                CorrelationId = request.CorrelationId,
                CreatedAt = now
            };
            _dbContext.CustomsEvidence.Add(evidence);
        }
        else
        {
            evidence.CurrentVersionId = versionId;
            evidence.CurrentVersionNumber += 1;
            evidence.AdmittedAtUtc = now;
            evidence.AdmittedBy = request.Actor;
            evidence.CorrelationId = request.CorrelationId;
        }

        var version = new CustomsEvidenceVersion
        {
            Id = versionId,
            TenantId = request.TenantId,
            CustomsEvidenceId = evidence.Id,
            VersionNumber = evidence.CurrentVersionNumber,
            HsCode = normalized.HsCode,
            CommodityDescription = normalized.CommodityDescription,
            CountryOfOriginIso2 = normalized.CountryOfOriginIso2,
            CountryOfManufactureIso2 = normalized.CountryOfManufactureIso2,
            DeclaredValue = normalized.DeclaredValue,
            Currency = normalized.Currency,
            NetWeight = normalized.NetWeight,
            WeightUnit = normalized.WeightUnit,
            QuantityUnit = normalized.QuantityUnit,
            DangerousGoods = normalized.DangerousGoods,
            ExportClassification = normalized.ExportClassification,
            ValueSource = normalized.ValueSource,
            SourceEvidenceJson = normalized.SourceEvidenceJson,
            EvidenceFingerprintSha256 = fingerprint,
            AdmittedAtUtc = now,
            AdmittedBy = request.Actor,
            CorrelationId = request.CorrelationId,
            CreatedAt = now
        };
        _dbContext.CustomsEvidenceVersions.Add(version);
        _dbContext.CustomsEvidenceFingerprints.Add(new CustomsEvidenceFingerprint
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            CustomsEvidenceId = evidence.Id,
            CustomsEvidenceVersionId = version.Id,
            FingerprintSha256 = fingerprint,
            CreatedAtUtc = now,
            CreatedAt = now
        });
        _dbContext.CustomsEvidenceHistory.Add(new CustomsEvidenceHistory
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            CustomsEvidenceId = evidence.Id,
            CustomsEvidenceVersionId = version.Id,
            VersionNumber = version.VersionNumber,
            Action = "EvidenceVersionCreated",
            Reason = "BackendCustomsAdmission",
            Actor = request.Actor,
            CorrelationId = request.CorrelationId,
            OccurredAtUtc = now,
            CreatedAt = now
        });
        await AddAuditAsync(
            request.TenantId,
            CustomsAdmissionAuditAction.EvidenceAdmitted,
            request.Actor,
            "Customs evidence admitted.",
            request.CorrelationId,
            customsEvidenceId: evidence.Id,
            customsEvidenceVersionId: version.Id,
            productId: request.ProductId,
            productVariantId: request.ProductVariantId,
            fingerprint: fingerprint,
            cancellationToken: cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CustomsEvidenceAdmissionResult(true, evidence.Id, version.Id, version.VersionNumber, null, null);
    }

    public async Task<ShipmentCustomsSnapshotResult> CreateOrReuseShipmentSnapshotAsync(
        ShipmentCustomsSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await LoadSnapshotAsync(request.TenantId, request.ShipmentId, cancellationToken);
        if (existing is not null)
        {
            var existingDto = ToDto(existing);
            await AddAuditAsync(
                request.TenantId,
                CustomsAdmissionAuditAction.ShipmentSnapshotReused,
                request.Actor,
                "Recovery authority reused immutable shipment customs snapshot.",
                request.CorrelationId,
                shipmentSnapshotId: existing.Id,
                shipmentId: request.ShipmentId,
                orderId: request.OrderId,
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ShipmentCustomsSnapshotResult(true, existing.Id, existingDto, null, null);
        }

        var initialFailure = await ValidateSnapshotRequestAsync(request, cancellationToken);
        if (initialFailure is not null)
        {
            await AuditSnapshotFailureAsync(request, initialFailure, cancellationToken);
            return new ShipmentCustomsSnapshotResult(false, null, null, initialFailure, initialFailure);
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .FirstOrDefaultAsync(
                order => order.Id == request.OrderId &&
                         order.TenantId == request.TenantId &&
                         !order.IsDeleted,
                cancellationToken);
        var shipment = await _dbContext.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                shipment => shipment.Id == request.ShipmentId &&
                            shipment.TenantId == request.TenantId &&
                            shipment.OrderId == request.OrderId &&
                            !shipment.IsDeleted,
                cancellationToken);
        if (order is null || shipment is null)
        {
            const string failure = "MALFORMED_EVIDENCE";
            await AuditSnapshotFailureAsync(request, failure, cancellationToken);
            return new ShipmentCustomsSnapshotResult(false, null, null, failure, failure);
        }

        if (order.Items.Count == 0 || order.Items.Any(item => item.Quantity <= 0))
        {
            const string failure = "INVALID_QUANTITY";
            await AuditSnapshotFailureAsync(request, failure, cancellationToken);
            return new ShipmentCustomsSnapshotResult(false, null, null, failure, failure);
        }

        var evidenceRows = await LoadCurrentEvidenceForOrderAsync(request.TenantId, order.Items, cancellationToken);
        var snapshotId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var lines = new List<ShipmentCustomsSnapshotLine>();
        foreach (var item in order.Items.OrderBy(item => item.Id))
        {
            var evidence = ResolveEvidenceForOrderItem(item, evidenceRows);
            if (evidence is null)
            {
                const string failure = "CUSTOMS_EVIDENCE_NOT_ADMITTED";
                await AuditSnapshotFailureAsync(request, failure, cancellationToken);
                return new ShipmentCustomsSnapshotResult(false, null, null, failure, failure);
            }

            lines.Add(new ShipmentCustomsSnapshotLine
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                ShipmentCustomsSnapshotId = snapshotId,
                ShipmentId = request.ShipmentId,
                OrderId = request.OrderId,
                OrderItemId = item.Id,
                ProductId = item.ProductId,
                ProductVariantId = item.VariantId,
                CustomsEvidenceId = evidence.CustomsEvidenceId,
                CustomsEvidenceVersionId = evidence.Id,
                EvidenceVersionNumber = evidence.VersionNumber,
                HsCode = evidence.HsCode,
                CommodityDescription = evidence.CommodityDescription,
                CountryOfOriginIso2 = evidence.CountryOfOriginIso2,
                CountryOfManufactureIso2 = evidence.CountryOfManufactureIso2,
                DeclaredValue = evidence.DeclaredValue,
                Currency = evidence.Currency,
                Quantity = item.Quantity,
                QuantityUnit = evidence.QuantityUnit,
                NetWeight = evidence.NetWeight * item.Quantity,
                NetWeightUnit = evidence.WeightUnit,
                DangerousGoods = evidence.DangerousGoods,
                ExportClassification = evidence.ExportClassification,
                SourceEvidenceFingerprintSha256 = evidence.EvidenceFingerprintSha256,
                CreatedAt = now
            });
        }

        var dto = new CarrierNeutralCustomsDto(
            snapshotId,
            request.ShipmentId,
            request.OrderId,
            1,
            1,
            request.OriginCountryIso2.Trim().ToUpperInvariant(),
            request.DestinationCountryIso2.Trim().ToUpperInvariant(),
            request.ShipmentGrossWeight,
            request.ShipmentGrossWeightUnit.Trim().ToLowerInvariant(),
            lines.Select(ToDtoLine).ToArray());
        var snapshotJson = JsonSerializer.Serialize(dto);
        var fingerprint = ComputeSha256(snapshotJson);
        var snapshot = new ShipmentCustomsSnapshot
        {
            Id = snapshotId,
            TenantId = request.TenantId,
            ShipmentId = request.ShipmentId,
            OrderId = request.OrderId,
            SnapshotVersion = 1,
            CarrierNeutralDtoVersion = 1,
            OriginCountryIso2 = dto.OriginCountryIso2,
            DestinationCountryIso2 = dto.DestinationCountryIso2,
            ShipmentGrossWeight = request.ShipmentGrossWeight,
            ShipmentGrossWeightUnit = dto.ShipmentGrossWeightUnit,
            SnapshotFingerprintSha256 = fingerprint,
            SnapshotJson = snapshotJson,
            IsRegistrationFrozen = true,
            RegistrationBeganAtUtc = now,
            CorrelationId = request.CorrelationId,
            CreatedAt = now,
            Lines = lines
        };

        _dbContext.ShipmentCustomsSnapshots.Add(snapshot);
        await AddAuditAsync(
            request.TenantId,
            CustomsAdmissionAuditAction.ShipmentSnapshotCreated,
            request.Actor,
            "Shipment customs snapshot created from admitted evidence.",
            request.CorrelationId,
            shipmentSnapshotId: snapshot.Id,
            shipmentId: request.ShipmentId,
            orderId: request.OrderId,
            fingerprint: fingerprint,
            cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new ShipmentCustomsSnapshotResult(true, snapshot.Id, dto, null, null);
    }

    public async Task<ShipmentCustomsSnapshotResult> RecoverShipmentSnapshotAsync(
        Guid tenantId,
        Guid shipmentId,
        string actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadSnapshotAsync(tenantId, shipmentId, cancellationToken);
        if (snapshot is null || snapshot.Lines.Count == 0)
        {
            await AddAuditAsync(
                tenantId,
                CustomsAdmissionAuditAction.RecoveryRejected,
                actor,
                "Recovery rejected because SQL shipment customs snapshot is missing or incomplete.",
                correlationId,
                shipmentId: shipmentId,
                failureCode: "CUSTOMS_SNAPSHOT_MISSING",
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ShipmentCustomsSnapshotResult(false, null, null, "CUSTOMS_SNAPSHOT_MISSING", "CUSTOMS_SNAPSHOT_MISSING");
        }

        var dto = ToDto(snapshot);
        await AddAuditAsync(
            tenantId,
            CustomsAdmissionAuditAction.ShipmentSnapshotReused,
            actor,
            "Recovery reused SQL shipment customs snapshot only.",
            correlationId,
            shipmentSnapshotId: snapshot.Id,
            shipmentId: shipmentId,
            orderId: snapshot.OrderId,
            cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new ShipmentCustomsSnapshotResult(true, snapshot.Id, dto, null, null);
    }

    private async Task<string?> ValidateEvidenceAdmissionAsync(
        CustomsEvidenceAdmissionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.SourceId == Guid.Empty)
        {
            return "MALFORMED_EVIDENCE";
        }

        if (string.IsNullOrWhiteSpace(request.HsCode))
        {
            return "MISSING_HS_CODE";
        }

        if (string.IsNullOrWhiteSpace(request.CommodityDescription))
        {
            return "MISSING_COMMODITY";
        }

        if (!IsIso2(request.CountryOfOriginIso2))
        {
            return "MISSING_COUNTRY_OF_ORIGIN";
        }

        if (request.DeclaredValue <= 0)
        {
            return "MISSING_DECLARED_VALUE";
        }

        if (!IsCurrency(request.Currency))
        {
            return "MISSING_CURRENCY";
        }

        if (request.NetWeight <= 0 || !SupportedWeightUnits.Contains(request.WeightUnit))
        {
            return "INVALID_WEIGHT";
        }

        if (string.IsNullOrWhiteSpace(request.QuantityUnit))
        {
            return "INVALID_UNITS";
        }

        try
        {
            JsonDocument.Parse(request.SourceEvidenceJson);
        }
        catch (JsonException)
        {
            return "MALFORMED_EVIDENCE";
        }

        var countryIso2 = request.CountryOfOriginIso2.Trim().ToUpperInvariant();
        var supportedCountry = await _dbContext.GeographicCountryProfiles
            .AsNoTracking()
            .AnyAsync(profile => profile.Iso2 == countryIso2 && profile.IsActive, cancellationToken);
        return supportedCountry ? null : "UNSUPPORTED_COUNTRY";
    }

    private async Task<string?> ValidateSnapshotRequestAsync(
        ShipmentCustomsSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty ||
            request.ShipmentId == Guid.Empty ||
            request.OrderId == Guid.Empty)
        {
            return "MALFORMED_EVIDENCE";
        }

        if (!IsIso2(request.OriginCountryIso2) || !IsIso2(request.DestinationCountryIso2))
        {
            return "UNSUPPORTED_COUNTRY";
        }

        if (request.ShipmentGrossWeight <= 0 || !SupportedWeightUnits.Contains(request.ShipmentGrossWeightUnit))
        {
            return "INVALID_WEIGHT";
        }

        var origin = request.OriginCountryIso2.Trim().ToUpperInvariant();
        var destination = request.DestinationCountryIso2.Trim().ToUpperInvariant();
        var supportedCountries = await _dbContext.GeographicCountryProfiles
            .AsNoTracking()
            .Where(profile => profile.IsActive && (profile.Iso2 == origin || profile.Iso2 == destination))
            .Select(profile => profile.Iso2)
            .ToListAsync(cancellationToken);
        return supportedCountries.Contains(origin) && supportedCountries.Contains(destination)
            ? null
            : "UNSUPPORTED_COUNTRY";
    }

    private async Task<List<CustomsEvidenceVersion>> LoadCurrentEvidenceForOrderAsync(
        Guid tenantId,
        IEnumerable<OrderItem> orderItems,
        CancellationToken cancellationToken)
    {
        var productIds = orderItems.Select(item => item.ProductId).Distinct().ToArray();
        var variantIds = orderItems.Where(item => item.VariantId.HasValue).Select(item => item.VariantId!.Value).Distinct().ToArray();
        return await _dbContext.CustomsEvidenceVersions
            .AsNoTracking()
            .Include(version => version.Evidence)
            .Where(version =>
                version.TenantId == tenantId &&
                version.Evidence.Status == CustomsEvidenceStatus.Published &&
                version.Evidence.CurrentVersionId == version.Id &&
                (productIds.Contains(version.Evidence.ProductId ?? Guid.Empty) ||
                 variantIds.Contains(version.Evidence.ProductVariantId ?? Guid.Empty)))
            .Where(version =>
                _dbContext.CustomsEvidenceApprovals.Any(approval =>
                    approval.TenantId == tenantId &&
                    approval.CustomsEvidenceId == version.CustomsEvidenceId &&
                    approval.CustomsEvidenceVersionId == version.Id &&
                    approval.ValidationChecksumSha256 == version.EvidenceFingerprintSha256 &&
                    !approval.IsDeleted) &&
                _dbContext.CustomsEvidencePublications.Any(publication =>
                    publication.TenantId == tenantId &&
                    publication.CustomsEvidenceId == version.CustomsEvidenceId &&
                    publication.CustomsEvidenceVersionId == version.Id &&
                    publication.ValidationChecksumSha256 == version.EvidenceFingerprintSha256 &&
                    publication.Action == CustomsPublicationAction.Published &&
                    !publication.IsDeleted))
            .ToListAsync(cancellationToken);
    }

    private static CustomsEvidenceVersion? ResolveEvidenceForOrderItem(
        OrderItem item,
        IReadOnlyCollection<CustomsEvidenceVersion> evidenceRows)
    {
        if (item.VariantId.HasValue)
        {
            var variantEvidence = evidenceRows.FirstOrDefault(evidence => evidence.Evidence.ProductVariantId == item.VariantId);
            if (variantEvidence is not null)
            {
                return variantEvidence;
            }
        }

        return evidenceRows.FirstOrDefault(evidence => evidence.Evidence.ProductId == item.ProductId);
    }

    private async Task<ShipmentCustomsSnapshot?> LoadSnapshotAsync(
        Guid tenantId,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ShipmentCustomsSnapshots
            .AsNoTracking()
            .Include(snapshot => snapshot.Lines)
            .FirstOrDefaultAsync(
                snapshot => snapshot.TenantId == tenantId &&
                            snapshot.ShipmentId == shipmentId &&
                            !snapshot.IsDeleted,
                cancellationToken);
    }

    private static CarrierNeutralCustomsDto ToDto(ShipmentCustomsSnapshot snapshot)
    {
        return new CarrierNeutralCustomsDto(
            snapshot.Id,
            snapshot.ShipmentId,
            snapshot.OrderId,
            snapshot.SnapshotVersion,
            snapshot.CarrierNeutralDtoVersion,
            snapshot.OriginCountryIso2,
            snapshot.DestinationCountryIso2,
            snapshot.ShipmentGrossWeight,
            snapshot.ShipmentGrossWeightUnit,
            snapshot.Lines.OrderBy(line => line.OrderItemId).Select(ToDtoLine).ToArray());
    }

    private static CarrierNeutralCustomsLineDto ToDtoLine(ShipmentCustomsSnapshotLine line)
    {
        return new CarrierNeutralCustomsLineDto(
            line.OrderItemId,
            line.ProductId,
            line.ProductVariantId,
            line.CustomsEvidenceId,
            line.CustomsEvidenceVersionId,
            line.EvidenceVersionNumber,
            line.HsCode,
            line.CommodityDescription,
            line.CountryOfOriginIso2,
            line.CountryOfManufactureIso2,
            line.DeclaredValue,
            line.Currency,
            line.Quantity,
            line.QuantityUnit,
            line.NetWeight,
            line.NetWeightUnit,
            line.DangerousGoods,
            line.ExportClassification);
    }

    private async Task AuditSnapshotFailureAsync(
        ShipmentCustomsSnapshotRequest request,
        string failure,
        CancellationToken cancellationToken)
    {
        await AddAuditAsync(
            request.TenantId,
            CustomsAdmissionAuditAction.ShipmentSnapshotRejected,
            request.Actor,
            failure,
            request.CorrelationId,
            shipmentId: request.ShipmentId,
            orderId: request.OrderId,
            failureCode: failure,
            cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AddAuditAsync(
        Guid tenantId,
        CustomsAdmissionAuditAction action,
        string actor,
        string reason,
        string correlationId,
        Guid? customsEvidenceId = null,
        Guid? customsEvidenceVersionId = null,
        Guid? shipmentSnapshotId = null,
        Guid? shipmentId = null,
        Guid? orderId = null,
        Guid? productId = null,
        Guid? productVariantId = null,
        string? fingerprint = null,
        string? failureCode = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        _dbContext.CustomsAdmissionAudit.Add(new CustomsAdmissionAudit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Action = action,
            CustomsEvidenceId = customsEvidenceId,
            CustomsEvidenceVersionId = customsEvidenceVersionId,
            ShipmentCustomsSnapshotId = shipmentSnapshotId,
            ShipmentId = shipmentId,
            OrderId = orderId,
            ProductId = productId,
            ProductVariantId = productVariantId,
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
            Reason = reason,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            SourceEvidenceFingerprintSha256 = fingerprint,
            FailureCode = failureCode,
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static NormalizedEvidenceRequest NormalizeEvidenceRequest(CustomsEvidenceAdmissionRequest request)
    {
        return new NormalizedEvidenceRequest(
            request.HsCode.Trim().ToUpperInvariant(),
            request.CommodityDescription.Trim(),
            request.CountryOfOriginIso2.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(request.CountryOfManufactureIso2) ? null : request.CountryOfManufactureIso2.Trim().ToUpperInvariant(),
            request.DeclaredValue,
            request.Currency.Trim().ToUpperInvariant(),
            request.NetWeight,
            request.WeightUnit.Trim().ToLowerInvariant(),
            request.QuantityUnit.Trim(),
            request.DangerousGoods,
            string.IsNullOrWhiteSpace(request.ExportClassification) ? null : request.ExportClassification.Trim(),
            request.ValueSource.Trim(),
            request.SourceEvidenceJson.Trim());
    }

    private static bool IsIso2(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length == 2 &&
        value.Trim().All(char.IsLetter);

    private static bool IsCurrency(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length == 3 &&
        value.Trim().All(char.IsLetter);

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record NormalizedEvidenceRequest(
        string HsCode,
        string CommodityDescription,
        string CountryOfOriginIso2,
        string? CountryOfManufactureIso2,
        decimal DeclaredValue,
        string Currency,
        decimal NetWeight,
        string WeightUnit,
        string QuantityUnit,
        bool? DangerousGoods,
        string? ExportClassification,
        string ValueSource,
        string SourceEvidenceJson);
}
