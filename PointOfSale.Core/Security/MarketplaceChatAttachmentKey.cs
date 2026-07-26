namespace PointOfSale.Core.Security;

public static class MarketplaceChatAttachmentKey
{
    public const string StorageSegment = "marketplace-chat-attachments";
    private const string LegacyUploadsSegment = "/uploads/chat/";

    public static string BuildStoragePath(Guid tenantId, Guid serviceRequestId, Guid userId)
    {
        return $"tenants/{tenantId:D}/{StorageSegment}/{serviceRequestId:D}/{userId:D}";
    }

    public static bool TryNormalizeTenantScopedObjectKey(
        string? candidate,
        Guid tenantId,
        Guid serviceRequestId,
        Guid userId,
        out string objectKey)
    {
        objectKey = string.Empty;

        var normalized = Normalize(candidate);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var expectedPrefix = $"{BuildStoragePath(tenantId, serviceRequestId, userId)}/";
        if (!normalized.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        objectKey = normalized;
        return true;
    }

    public static bool TryExtractLegacyLocalFileName(string? candidate, out string fileName)
    {
        fileName = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var value = candidate.Trim().Replace('\\', '/');
        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            value = absoluteUri.AbsolutePath;
        }

        // Normalize: remove leading slash for consistent searching
        value = value.TrimStart('/');

        // Single search for the canonical uploads segment (without leading slash)
        const string uploadsSegment = "uploads/chat/";
        var uploadsIndex = value.IndexOf(uploadsSegment, StringComparison.OrdinalIgnoreCase);
        if (uploadsIndex < 0)
        {
            return false;
        }

        var rawFileName = value[(uploadsIndex + uploadsSegment.Length)..].Trim('/');
        var safeFileName = Path.GetFileName(rawFileName);

        if (string.IsNullOrWhiteSpace(safeFileName) ||
            safeFileName.Contains("..", StringComparison.Ordinal) ||
            rawFileName.Contains('/', StringComparison.Ordinal) ||
            rawFileName.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        fileName = safeFileName;
        return true;
    }

    public static string? GetDisplayFileName(string? candidate)
    {
        var normalized = Normalize(candidate);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return Path.GetFileName(normalized);
        }

        return TryExtractLegacyLocalFileName(candidate, out var legacyFileName)
            ? legacyFileName
            : null;
    }

    private static string Normalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        // URL-decode to detect encoded traversal sequences like %2e%2e
        try
        {
            candidate = Uri.UnescapeDataString(candidate);
        }
        catch
        {
            // Treat decode failures as invalid input
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var normalized = candidate
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            return string.Empty;
        }

        return normalized;
    }
}
