using System.Security.Cryptography;
using System.Text.Json;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces.Geographic;

namespace PointOfSale.Infrastructure.Geographic;

public static class WarehouseAddressFingerprint
{
    public const string Version = "WarehouseAddressAdmission-v1";

    public static string Compute(Guid tenantId, Warehouse warehouse, GeographicPlaceLineage lineage, string? postalCode) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version, TenantId = tenantId, WarehouseId = warehouse.Id,
            StreetLine1 = warehouse.Address?.Trim(), ContactName = warehouse.Name.Trim(), ContactPhone = warehouse.Phone?.Trim(),
            Lineage = lineage, PostalCode = postalCode?.Trim()
        })));
}
