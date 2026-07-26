using System.Text.RegularExpressions;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public class CommerceHubVendorPaymentProof
{
    /// <summary>
    /// The exact sentinel value used for legacy records migrated before ContentHash tracking.
    /// This value is set by migration 20260409202930_AddContentHashToVendorPaymentProof.
    /// Format: 'LEGACY_' (7 chars) + 57 zeros = 64 characters total (SHA-256 hash length)
    /// </summary>
    public const string LegacyMigrationSentinel = "LEGACY_000000000000000000000000000000000000000000000000000000000";

    public Guid Id { get; private set; }
    public Guid PaymentRequestId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public DateTime UploadedAtUtc { get; private set; }

    public CommerceHubVendorPaymentRequest? PaymentRequest { get; private set; }

    private CommerceHubVendorPaymentProof()
    {
    }

    /// <summary>
    /// Determines whether the given content hash is the legacy migration sentinel.
    /// Only the exact sentinel value bypasses ContentHash validation in replay scenarios.
    /// </summary>
    public static bool IsLegacyContentHash(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return false;
        }

        return string.Equals(
            contentHash.Trim(),
            LegacyMigrationSentinel,
            StringComparison.Ordinal);
    }

    public static CommerceHubVendorPaymentProof Create(
        Guid paymentRequestId,
        string objectKey,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string contentHash,
        DateTime? uploadedAtUtc = null)
    {
        if (paymentRequestId == Guid.Empty)
        {
            throw new ArgumentException("PaymentRequestId is required.", nameof(paymentRequestId));
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("ObjectKey is required.", nameof(objectKey));
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("OriginalFileName is required.", nameof(originalFileName));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("ContentType is required.", nameof(contentType));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "SizeBytes must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(contentHash))
        {
            throw new ArgumentException("ContentHash is required.", nameof(contentHash));
        }

        var trimmedHash = contentHash.Trim();
        var isLegacySentinel = IsLegacyContentHash(trimmedHash);
        
        if (!isLegacySentinel && !Regex.IsMatch(trimmedHash, "^[A-Fa-f0-9]{64}$"))
        {
            throw new ArgumentException("ContentHash must be a valid SHA-256 hash (64 hexadecimal characters) or a legacy sentinel.", nameof(contentHash));
        }

        return new CommerceHubVendorPaymentProof
        {
            Id = Guid.NewGuid(),
            PaymentRequestId = paymentRequestId,
            ObjectKey = objectKey.Trim(),
            OriginalFileName = originalFileName.Trim(),
            ContentType = contentType.Trim().ToLowerInvariant(),
            SizeBytes = sizeBytes,
            ContentHash = contentHash.Trim().ToUpperInvariant(),
            UploadedAtUtc = uploadedAtUtc ?? DateTime.UtcNow
        };
    }
}
