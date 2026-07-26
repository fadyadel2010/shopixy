namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Single source of truth for media uploads.
/// All file uploads MUST go through this service.
/// </summary>
public interface IMediaService
{
    Task<string> UploadImageAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<string> UploadAttachmentAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task DeleteMediaAsync(string mediaUrl, CancellationToken cancellationToken = default);
}
