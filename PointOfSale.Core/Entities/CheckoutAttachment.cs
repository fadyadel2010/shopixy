using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class CheckoutAttachment : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CartId { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public Guid CheckoutFieldDefinitionId { get; set; }
    public Guid? OrderId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string? FinalObjectKey { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long ExpectedSizeBytes { get; set; }
    public CheckoutAttachmentStatus Status { get; set; } = CheckoutAttachmentStatus.Pending;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public DateTime? AttachedAtUtc { get; set; }
    public string? StagedETag { get; set; }
    public string? FinalETag { get; set; }
    public string? FinalChecksumSha256 { get; set; }
    public string MetadataJson { get; set; } = "{}";

    public Order? Order { get; set; }
}
