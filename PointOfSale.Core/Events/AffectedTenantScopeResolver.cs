using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PointOfSale.Core.Events;

/// <summary>
/// Exception thrown when an event payload cannot be parsed or has an invalid structure
/// for tenant scope resolution.
/// SECURITY: Does not store the payload content to prevent PII/secret leakage in logs/serialization.
/// Use PayloadHash for correlation and PayloadLength for diagnostics.
/// </summary>
public class InvalidEventPayloadException : Exception
{
    /// <summary>
    /// Length of the original event payload in characters.
    /// </summary>
    public int PayloadLength { get; }

    /// <summary>
    /// SHA256 hash of the event payload for correlation and diagnostics.
    /// Can be used to identify the same payload across systems without exposing content.
    /// </summary>
    public string? PayloadHash { get; }

    public InvalidEventPayloadException(string message, string? eventPayload, Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrEmpty(eventPayload))
        {
            PayloadLength = 0;
            PayloadHash = null;
        }
        else
        {
            PayloadLength = eventPayload.Length;

            // Compute SHA256 hash for secure diagnostics
            var payloadBytes = Encoding.UTF8.GetBytes(eventPayload);
            var hashBytes = SHA256.HashData(payloadBytes);
            PayloadHash = Convert.ToHexString(hashBytes);
        }
    }
}

/// <summary>
/// Resolves the complete tenant impact scope for persisted integration events.
/// Multi-vendor aggregate events keep a backward-compatible base TenantId, but
/// ordering and readiness gates must use the full affected-tenant set.
/// </summary>
public static class AffectedTenantScopeResolver
{
    private static readonly string[] AffectedTenantArrayPropertyNames =
    [
        "AffectedTenantIds",
        "AffectedStoreIds",
        "AffectedVendorIds"
    ];

    private static readonly string[] TenantScalarPropertyNames =
    [
        "TenantId",
        "StoreId",
        "StoreTenantId",
        "VendorId",
        "AffiliateAuthorityTenantId"
    ];

    public static HashSet<Guid> Resolve(Guid? baseTenantId, string? eventPayload)
    {
        var tenantIds = new HashSet<Guid>();
        if (baseTenantId.HasValue && baseTenantId.Value != Guid.Empty)
        {
            tenantIds.Add(baseTenantId.Value);
        }

        if (string.IsNullOrWhiteSpace(eventPayload))
        {
            return tenantIds;
        }

        try
        {
            using var document = JsonDocument.Parse(eventPayload);
            var root = document.RootElement;
            
            // SECURITY: Fail closed - invalid payload structure must throw
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidEventPayloadException(
                    $"Event payload root must be a JSON object, but was {root.ValueKind}. " +
                    "Tenant scope resolution requires structured event data.",
                    eventPayload);
            }

            AddTenantScopeRecursive(root, tenantIds);
        }
        catch (JsonException jsonEx)
        {
            // SECURITY: Fail closed - malformed JSON must throw, not return partial data
            throw new InvalidEventPayloadException(
                "Event payload contains malformed JSON and cannot be parsed for tenant scope resolution. " +
                $"Parse error: {jsonEx.Message}",
                eventPayload,
                jsonEx);
        }

        return tenantIds;
    }

    public static bool ContainsTenant(Guid? baseTenantId, string? eventPayload, Guid tenantId) =>
        tenantId != Guid.Empty && Resolve(baseTenantId, eventPayload).Contains(tenantId);

    public static bool RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(
        Guid? baseTenantId,
        string? eventPayload)
    {
        if (string.IsNullOrWhiteSpace(eventPayload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(eventPayload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidEventPayloadException(
                    $"Event payload root must be a JSON object, but was {root.ValueKind}. " +
                    "Tenant scope ambiguity detection requires structured event data.",
                    eventPayload);
            }

            if (!LooksLikeMarketplaceOrderAggregate(root) ||
                ContainsExplicitAffectedTenantArray(root))
            {
                return false;
            }

            var tenantIds = new HashSet<Guid>();
            AddTenantScopeRecursive(root, tenantIds);

            if (baseTenantId.HasValue && baseTenantId.Value != Guid.Empty)
            {
                return tenantIds.Count != 1 || !tenantIds.Contains(baseTenantId.Value);
            }

            return tenantIds.Count > 0;
        }
        catch (JsonException jsonEx)
        {
            throw new InvalidEventPayloadException(
                "Event payload contains malformed JSON and cannot be parsed for tenant scope ambiguity detection. " +
                $"Parse error: {jsonEx.Message}",
                eventPayload,
                jsonEx);
        }
    }

    public static bool Overlaps(
        IReadOnlySet<Guid> tenantScope,
        Guid? otherBaseTenantId,
        string? otherEventPayload)
    {
        if (tenantScope.Count == 0)
        {
            return false;
        }

        var otherScope = Resolve(otherBaseTenantId, otherEventPayload);
        return otherScope.Any(tenantScope.Contains);
    }

    private static void AddGuidArray(JsonElement root, string propertyName, HashSet<Guid> tenantIds)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            AddGuidValue(item, tenantIds);
        }
    }

    private static void AddGuidScalar(JsonElement root, string propertyName, HashSet<Guid> tenantIds)
    {
        if (TryGetPropertyIgnoreCase(root, propertyName, out var element))
        {
            AddGuidValue(element, tenantIds);
        }
    }

    private static void AddGuidValue(JsonElement element, HashSet<Guid> tenantIds)
    {
        if (element.ValueKind == JsonValueKind.String &&
            Guid.TryParse(element.GetString(), out var tenantId) &&
            tenantId != Guid.Empty)
        {
            tenantIds.Add(tenantId);
        }
    }

    private static void AddTenantScopeRecursive(JsonElement element, HashSet<Guid> tenantIds)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (AffectedTenantArrayPropertyNames.Any(name =>
                    string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        AddGuidValue(item, tenantIds);
                    }
                }

                continue;
            }

            if (TenantScalarPropertyNames.Any(name =>
                    string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                AddGuidValue(property.Value, tenantIds);
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                AddTenantScopeRecursive(property.Value, tenantIds);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    AddTenantScopeRecursive(item, tenantIds);
                }
            }
        }
    }

    private static bool ContainsExplicitAffectedTenantArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            // Check if this property is one of the affected tenant array properties
            if (AffectedTenantArrayPropertyNames.Any(name =>
                    string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)) &&
                property.Value.ValueKind == JsonValueKind.Array)
            {
                // VALIDATION: Only consider the array "explicit" if it contains at least one
                // valid, non-empty GUID string. This prevents malformed arrays (e.g., empty
                // strings, invalid GUIDs, or non-string items) from being treated as explicit
                // tenant scope declarations.
                //
                // This aligns with the Resolve(...) method's AddGuidValue logic, which only
                // extracts valid, non-empty GUIDs from these arrays.
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        Guid.TryParse(item.GetString(), out var tenantId) &&
                        tenantId != Guid.Empty)
                    {
                        return true;
                    }
                }
                
                // Array exists but contains no valid GUIDs - treat as absent and continue checking
                // Skip generic array recursion for this property to avoid duplicate handling
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Object &&
                ContainsExplicitAffectedTenantArray(property.Value))
            {
                return true;
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (ContainsExplicitAffectedTenantArray(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool LooksLikeMarketplaceOrderAggregate(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // SCOPE FIX: Short-circuit if element is sub-order scoped
        // Sub-order elements and their children should not be considered marketplace-order aggregates
        if (TryGetPropertyIgnoreCase(element, "SubOrderId", out _))
        {
            return false;
        }

        if (TryGetPropertyIgnoreCase(element, "MarketplaceOrderId", out _))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                LooksLikeMarketplaceOrderAggregate(property.Value))
            {
                return true;
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (LooksLikeMarketplaceOrderAggregate(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement root,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
