using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

internal sealed record AccountBoundRegistrationIntent(int Version, Guid TenantId, Guid ShipmentId, Guid OrderId,
    string CarrierCode, Guid ConfigurationId, string EncryptedAccount, string Context, string ParcelJson,
    string? CanonicalPairFingerprint, CreateShipmentRequest Request);

internal static class CarrierOriginalAccountIdentity
{
    internal static string CaptureCredentials(CarrierCredentials credentials, ICarrierCredentialEncryptionService encryption)
    {
        var encrypted = encryption.Encrypt(Serialize(credentials));
        if (!encryption.IsEncrypted(encrypted)) throw new InvalidOperationException("CARRIER_ACCOUNT_BINDING_ENCRYPTION_REQUIRED");
        return encrypted;
    }

    internal static bool CredentialsMatch(string encrypted, CarrierCredentials credentials, ICarrierCredentialEncryptionService encryption)
    {
        if (!encryption.IsEncrypted(encrypted)) return false;
        var original = Encoding.UTF8.GetBytes(encryption.Decrypt(encrypted));
        var current = Encoding.UTF8.GetBytes(Serialize(credentials));
        return original.Length == current.Length && CryptographicOperations.FixedTimeEquals(original, current);
    }

    internal static string Serialize(CarrierCredentials c) => JsonSerializer.Serialize(new
    {
        c.ApiKey, c.ApiSecret, c.AccountId, c.ApiUrl, c.UseSandbox,
        ExtraConfig = c.ExtraConfig.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()
    });

    internal static bool Matches(AccountBoundRegistrationIntent intent, CarrierCredentialsResolution resolution, ICarrierCredentialEncryptionService encryption)
    {
        if (!resolution.Succeeded || resolution.Configuration == null || resolution.Credentials == null || intent.ConfigurationId != resolution.Configuration.Id ||
            resolution.CarrierCode != intent.CarrierCode || resolution.Configuration.CarrierCode != intent.CarrierCode ||
            resolution.Configuration.UseSandbox != resolution.Credentials.UseSandbox || !encryption.IsEncrypted(intent.EncryptedAccount)) return false;
        var original = Encoding.UTF8.GetBytes(encryption.Decrypt(intent.EncryptedAccount));
        var current = Encoding.UTF8.GetBytes(Serialize(resolution.Credentials));
        return original.Length == current.Length && CryptographicOperations.FixedTimeEquals(original, current);
    }

    internal static bool Verifies(Shipment s, CarrierCredentialsResolution resolution, ICarrierCredentialEncryptionService? encryption)
    {
        try
        {
            if (encryption == null || s.CarrierRequestSnapshotJson == null || s.CarrierRequestFingerprint == null) return false;
            var intent = JsonSerializer.Deserialize<AccountBoundRegistrationIntent>(s.CarrierRequestSnapshotJson);
            return intent != null && intent.Version == 1 && intent.Request != null && intent.TenantId == s.TenantId && intent.ShipmentId == s.Id &&
                intent.OrderId == s.OrderId && intent.CarrierCode == s.CarrierCode && !string.IsNullOrWhiteSpace(s.CarrierRequestId) &&
                intent.Request.CarrierRequestId == s.CarrierRequestId && intent.Request.InternalShipmentId == s.CarrierRequestId &&
                s.CarrierRequestFingerprint == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.CarrierRequestSnapshotJson))) &&
                Matches(intent, resolution, encryption);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or CryptographicException) { return false; }
    }
}
