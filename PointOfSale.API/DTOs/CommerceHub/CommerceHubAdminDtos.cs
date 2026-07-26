using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.CommerceHub;

public class PublishToCommerceHubRequest
{
    public Guid VariantId { get; set; }
    [JsonPropertyName("commerceHubCategoryId")]
    public Guid? CommerceHubCategoryId { get; set; }

    [JsonPropertyName("categoryId")]
    public Guid? CategoryId { get; set; }

    public decimal? CommerceHubPrice { get; set; }
    public string? OverrideReason { get; set; }
    public int? ExpectedPriceOverrideMutationVersion { get; set; }
    public JsonElement? AttributesJson { get; set; }

    public Guid ResolveCategoryId() => CommerceHubCategoryId ?? CategoryId ?? Guid.Empty;
    public string? ResolveAttributesJson() => CommerceHubRequestJsonHelper.SerializeAttributesJson(AttributesJson);
}

public class UpdateCommerceHubListingRequest
{
    public Guid VariantId { get; set; }
    [JsonPropertyName("commerceHubCategoryId")]
    public Guid? CommerceHubCategoryId { get; set; }

    [JsonPropertyName("categoryId")]
    public Guid? CategoryId { get; set; }

    public decimal? CommerceHubPrice { get; set; }
    public string? OverrideReason { get; set; }
    public int? ExpectedPriceOverrideMutationVersion { get; set; }
    public JsonElement? AttributesJson { get; set; }

    public Guid ResolveCategoryId() => CommerceHubCategoryId ?? CategoryId ?? Guid.Empty;
    public string? ResolveAttributesJson() => CommerceHubRequestJsonHelper.SerializeAttributesJson(AttributesJson);
}

public class UnpublishFromCommerceHubRequest
{
    public Guid VariantId { get; set; }
}

public class CommerceHubAdminListingResponse
{
    public Guid? ListingId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public bool IsPublished { get; set; }
    public Guid? CommerceHubCategoryId { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal? CommerceHubPrice { get; set; }
    public int PriceOverrideMutationVersion { get; set; }
    public decimal EffectivePrice { get; set; }
    public string? AttributesJson { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public bool WasChanged { get; set; }
}

public class CommerceHubAdminProductDto
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string ProductCurrency { get; set; } = "EGP";
    public bool IsCommerceHubCurrencyEligible { get; set; } = true;
    public string? CommerceHubCurrencyPolicyMessage { get; set; }
    public List<CommerceHubAdminVariantDto> Variants { get; set; } = new();
}

public class CommerceHubAdminVariantDto
{
    public Guid VariantId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string ProductCurrency { get; set; } = "EGP";
    public bool IsCommerceHubCurrencyEligible { get; set; } = true;
    public string? CommerceHubCurrencyPolicyMessage { get; set; }
    public string? AttributeSummary { get; set; }
    public CommerceHubAdminVariantListingDto? Listing { get; set; }
}

public class CommerceHubAdminVariantListingDto
{
    public Guid ListingId { get; set; }
    public bool IsPublished { get; set; }
    public Guid CommerceHubCategoryId { get; set; }
    public Guid CategoryId { get; set; }
    public decimal? CommerceHubPrice { get; set; }
    public int PriceOverrideMutationVersion { get; set; }
    public string? AttributesJson { get; set; }
}

public class CommerceHubListingOperationsQuery
{
    public string? Q { get; set; }
    public string? Status { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? OverrideEnabled { get; set; }
    public string? InventoryStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class CommerceHubListingOperationsRowDto
{
    public Guid ListingId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public bool IsPublished { get; set; }
    public bool IsSuppressed { get; set; }
    public string? SuppressionReason { get; set; }
    public DateTime? SuppressionTimestampUtc { get; set; }
    public string? SuppressionOwner { get; set; }
    public bool IsOrphaned { get; set; }
    public string? OrphanReason { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public bool OverrideEnabled { get; set; }
    public decimal? CommerceHubPrice { get; set; }
    public int PriceOverrideMutationVersion { get; set; }
    public decimal InventoryAvailable { get; set; }
    public string InventoryStatus { get; set; } = "unknown";
    public string? AttributesJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class CommerceHubListingKpisDto
{
    public int TotalListings { get; set; }
    public int PublishedListings { get; set; }
    public int UnpublishedListings { get; set; }
    public int SuppressedListings { get; set; }
    public int OverrideEnabledListings { get; set; }
    public int OrphanedListings { get; set; }
}

public class CommerceHubListingAnalyticsDto
{
    public int Days { get; set; }
    public long ListingViews { get; set; }
    public long ListingSearches { get; set; }
    public long ListingConversions { get; set; }
    public int SuppressionCount { get; set; }
    public string AuthorityOwner { get; set; } = "SQL CommerceHub listing/search/order authority";
}

public class CommerceHubListingNotificationDto
{
    public Guid ListingId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool Actionable { get; set; } = true;
}

public class CommerceHubBulkListingOperationRequest
{
    public string Operation { get; set; } = string.Empty;
    public List<Guid> ListingIds { get; set; } = new();
    public Guid? CategoryId { get; set; }
}

public class CommerceHubBulkListingOperationResponse
{
    public int Requested { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<CommerceHubBulkListingOperationOutcomeDto> Outcomes { get; set; } = new();
}

public class CommerceHubBulkListingOperationOutcomeDto
{
    public Guid ListingId { get; set; }
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public class CommerceHubAdminCommissionPreviewDto
{
    public Guid? VendorId { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal Rate { get; set; }
    public string Source { get; set; } = CommissionCalculator.LegacySource;
    public Guid? RuleId { get; set; }
    public int? Priority { get; set; }
    public CommissionRuleScopeType? ScopeType { get; set; }
    public Guid? ScopeId { get; set; }
    public bool UsedLegacyFallback { get; set; }
    public string CacheStrategy { get; set; } = "MemoryCache";
}

internal static class CommerceHubRequestJsonHelper
{
    public static string? SerializeAttributesJson(JsonElement? rawAttributesJson)
    {
        if (!rawAttributesJson.HasValue)
        {
            return null;
        }

        var value = rawAttributesJson.Value;
        return value.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.Null => null,
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            _ => value.GetRawText()
        };
    }
}
