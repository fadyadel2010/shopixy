namespace PointOfSale.Core.DTOs.Customs;

public sealed record CarrierNeutralCustomsDto(
    Guid ShipmentCustomsSnapshotId,
    Guid ShipmentId,
    Guid OrderId,
    int SnapshotVersion,
    int DtoVersion,
    string OriginCountryIso2,
    string DestinationCountryIso2,
    decimal ShipmentGrossWeight,
    string ShipmentGrossWeightUnit,
    IReadOnlyList<CarrierNeutralCustomsLineDto> Lines);

public sealed record CarrierNeutralCustomsLineDto(
    Guid OrderItemId,
    Guid ProductId,
    Guid? ProductVariantId,
    Guid CustomsEvidenceId,
    Guid CustomsEvidenceVersionId,
    int EvidenceVersionNumber,
    string HsCode,
    string CommodityDescription,
    string CountryOfOriginIso2,
    string? CountryOfManufactureIso2,
    decimal DeclaredValue,
    string Currency,
    decimal Quantity,
    string QuantityUnit,
    decimal NetWeight,
    string NetWeightUnit,
    bool? DangerousGoods,
    string? ExportClassification);
