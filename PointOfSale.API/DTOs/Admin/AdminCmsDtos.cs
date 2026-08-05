using System.Text.Json;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Admin;

public sealed class AdminCmsPageRequest
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public JsonElement Content { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public int? ExpectedVersion { get; set; }
    public AdminCmsPageSeoRequest Seo { get; set; } = new();

    public string? Validate(bool requireSlug = true)
    {
        Seo ??= new AdminCmsPageSeoRequest();

        if (requireSlug && string.IsNullOrWhiteSpace(Slug))
        {
            return "Slug is required";
        }

        if (!string.IsNullOrWhiteSpace(Slug) && Slug.Length > 160)
        {
            return "Slug must be less than 160 characters";
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            return "Title is required";
        }

        if (Title.Length > 200)
        {
            return "Title must be less than 200 characters";
        }

        if (SchemaVersion < 1)
        {
            return "Schema version must be greater than zero";
        }

        if (Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "Content is required";
        }

        return null;
    }
}

public sealed class AdminCmsPageSeoRequest
{
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? OpenGraphImageUrl { get; set; }
}

public sealed class AdminCmsPageListItemResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public bool HasDraft { get; set; }
    public bool HasPublished { get; set; }
    public bool HasPendingChanges { get; set; }
    public string Status { get; set; } = "Draft";
    public int? DraftVersion { get; set; }
    public int? PublishedVersion { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class AdminCmsPageResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public bool HasPendingChanges { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public AdminCmsPageVersionResponse? Draft { get; set; }
    public AdminCmsPageVersionResponse? Published { get; set; }
}

public sealed class AdminCmsPageVersionResponse
{
    public Guid RecordId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public JsonElement Content { get; set; }
    public int Version { get; set; }
    public int SchemaVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public AdminCmsPageSeoRequest Seo { get; set; } = new();
}

public sealed class AdminCmsPreviewAccessResponse
{
    public string Slug { get; set; } = string.Empty;
    public string PreviewToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class AdminCmsSeedResponse
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> SeededPages { get; set; } = new();
    public bool NavigationCreated { get; set; }
    public bool HomepageCreated { get; set; }
    public bool FooterCreated { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class AdminCmsPageVersionHistoryResponse
{
    public Guid PageId { get; set; }
    public Guid TenantId { get; set; }
    public int? CurrentDraftVersion { get; set; }
    public int? PublishedVersion { get; set; }
    public List<AdminCmsPageVersionHistoryEntryResponse> Versions { get; set; } = new();
}

public sealed class AdminCmsPageVersionHistoryEntryResponse
{
    public int Version { get; set; }
    public bool IsCurrentDraft { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public AdminCmsPageVersionResponse Snapshot { get; set; } = new();
}

public sealed class AdminCmsRollbackRequest
{
    public int Version { get; set; }
    public string? Reason { get; set; }
}

public class AdminNavigationRequest
{
    public string? RowVersion { get; set; }
    public List<AdminNavigationLinkRequest> Header { get; set; } = new();
    public List<AdminNavigationLinkRequest> Footer { get; set; } = new();
}

public class AdminNavigationLinkRequest
{
    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }
    public string Type { get; set; } = "custom";
    public Guid? TargetId { get; set; }
    public string? CollectionKey { get; set; }
    public string? Path { get; set; }
    public string? Slug { get; set; }
    public bool OpenInNewTab { get; set; }
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
    public string? ValidationStatus { get; set; }
    public string? ValidationCode { get; set; }
    public string? ValidationMessage { get; set; }
}

public class AdminNavigationResponse
{
    public string RowVersion { get; set; } = "0";
    public AdminNavigationConfigResponse Draft { get; set; } = new();
    public AdminNavigationConfigResponse? Live { get; set; }
    public bool HasPendingChanges { get; set; }
    public List<AdminNavigationValidationWarningResponse> ValidationWarnings { get; set; } = new();
    public List<AdminNavigationValidationWarningResponse> BrokenLinks { get; set; } = new();
}

public class AdminNavigationConfigResponse
{
    public List<AdminNavigationLinkRequest> Header { get; set; } = new();
    public List<AdminNavigationLinkRequest> Footer { get; set; } = new();
}

public sealed class AdminNavigationValidationWarningResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Snapshot { get; set; } = string.Empty;
    public string Menu { get; set; } = string.Empty;
    public int? Index { get; set; }
    public string? Field { get; set; }
    public string LinkType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string Status { get; set; } = "invalid";
}

public sealed class AdminNavigationTargetsResponse
{
    public List<AdminNavigationTargetOptionResponse> Categories { get; set; } = new();
    public List<AdminNavigationTargetOptionResponse> Products { get; set; } = new();
    public List<AdminNavigationTargetOptionResponse> CmsPages { get; set; } = new();
    public List<AdminNavigationTargetOptionResponse> Collections { get; set; } = new();
}

public sealed class AdminNavigationTargetOptionResponse
{
    public string Type { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string? CollectionKey { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? ResolvedPath { get; set; }
    public string State { get; set; } = string.Empty;
}

public sealed class AdminNavigationAdmissionErrorDetails
{
    public string LinkType { get; set; } = string.Empty;
    public string Menu { get; set; } = string.Empty;
    public int? Index { get; set; }
    public string? Field { get; set; }
    public Guid? TargetId { get; set; }
}

public class AdminCmsApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public object? LatestVersion { get; set; }
    public AdminNavigationAdmissionErrorDetails? Navigation { get; set; }

    public AdminCmsApiErrorResponse()
    {
    }

    public AdminCmsApiErrorResponse(string code, string message, string? traceId = null)
    {
        Code = code;
        Message = message;
        TraceId = traceId;
    }

    public static AdminCmsApiErrorResponse TenantHeaderMissing(string? traceId = null) =>
        new("TENANT_HEADER_MISSING", "X-Tenant-Id header is required", traceId);

    public static AdminCmsApiErrorResponse TenantHeaderInvalid(string? traceId = null) =>
        new("TENANT_HEADER_INVALID", "X-Tenant-Id must be a valid GUID", traceId);

    public static AdminCmsApiErrorResponse InvalidSlug(string? traceId = null) =>
        new("INVALID_SLUG", "Invalid page slug", traceId);

    public static AdminCmsApiErrorResponse InvalidPageId(string? traceId = null) =>
        new("INVALID_PAGE_ID", "Invalid page identifier", traceId);

    public static AdminCmsApiErrorResponse PageNotFound(string? traceId = null) =>
        new("PAGE_NOT_FOUND", "CMS page not found", traceId);

    public static AdminCmsApiErrorResponse InternalError(string? traceId = null) =>
        new("INTERNAL_ERROR", "An unexpected error occurred", traceId);

    public static AdminCmsApiErrorResponse ValidationError(string message, string? traceId = null) =>
        new("VALIDATION_ERROR", message, traceId);
}
