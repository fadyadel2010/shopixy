namespace PointOfSale.Core.Abstractions;

/// <summary>
/// Verifies that a chat attachment reference was issued through the server-backed
/// durable attachment pipeline before it is bound to a message.
/// </summary>
public interface IChatAttachmentReferenceVerifier
{
    Task<bool> IsServerIssuedAttachmentAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}
