using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Domain service for ServiceRequest chat functionality.
/// Enforces interaction invariants and persists messages.
/// PHASE CR-1: Server-authoritative tenant parameter added.
/// </summary>
public interface IChatService
{
    Task<ServiceRequestMessage> SendMessageAsync(Guid serviceRequestId, Guid senderUserId, Guid tenantId, string messageBody, string? attachmentObjectKey = null, CancellationToken cancellationToken = default);
    Task<ServiceRequestMessage> SendSystemMessageAsync(Guid serviceRequestId, string messageBody, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get messages for a service request with cursor-based pagination.
    /// Returns the selected window in ascending presentation order.
    /// Pass beforeMessageId to load rows strictly older than that authoritative message.
    /// PHASE: Marketplace Audit Hardening (P1-6) — prevents unbounded message loading.
    /// </summary>
    Task<IEnumerable<ServiceRequestMessage>> GetMessagesAsync(Guid serviceRequestId, Guid userId, Guid tenantId, int pageSize = 100, Guid? beforeMessageId = null, CancellationToken cancellationToken = default);
    
    Task<bool> CanUserParticipateAsync(Guid serviceRequestId, Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> CanUserViewConversationAsync(Guid serviceRequestId, Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}

