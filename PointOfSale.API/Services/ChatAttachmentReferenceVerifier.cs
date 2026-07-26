using System.Net;
using Amazon.S3;
using PointOfSale.Core.Abstractions;

namespace PointOfSale.API.Services;

public sealed class ChatAttachmentReferenceVerifier : IChatAttachmentReferenceVerifier
{
    private readonly IR2StorageService _storageService;
    private readonly ILogger<ChatAttachmentReferenceVerifier> _logger;

    public ChatAttachmentReferenceVerifier(
        IR2StorageService storageService,
        ILogger<ChatAttachmentReferenceVerifier> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<bool> IsServerIssuedAttachmentAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return false;
        }

        try
        {
            await _storageService.GetObjectMetadataAsync(objectKey, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex)
            when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to verify marketplace chat attachment reference in durable storage | ObjectKey: {ObjectKey}",
                objectKey);
            throw new InvalidOperationException("Attachment storage is unavailable", ex);
        }
    }
}
