using System.Text.Json;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>Restores source measurements without MyDHL limits or rounding.</summary>
public static class FedExParcelMeasurements
{
    public static ParcelEvidence Admit(ParcelMeasurement input, Shipment shipment, Order order, Guid actor)
    {
        if (input == null) throw new ArgumentException("PARCEL_MEASUREMENTS_REQUIRED");
        if (input.PackageCount != 1 || shipment.PackageCount != 1) throw new ArgumentException("PARCEL_SINGLE_PACKAGE_REQUIRED");
        if (actor == Guid.Empty || input.PieceReference is not { Length: > 0 and <= 35 } ||
            input.PieceReference.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("PARCEL_PIECE_REFERENCE_INVALID");
        if (shipment.Id == Guid.Empty || shipment.TenantId == Guid.Empty || order.Id != shipment.OrderId || order.TenantId != shipment.TenantId ||
            order.IsDeleted || shipment.IsDeleted || order.Items.Count == 0 ||
            order.Items.Any(i => i.OrderId != order.Id || i.Id == Guid.Empty || i.ProductId == Guid.Empty || i.Quantity <= 0))
            throw new ArgumentException("PARCEL_ORDER_CONTENTS_INVALID");
        var dimensionFactor = input.DimensionUnit switch { "cm" => 1m, "mm" => 0.1m, "in" => 2.54m, _ => throw new ArgumentException("PARCEL_DIMENSION_UNIT_UNSUPPORTED") };
        var weightFactor = input.WeightUnit switch { "kg" => 1m, "g" => 0.001m, "lb" => 0.45359237m, "oz" => 0.028349523125m, _ => throw new ArgumentException("PARCEL_WEIGHT_UNIT_UNSUPPORTED") };
        try
        {
            var length = input.Length * dimensionFactor;
            var width = input.Width * dimensionFactor;
            var height = input.Height * dimensionFactor;
            var weight = input.GrossWeight * weightFactor;
            if (new[] { length, width, height }.Any(v => v <= 0 || v > 999 || decimal.Truncate(v) != v) ||
                weight <= 0 || decimal.Round(weight, 2) != weight)
                throw new ArgumentException("FEDEX_PARCEL_MEASUREMENTS_INVALID");
            return new(1, shipment.TenantId, shipment.Id, order.Id, input, length, width, height, weight,
                ParcelMeasurements.Contents(order), actor, DateTime.UtcNow);
        }
        catch (OverflowException) { throw new ArgumentException("FEDEX_PARCEL_MEASUREMENTS_INVALID"); }
    }

    public static ParcelEvidence Restore(Shipment shipment, Order order)
    {
        ParcelEvidence evidence;
        try { evidence = JsonSerializer.Deserialize<ParcelEvidence>(shipment.ParcelEvidenceJson ?? "null") ?? throw new ArgumentException("PARCEL_MEASUREMENTS_REQUIRED"); }
        catch (JsonException) { throw new ArgumentException("PARCEL_EVIDENCE_INVALID"); }
        var validated = Admit(evidence.Measurement, shipment, order, evidence.MeasuredBy);
        if (evidence.Version != 1 || evidence.TenantId != shipment.TenantId || evidence.ShipmentId != shipment.Id || evidence.OrderId != order.Id ||
            evidence.RecordedAtUtc.Kind != DateTimeKind.Utc || evidence.RecordedAtUtc == default || evidence.RecordedAtUtc > DateTime.UtcNow ||
            evidence.LengthCm != validated.LengthCm || evidence.WidthCm != validated.WidthCm || evidence.HeightCm != validated.HeightCm ||
            evidence.GrossWeightKg != validated.GrossWeightKg || evidence.Contents == null || !evidence.Contents.SequenceEqual(validated.Contents))
            throw new ArgumentException("PARCEL_EVIDENCE_CONTEXT_CHANGED");
        return evidence;
    }
}
