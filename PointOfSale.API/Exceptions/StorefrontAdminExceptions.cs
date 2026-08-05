namespace PointOfSale.API.Exceptions;

/// <summary>
/// Exception thrown when a concurrent modification conflict is detected.
/// Phase: Storefront Admin Hardening - P0
/// 
/// This exception is thrown when:
/// - Two admins try to save changes to the same StoreConfig/CMS page simultaneously
/// - The row version (xmin) has changed since the entity was loaded
/// 
/// Frontend should:
/// 1. Display user-friendly message: "This configuration was modified by another admin. Please refresh."
/// 2. Optionally offer to merge changes or force overwrite
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public string EntityType { get; }
    public Guid EntityId { get; }
    public string? ModifiedBy { get; }
    
    public ConcurrencyConflictException(
        string entityType, 
        Guid entityId, 
        string? modifiedBy = null)
        : base($"The {entityType} was modified by another user. Please refresh and try again.")
    {
        EntityType = entityType;
        EntityId = entityId;
        ModifiedBy = modifiedBy;
    }
    
    public ConcurrencyConflictException(
        string entityType, 
        Guid entityId, 
        Exception innerException)
        : base($"The {entityType} was modified by another user. Please refresh and try again.", innerException)
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}

/// <summary>
/// Exception thrown when a slug uniqueness constraint is violated.
/// Phase: Storefront Admin Hardening - P0
/// </summary>
public class SlugConflictException : Exception
{
    public string Slug { get; }
    public Guid? TenantId { get; }
    public string EntityType { get; }

    public SlugConflictException(string entityType, string slug)
        : base($"The slug '{slug}' already exists. Please use a different slug.")
    {
        EntityType = entityType;
        Slug = slug;
        TenantId = null;
    }
    
    public SlugConflictException(string entityType, string slug, Guid tenantId)
        : base($"The slug '{slug}' already exists for this tenant. Please use a different slug.")
    {
        EntityType = entityType;
        Slug = slug;
        TenantId = tenantId;
    }
}

/// <summary>
/// Structured failure emitted only by the canonical Navigation admission pipeline.
/// </summary>
public sealed class NavigationAdmissionException : Exception
{
    public string Code { get; }
    public string LinkType { get; }
    public string Menu { get; }
    public int? Index { get; }
    public string? Field { get; }
    public Guid? TargetId { get; }

    public NavigationAdmissionException(
        string code,
        string message,
        string linkType = "",
        string menu = "",
        int? index = null,
        string? field = null,
        Guid? targetId = null)
        : base(message)
    {
        Code = code;
        LinkType = linkType;
        Menu = menu;
        Index = index;
        Field = field;
        TargetId = targetId;
    }
}

public sealed class FooterAuthoringException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public string? Field { get; }

    public FooterAuthoringException(
        int statusCode,
        string code,
        string message,
        string? field = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        Field = field;
    }
}
