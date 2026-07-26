namespace PointOfSale.Core.Abstractions;

/// <summary>
/// Abstraction for marketplace operational telemetry.
/// Allows controllers and services to record metrics without depending on infrastructure.
/// </summary>
public interface IMarketplaceTelemetry
{
    /// <summary>
    /// Records an external or absolute attachment URL rejection.
    /// </summary>
    /// <param name="tenantId">The tenant attempting to send the attachment reference</param>
    void RecordChatAttachmentUrlRejected(Guid tenantId);

    /// <summary>
    /// Records invalid chat attachment input that was not a valid server-issued object key.
    /// </summary>
    /// <param name="tenantId">The tenant attempting to send the attachment reference</param>
    /// <param name="reason">Normalized rejection reason</param>
    void RecordChatAttachmentInvalidInput(Guid tenantId, string reason);

    /// <summary>
    /// Records an unauthorized attachment access attempt (anonymous user).
    /// </summary>
    void RecordAttachmentUnauthorizedAccess();

    /// <summary>
    /// Records an attachment access denial for an authenticated user.
    /// </summary>
    /// <param name="tenantId">The tenant ID of the requester</param>
    /// <param name="reason">The reason for denial (normalized for metrics)</param>
    void RecordAttachmentAccessDenied(Guid tenantId, string reason);

    /// <summary>
    /// Records a successful attachment delivery.
    /// </summary>
    /// <param name="tenantId">The tenant ID of the requester</param>
    /// <param name="deliveryMode">The delivery mode (e.g., "signed_url", "direct")</param>
    void RecordAttachmentServed(Guid tenantId, string deliveryMode);

    /// <summary>
    /// Records trust aggregate drift detection.
    /// </summary>
    /// <param name="count">Number of drift instances detected</param>
    void RecordTrustDrift(long count);

    /// <summary>
    /// Records Elasticsearch index drift detection.
    /// </summary>
    /// <param name="count">Number of drift instances detected</param>
    void RecordElasticsearchDrift(long count);

    /// <summary>
    /// Updates the current disk usage metric.
    /// </summary>
    /// <param name="bytes">Current disk usage in bytes</param>
    void UpdateDiskUsage(long bytes);
}
