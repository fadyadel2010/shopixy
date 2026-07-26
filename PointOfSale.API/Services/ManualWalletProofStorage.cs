using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services;

public static class ManualWalletProofStorage
{
    private const string ManualWalletProofSegment = "manual-wallet-proofs/";

    public static bool TryResolveObjectKey(string? proofImageUrl, R2Options options, out string objectKey)
    {
        objectKey = string.Empty;
        _ = options;

        var candidate = proofImageUrl?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (IsExternalAbsoluteUrl(candidate))
        {
            return false;
        }

        return TryNormalizeObjectKey(candidate, out objectKey);
    }

    public static bool TryNormalizeTenantScopedObjectKey(
        string? candidate,
        Guid tenantId,
        R2Options options,
        out string objectKey)
    {
        objectKey = string.Empty;

        if (!TryResolveObjectKey(candidate, options, out var resolvedObjectKey))
        {
            return false;
        }

        var expectedPrefix = $"tenants/{tenantId:D}/manual-wallet-proofs/";
        if (!resolvedObjectKey.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        objectKey = resolvedObjectKey;
        return true;
    }

    public static bool IsExternalAbsoluteUrl(string? candidate)
    {
        var trimmed = candidate?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) &&
               Uri.TryCreate(trimmed, UriKind.Absolute, out _);
    }

    private static bool TryNormalizeObjectKey(string candidate, out string objectKey)
    {
        objectKey = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate) || Uri.TryCreate(candidate, UriKind.Absolute, out _))
        {
            return false;
        }

        var normalized = candidate
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');

        // URL-decode to detect encoded traversal sequences like %2e%2e
        try
        {
            normalized = Uri.UnescapeDataString(normalized);
        }
        catch
        {
            // Treat decode failures as invalid input
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        // Re-normalize after decoding
        normalized = normalized
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.StartsWith("manual-wallet-proofs/", StringComparison.OrdinalIgnoreCase) ||
            Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            return false;
        }

        if (!normalized.StartsWith("tenants/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Validate that "manual-wallet-proofs" exists as a complete path segment
        var segments = normalized.Split('/');
        var hasManualWalletProofSegment = false;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].Equals("manual-wallet-proofs", StringComparison.OrdinalIgnoreCase))
            {
                hasManualWalletProofSegment = true;
                break;
            }
        }

        if (!hasManualWalletProofSegment)
        {
            return false;
        }

        objectKey = normalized;
        return true;
    }

    public static bool LooksLikeLegacyLocalReference(string? proofImageUrl)
    {
        var candidate = proofImageUrl?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return !Uri.TryCreate(candidate, UriKind.Absolute, out _) &&
               candidate.Contains("/manual-payments/", StringComparison.OrdinalIgnoreCase);
    }
}
