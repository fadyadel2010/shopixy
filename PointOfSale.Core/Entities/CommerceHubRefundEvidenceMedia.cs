namespace PointOfSale.Core.Entities;

public class CommerceHubRefundEvidenceMedia : BaseEntity
{
    public Guid? RefundRequestId { get; set; }
    public CommerceHubRefundRequest? RefundRequest { get; set; }
    public Guid CustomerId { get; set; }
    public CommerceHubCustomer Customer { get; set; } = null!;
    public string ObjectKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long ExpectedSizeBytes { get; set; }
    public string? ContentHash { get; set; }
    public string? MetadataJson { get; set; }
    public string? ThumbnailObjectKey { get; set; }
    public bool IsTemporary { get; set; } = true;
    public bool IsFlagged { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public DateTime? AttachedAtUtc { get; set; }
}
