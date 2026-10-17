using System.Text.Json;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>Actual measurements of one packed parcel. No catalog-derived defaults.</summary>
public sealed record ParcelMeasurement(decimal Length, decimal Width, decimal Height, string DimensionUnit,
    decimal GrossWeight, string WeightUnit, int PackageCount, string PieceReference)
{
    // Optional for existing carrier contracts; mandatory and immutable for Bosta.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ParcelGoodsDeclaration? Goods { get; init; }
}
public sealed record ParcelGoodsDeclaration(string Description, decimal Value, string Currency);
public sealed record ParcelContent(Guid OrderItemId, Guid ProductId, Guid? VariantId, decimal Quantity);
public sealed record ParcelEvidence(int Version, Guid TenantId, Guid ShipmentId, Guid OrderId,
    ParcelMeasurement Measurement, decimal LengthCm, decimal WidthCm, decimal HeightCm, decimal GrossWeightKg,
    IReadOnlyList<ParcelContent> Contents, Guid MeasuredBy, DateTime RecordedAtUtc);

public static class ParcelMeasurements
{
    // MyDHL 3.3.4 Package schema: weight 0.001..999999999999 (3 decimals),
    // dimensions 0.001..9999999 (3 decimals). Units sent to DHL are metric.
    public static bool IsMetricWeight(decimal value) => value is >= 0.001m and <= 999999999999m && decimal.Round(value, 3) == value;
    public static bool IsMetricDimension(decimal? value) => value is >= 0.001m and <= 9999999m && decimal.Round(value.Value, 3) == value;

    public static ParcelEvidence Admit(ParcelMeasurement input, Shipment shipment, Order order, Guid actor) =>
        AdmitCore(input, shipment, order, actor, requireGoods: true);

    private static ParcelEvidence AdmitCore(ParcelMeasurement input, Shipment shipment, Order order, Guid actor, bool requireGoods)
    {
        if (input == null) throw new ArgumentException("PARCEL_MEASUREMENTS_REQUIRED");
        if (input.PackageCount != 1 || shipment.PackageCount != 1) throw new ArgumentException("PARCEL_SINGLE_PACKAGE_REQUIRED");
        if (actor == Guid.Empty || input.PieceReference is not { Length: > 0 and <= 35 } ||
            input.PieceReference.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("PARCEL_PIECE_REFERENCE_INVALID");
        var dimensionFactor = input.DimensionUnit switch { "cm" => 1m, "mm" => 0.1m, "in" => 2.54m, _ => throw new ArgumentException("PARCEL_DIMENSION_UNIT_UNSUPPORTED") };
        var weightFactor = input.WeightUnit switch { "kg" => 1m, "g" => 0.001m, "lb" => 0.45359237m, "oz" => 0.028349523125m, _ => throw new ArgumentException("PARCEL_WEIGHT_UNIT_UNSUPPORTED") };
        try
        {
            var length = Convert(input.Length, dimensionFactor, 9999999m);
            var width = Convert(input.Width, dimensionFactor, 9999999m);
            var height = Convert(input.Height, dimensionFactor, 9999999m);
            var weight = Convert(input.GrossWeight, weightFactor, 999999999999m);
            if (!IsMetricDimension(length) || !IsMetricDimension(width) || !IsMetricDimension(height) || !IsMetricWeight(weight))
                throw new ArgumentException("PARCEL_MEASUREMENTS_OUT_OF_RANGE");
            if (order.TenantId != shipment.TenantId || order.Id != shipment.OrderId || order.Items.Count == 0 || order.Items.Any(i => i.Quantity <= 0))
                throw new ArgumentException("PARCEL_ORDER_CONTENTS_INVALID");
            if (shipment.CarrierCode == Core.Constants.CarrierCodes.Bosta && (requireGoods || input.Goods != null))
                BostaParcelContents.Validate(input.Goods, order);
            return new(1, shipment.TenantId, shipment.Id, order.Id, input, length, width, height, weight,
                Contents(order), actor, DateTime.UtcNow);
        }
        catch (OverflowException) { throw new ArgumentException("PARCEL_MEASUREMENTS_OUT_OF_RANGE"); }
    }

    private static decimal Convert(decimal value, decimal factor, decimal maximum)
    {
        if (value <= 0) throw new ArgumentException("PARCEL_POSITIVE_MEASUREMENTS_REQUIRED");
        var unrounded = value * factor;
        if (unrounded < 0.001m || unrounded > maximum) throw new ArgumentException("PARCEL_MEASUREMENTS_OUT_OF_RANGE");
        var result = decimal.Round(unrounded, 3, MidpointRounding.AwayFromZero);
        if (result <= 0) throw new ArgumentException("PARCEL_MEASUREMENTS_OUT_OF_RANGE");
        return result;
    }

    public static ParcelContent[] Contents(Order order) => order.Items.OrderBy(i => i.Id)
        .Select(i => new ParcelContent(i.Id, i.ProductId, i.VariantId, i.Quantity)).ToArray();

    public static ParcelEvidence Restore(Shipment shipment, Order order)
    {
        ParcelEvidence evidence;
        try { evidence = JsonSerializer.Deserialize<ParcelEvidence>(shipment.ParcelEvidenceJson ?? "null") ?? throw new ArgumentException("PARCEL_MEASUREMENTS_REQUIRED"); }
        catch (JsonException) { throw new ArgumentException("PARCEL_EVIDENCE_INVALID"); }
        // Legacy evidence can be read/reconciled under its original binding; only
        // a new save/create requires the new explicit goods declaration.
        var validated = AdmitCore(evidence.Measurement, shipment, order, evidence.MeasuredBy, requireGoods: false);
        if (evidence.Version != 1 || evidence.TenantId != shipment.TenantId || evidence.ShipmentId != shipment.Id || evidence.OrderId != order.Id ||
            evidence.LengthCm != validated.LengthCm || evidence.WidthCm != validated.WidthCm || evidence.HeightCm != validated.HeightCm ||
            evidence.GrossWeightKg != validated.GrossWeightKg || evidence.Contents == null || !evidence.Contents.SequenceEqual(validated.Contents))
            throw new ArgumentException("PARCEL_EVIDENCE_CONTEXT_CHANGED");
        return evidence;
    }

    public static void Apply(ParcelEvidence evidence, CreateShipmentRequest request)
    {
        request.LengthCm = evidence.LengthCm; request.WidthCm = evidence.WidthCm; request.HeightCm = evidence.HeightCm;
        request.WeightKg = evidence.GrossWeightKg; request.PackageCount = 1; request.PieceReference = evidence.Measurement.PieceReference;
    }
}
