using PointOfSale.Core.Enums;
using PointOfSale.Core.Security;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Immutable message in a ServiceRequest conversation.
/// Messages can only be created, never modified.
/// ConversationId == ServiceRequestId (1:1 mapping).
/// </summary>
public class ServiceRequestMessage : TenantEntity
{
    public Guid ServiceRequestId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public SenderRole SenderRole { get; private set; }
    public MessageType MessageType { get; private set; }
    public string Body { get; private set; } = string.Empty;
    
    // NOTE: Retained name for backwards compatibility; stores storage/object key, not a URL
    // Phase FE-3: Attachment support
    public string? AttachmentUrl { get; private set; }

    public ServiceRequest? ServiceRequest { get; private set; }

    private ServiceRequestMessage() { }

    public static ServiceRequestMessage CreateText(
        Guid tenantId,
        Guid serviceRequestId,
        Guid senderUserId,
        SenderRole senderRole,
        string body,
        string? attachmentObjectKey = null)
    {
        // Body can be empty if there's an attachment
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(attachmentObjectKey))
            throw new ArgumentException("Message must have either body text or an attachment", nameof(body));

        string? normalizedKey = null;
        if (!string.IsNullOrWhiteSpace(attachmentObjectKey))
        {
            if (!MarketplaceChatAttachmentKey.TryNormalizeTenantScopedObjectKey(
                attachmentObjectKey,
                tenantId,
                serviceRequestId,
                senderUserId,
                out var validatedKey))
            {
                throw new ArgumentException(
                    "Attachment reference must be a valid tenant-scoped storage key for this service request and user.",
                    nameof(attachmentObjectKey));
            }
            normalizedKey = validatedKey;
        }

        return new ServiceRequestMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceRequestId = serviceRequestId,
            SenderUserId = senderUserId,
            SenderRole = senderRole,
            MessageType = MessageType.Text,
            Body = body?.Trim() ?? string.Empty,
            AttachmentUrl = normalizedKey, // Stores normalized object key, not URL (property name retained for compatibility)
            CreatedAt = DateTime.UtcNow
        };
    }

    public void ReplaceAttachmentReference(string attachmentObjectKey)
    {
        if (string.IsNullOrWhiteSpace(attachmentObjectKey))
        {
            throw new ArgumentException("Attachment reference is required.", nameof(attachmentObjectKey));
        }

        if (!MarketplaceChatAttachmentKey.TryNormalizeTenantScopedObjectKey(
            attachmentObjectKey,
            TenantId,
            ServiceRequestId,
            SenderUserId,
            out var normalizedKey))
        {
            throw new ArgumentException(
                "Attachment reference must be a valid tenant-scoped storage key for this service request and user.",
                nameof(attachmentObjectKey));
        }

        AttachmentUrl = normalizedKey; // Stores normalized object key, not URL (property name retained for compatibility)
        UpdatedAt = DateTime.UtcNow;
    }

    public static ServiceRequestMessage CreateSystem(
        Guid tenantId,
        Guid serviceRequestId,
        Guid systemUserId,
        string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("System message body cannot be empty", nameof(body));

        return new ServiceRequestMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceRequestId = serviceRequestId,
            SenderUserId = systemUserId,
            SenderRole = SenderRole.Provider, // System messages use Provider role by convention
            MessageType = MessageType.System,
            Body = body.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }
}
