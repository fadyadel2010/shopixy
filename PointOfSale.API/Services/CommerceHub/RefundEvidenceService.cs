using System.Net;
using System.Text.Json;
using Amazon.S3;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public interface IRefundEvidenceService
{
    Task<RefundEvidenceUploadLease> CreateUploadLeaseAsync(
        Guid customerId,
        RefundEvidenceUploadUrlRequestDto request,
        CancellationToken cancellationToken = default);

    Task<RefundEvidenceUploadedMedia> UploadEvidenceAsync(
        Guid customerId,
        IFormFile file,
        string sha256,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommerceHubRefundEvidenceMedia>> ValidateOwnedMediaAsync(
        Guid customerId,
        IReadOnlyCollection<string> mediaUrls,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>>> GetEvidenceLookupAsync(
        IEnumerable<Guid> refundRequestIds,
        CancellationToken cancellationToken = default);

    Task<string> GetCustomerMediaAccessUrlAsync(
        Guid customerId,
        Guid mediaId,
        CancellationToken cancellationToken = default);

    Guid? TryParseMediaId(string? mediaUrl);
}

public sealed record RefundEvidenceUploadLease(
    Guid MediaId,
    string UploadUrl,
    string UploadMethod,
    string ContentType);

public sealed record RefundEvidenceUploadedMedia(Guid MediaId);

public sealed class RefundEvidenceService : IRefundEvidenceService
{
    private const int PendingUploadLimitMultiplier = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly IR2StorageService _storageService;
    private readonly IFileValidationService _fileValidationService;
    private readonly RefundSettings _settings;
    private readonly ILogger<RefundEvidenceService> _logger;

    public RefundEvidenceService(
        ApplicationDbContext dbContext,
        IR2StorageService storageService,
        IFileValidationService fileValidationService,
        IOptions<RefundSettings> settings,
        ILogger<RefundEvidenceService> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _fileValidationService = fileValidationService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<RefundEvidenceUploadLease> CreateUploadLeaseAsync(
        Guid customerId,
        RefundEvidenceUploadUrlRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedContentType = NormalizeContentType(request.ContentType);
        var validation = ValidateContentType(normalizedContentType);
        var sanitizedFileName = SanitizeFileName(request.FileName, validation.Extension);
        var expectedSizeBytes = ValidateRequestedFileSize(request.FileSizeBytes, validation);
        var normalizedHash = NormalizeSha256(request.Sha256);
        var nowUtc = DateTime.UtcNow;

        await EnsurePendingUploadLimitAsync(customerId, nowUtc, cancellationToken);
        await RejectDuplicateTemporaryUploadAsync(
            customerId,
            normalizedContentType,
            expectedSizeBytes,
            normalizedHash,
            cancellationToken);

        var mediaId = Guid.NewGuid();
        var uploadTarget = await _storageService.CreateUploadUrlAsync(
            normalizedContentType,
            $"commercehub/refunds/{customerId:N}/staged/{nowUtc:yyyy/MM/dd}",
            TimeSpan.FromMinutes(Math.Max(1, _settings.UploadUrlExpiryMinutes)),
            cancellationToken);

        _dbContext.CommerceHubRefundEvidenceMedia.Add(new CommerceHubRefundEvidenceMedia
        {
            Id = mediaId,
            CustomerId = customerId,
            ObjectKey = uploadTarget.ObjectKey,
            OriginalFileName = sanitizedFileName,
            ContentType = normalizedContentType,
            MediaType = validation.MediaType,
            SizeBytes = 0,
            ExpectedSizeBytes = expectedSizeBytes,
            ContentHash = normalizedHash,
            MetadataJson = SerializeMetadata(
                mediaId,
                validation.MediaType,
                normalizedContentType,
                0,
                normalizedHash,
                uploadedAtUtc: null,
                sanitizedFileName,
                isTemporary: true,
                thumbnailUrl: null),
            IsTemporary = true,
            IsFlagged = false,
            ExpiresAtUtc = nowUtc.AddHours(Math.Max(1, _settings.StagedUploadExpiryHours)),
            CreatedBy = customerId,
            UpdatedBy = customerId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RefundEvidenceUploadLease(
            mediaId,
            uploadTarget.UploadUrl,
            uploadTarget.HttpMethod,
            uploadTarget.ContentType);
    }

    public async Task<RefundEvidenceUploadedMedia> UploadEvidenceAsync(
        Guid customerId,
        IFormFile file,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        await using var stream = file.OpenReadStream();
        var fileValidation = await _fileValidationService.ValidateFileAsync(
            stream,
            file.FileName,
            file.ContentType,
            FileValidationProfiles.RefundEvidence,
            cancellationToken);

        if (!fileValidation.IsValid ||
            string.IsNullOrWhiteSpace(fileValidation.CanonicalContentType) ||
            string.IsNullOrWhiteSpace(fileValidation.SanitizedFileName))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_TYPE_NOT_ALLOWED",
                "Supported refund evidence files are JPG, PNG, and MP4.",
                StatusCodes.Status400BadRequest);
        }

        var normalizedContentType = fileValidation.CanonicalContentType;
        var validation = ValidateContentType(normalizedContentType);
        var sanitizedFileName = fileValidation.SanitizedFileName;
        var expectedSizeBytes = ValidateRequestedFileSize(file.Length, validation);
        var normalizedHash = NormalizeSha256(sha256);
        var nowUtc = DateTime.UtcNow;

        await EnsurePendingUploadLimitAsync(customerId, nowUtc, cancellationToken);
        await RejectDuplicateTemporaryUploadAsync(
            customerId,
            normalizedContentType,
            expectedSizeBytes,
            normalizedHash,
            cancellationToken);

        stream.Position = 0;
        var uploadResult = await _storageService.UploadFileAsync(
            stream,
            fileValidation.CanonicalContentType,
            $"commercehub/refunds/{customerId:N}/staged/{nowUtc:yyyy/MM/dd}",
            cancellationToken);

        var mediaId = Guid.NewGuid();

        try
        {
            _dbContext.CommerceHubRefundEvidenceMedia.Add(new CommerceHubRefundEvidenceMedia
            {
                Id = mediaId,
                CustomerId = customerId,
                ObjectKey = uploadResult.ObjectKey,
                OriginalFileName = sanitizedFileName,
                ContentType = normalizedContentType,
                MediaType = validation.MediaType,
                SizeBytes = expectedSizeBytes,
                ExpectedSizeBytes = expectedSizeBytes,
                ContentHash = normalizedHash,
                MetadataJson = SerializeMetadata(
                    mediaId,
                    validation.MediaType,
                    normalizedContentType,
                    expectedSizeBytes,
                    normalizedHash,
                    nowUtc,
                    sanitizedFileName,
                    isTemporary: true,
                    thumbnailUrl: null),
                IsTemporary = true,
                IsFlagged = false,
                ExpiresAtUtc = nowUtc.AddHours(Math.Max(1, _settings.StagedUploadExpiryHours)),
                UploadedAtUtc = nowUtc,
                CreatedBy = customerId,
                UpdatedBy = customerId
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new RefundEvidenceUploadedMedia(mediaId);
        }
        catch
        {
            await DeleteObjectSafelyAsync(uploadResult.ObjectKey, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<CommerceHubRefundEvidenceMedia>> ValidateOwnedMediaAsync(
        Guid customerId,
        IReadOnlyCollection<string> mediaUrls,
        CancellationToken cancellationToken = default)
    {
        if (mediaUrls.Count == 0)
        {
            return Array.Empty<CommerceHubRefundEvidenceMedia>();
        }

        if (mediaUrls.Count > _settings.MaxEvidenceFiles)
        {
            _logger.LogWarning(
                "Refund evidence media limit exceeded during submit | CustomerId: {CustomerId} | RequestedCount: {RequestedCount} | MaxAllowed: {MaxAllowed}",
                customerId,
                mediaUrls.Count,
                _settings.MaxEvidenceFiles);

            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_LIMIT_EXCEEDED",
                $"You can attach up to {_settings.MaxEvidenceFiles} files to a refund request.",
                StatusCodes.Status400BadRequest);
        }

        var mediaIds = mediaUrls
            .Select(url => TryParseMediaId(url) ?? Guid.Empty)
            .ToList();

        if (mediaIds.Any(id => id == Guid.Empty))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_INVALID_REFERENCE",
                "One or more uploaded files could not be verified.",
                StatusCodes.Status400BadRequest);
        }

        var distinctMediaIds = mediaIds
            .Distinct()
            .ToList();

        if (distinctMediaIds.Count != mediaIds.Count)
        {
            _logger.LogWarning(
                "Duplicate refund evidence references detected during submit | CustomerId: {CustomerId}",
                customerId);

            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_DUPLICATED",
                "Duplicate evidence files are not allowed.",
                StatusCodes.Status400BadRequest);
        }

        var mediaRecords = await _dbContext.CommerceHubRefundEvidenceMedia
            .Where(media =>
                distinctMediaIds.Contains(media.Id) &&
                media.CustomerId == customerId &&
                !media.IsDeleted)
            .OrderBy(media => media.CreatedAt)
            .ThenBy(media => media.Id)
            .ToListAsync(cancellationToken);

        if (mediaRecords.Count != distinctMediaIds.Count)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_NOT_FOUND",
                "One or more uploaded files were not found for this customer.",
                StatusCodes.Status400BadRequest);
        }

        var duplicateHashes = mediaRecords
            .Where(media => !string.IsNullOrWhiteSpace(media.ContentHash))
            .GroupBy(media => media.ContentHash!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateHashes.Count > 0)
        {
            _logger.LogWarning(
                "Duplicate refund evidence hashes detected during submit | CustomerId: {CustomerId} | DuplicateHashes: {DuplicateHashes}",
                customerId,
                duplicateHashes);

            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_DUPLICATED",
                "Duplicate evidence files are not allowed.",
                StatusCodes.Status400BadRequest);
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var media in mediaRecords)
        {
            if (media.IsFlagged)
            {
                _logger.LogWarning(
                    "Flagged refund evidence blocked during submit | CustomerId: {CustomerId} | MediaId: {MediaId}",
                    customerId,
                    media.Id);

                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_FLAGGED",
                    "One or more uploaded files were flagged for review and cannot be submitted.",
                    StatusCodes.Status409Conflict);
            }

            if (media.RefundRequestId.HasValue)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_ALREADY_USED",
                    "One or more uploaded files are already attached to another refund request.",
                    StatusCodes.Status409Conflict);
            }

            if (media.ExpiresAtUtc <= nowUtc)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_EXPIRED",
                    "One or more uploaded files expired before the refund was submitted. Please upload them again.",
                    StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(media.ContentHash) || media.ExpectedSizeBytes <= 0)
            {
                MarkMediaFlagged(media, customerId, nowUtc, "Upload lease metadata is incomplete.");

                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_INVALID_REFERENCE",
                    "One or more uploaded files could not be verified. Please upload them again.",
                    StatusCodes.Status400BadRequest);
            }

            var objectMetadata = await GetObjectMetadataAsync(media.ObjectKey, cancellationToken);
            var validation = ValidateContentType(objectMetadata.ContentType);
            
            // Use ETag to ensure we validate the same object version for both metadata and content
            var objectBytes = await GetObjectPrefixAsync(media.ObjectKey, objectMetadata.ETag, cancellationToken);
            await using var objectPrefixStream = new MemoryStream(objectBytes, writable: false);
            var storedValidation = await _fileValidationService.ValidateFileAsync(
                objectPrefixStream,
                media.OriginalFileName,
                objectMetadata.ContentType,
                FileValidationProfiles.RefundEvidence,
                cancellationToken);

            if (!string.Equals(objectMetadata.ContentType, media.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                MarkMediaFlagged(media, customerId, nowUtc, "Uploaded content type did not match the approved content type.");

                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_CONTENT_TYPE_MISMATCH",
                    "One or more uploaded files do not match the approved file type.",
                    StatusCodes.Status400BadRequest);
            }

            if (objectMetadata.ContentLength <= 0)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_UPLOAD_INCOMPLETE",
                    "One or more uploaded files are still processing. Please retry the upload.",
                    StatusCodes.Status400BadRequest);
            }

            if (objectMetadata.ContentLength != media.ExpectedSizeBytes)
            {
                MarkMediaFlagged(media, customerId, nowUtc, "Uploaded content length did not match the approved size.");

                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_SIZE_MISMATCH",
                    "One or more uploaded files do not match the approved file size. Please upload them again.",
                    StatusCodes.Status400BadRequest);
            }

            if (objectMetadata.ContentLength > validation.MaxBytes)
            {
                MarkMediaFlagged(media, customerId, nowUtc, "Uploaded file exceeded the allowed size.");

                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_TOO_LARGE",
                    BuildFileTooLargeMessage(validation.MediaType, validation.MaxBytes),
                    StatusCodes.Status400BadRequest);
            }

            if (!storedValidation.IsValid ||
                !string.Equals(storedValidation.CanonicalContentType, objectMetadata.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                MarkMediaFlagged(media, customerId, nowUtc, "Uploaded file signature did not match the approved type.");

                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_SIGNATURE_MISMATCH",
                    "One or more uploaded files do not match the approved file type.",
                    StatusCodes.Status400BadRequest);
            }

            if (!string.IsNullOrWhiteSpace(objectMetadata.ChecksumSha256) &&
                !string.Equals(objectMetadata.ChecksumSha256, media.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                MarkMediaFlagged(media, customerId, nowUtc, "Uploaded checksum did not match the approved fingerprint.");

                throw new CommerceHubCheckoutException(
                    "REFUND_MEDIA_HASH_MISMATCH",
                    "One or more uploaded files failed fingerprint verification. Please upload them again.",
                    StatusCodes.Status400BadRequest);
            }

            media.MediaType = validation.MediaType;
            media.ContentType = objectMetadata.ContentType;
            media.SizeBytes = objectMetadata.ContentLength;
            media.UploadedAtUtc ??= nowUtc;
            media.MetadataJson = SerializeMetadata(
                media.Id,
                validation.MediaType,
                objectMetadata.ContentType,
                objectMetadata.ContentLength,
                media.ContentHash,
                media.UploadedAtUtc,
                media.OriginalFileName,
                media.IsTemporary,
                thumbnailUrl: null);
            media.UpdatedAt = nowUtc;
            media.UpdatedBy = customerId;
        }

        return mediaRecords;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>>> GetEvidenceLookupAsync(
        IEnumerable<Guid> refundRequestIds,
        CancellationToken cancellationToken = default)
    {
        var refundIds = refundRequestIds
            .Distinct()
            .ToList();

        if (refundIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>>();
        }

        var evidenceItems = await _dbContext.CommerceHubRefundEvidenceMedia
            .AsNoTracking()
            .Where(media =>
                media.RefundRequestId.HasValue &&
                refundIds.Contains(media.RefundRequestId.Value) &&
                !media.IsDeleted)
            .OrderBy(media => media.CreatedAt)
            .ThenBy(media => media.Id)
            .ToListAsync(cancellationToken);

        var lookup = new Dictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>>();
        foreach (var group in evidenceItems.GroupBy(media => media.RefundRequestId!.Value))
        {
            var mediaDtos = await Task.WhenAll(group.Select(media => MapMediaDtoAsync(media, cancellationToken)));
            lookup[group.Key] = mediaDtos;
        }

        return lookup;
    }

    public async Task<string> GetCustomerMediaAccessUrlAsync(
        Guid customerId,
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        var media = await _dbContext.CommerceHubRefundEvidenceMedia
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == mediaId && item.CustomerId == customerId && !item.IsDeleted,
                cancellationToken);

        if (media == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_NOT_FOUND",
                "Refund evidence file was not found.",
                StatusCodes.Status404NotFound);
        }

        if (media.IsFlagged)
        {
            _logger.LogWarning(
                "Flagged refund evidence access blocked | CustomerId: {CustomerId} | MediaId: {MediaId}",
                customerId,
                mediaId);

            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_FLAGGED",
                "Refund evidence file was flagged for review.",
                StatusCodes.Status409Conflict);
        }

        if (media.IsTemporary &&
            !media.RefundRequestId.HasValue &&
            media.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_NOT_FOUND",
                "Refund evidence file is no longer available.",
                StatusCodes.Status404NotFound);
        }

        await EnsureObjectExistsAsync(media.ObjectKey, cancellationToken);

        return await _storageService.CreateDownloadUrlAsync(
            media.ObjectKey,
            GetEvidenceAccessExpiry(),
            cancellationToken);
    }

    public Guid? TryParseMediaId(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            return null;
        }

        var trimmed = mediaUrl.Trim();
        if (Guid.TryParse(trimmed, out var directGuid))
        {
            return directGuid;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            Uri.TryCreate($"https://placeholder{(trimmed.StartsWith('/') ? string.Empty : "/")}{trimmed}", UriKind.Absolute, out absoluteUri);
        }

        var lastSegment = absoluteUri?.Segments.LastOrDefault()?.Trim('/');
        if (Guid.TryParse(lastSegment, out var segmentGuid))
        {
            return segmentGuid;
        }

        return null;
    }

    private async Task<RefundEvidenceMediaDto> MapMediaDtoAsync(
        CommerceHubRefundEvidenceMedia media,
        CancellationToken cancellationToken)
    {
        var accessUrl = await _storageService.CreateDownloadUrlAsync(
            media.ObjectKey,
            GetEvidenceAccessExpiry(),
            cancellationToken);

        string? thumbnailUrl = null;
        if (!string.IsNullOrWhiteSpace(media.ThumbnailObjectKey))
        {
            thumbnailUrl = await _storageService.CreateDownloadUrlAsync(
                media.ThumbnailObjectKey,
                GetEvidenceAccessExpiry(),
                cancellationToken);
        }

        return new RefundEvidenceMediaDto
        {
            MediaId = media.Id,
            Url = accessUrl,
            ThumbnailUrl = thumbnailUrl,
            MediaType = media.MediaType,
            MimeType = media.ContentType,
            SizeBytes = media.SizeBytes,
            Hash = media.ContentHash,
            UploadedAtUtc = media.UploadedAtUtc,
            FileName = media.OriginalFileName
        };
    }

    private async Task EnsurePendingUploadLimitAsync(
        Guid customerId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var activePendingUploads = await _dbContext.CommerceHubRefundEvidenceMedia
            .AsNoTracking()
            .Where(media =>
                media.CustomerId == customerId &&
                media.IsTemporary &&
                !media.IsDeleted &&
                !media.RefundRequestId.HasValue &&
                media.ExpiresAtUtc > nowUtc)
            .CountAsync(cancellationToken);

        if (activePendingUploads >= _settings.MaxEvidenceFiles)
        {
            _logger.LogWarning(
                "Excessive refund evidence upload attempts detected | CustomerId: {CustomerId} | ActivePendingUploads: {ActivePendingUploads}",
                customerId,
                activePendingUploads);
        }

        var hardPendingLimit = Math.Max(_settings.MaxEvidenceFiles, _settings.MaxEvidenceFiles * PendingUploadLimitMultiplier);
        if (activePendingUploads >= hardPendingLimit)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_PENDING_LIMIT_EXCEEDED",
                "Too many pending refund evidence uploads were found for this account. Please finish a refund request or wait for older uploads to expire.",
                StatusCodes.Status429TooManyRequests);
        }
    }

    private async Task RejectDuplicateTemporaryUploadAsync(
        Guid customerId,
        string contentType,
        long expectedSizeBytes,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.CommerceHubRefundEvidenceMedia
            .AsNoTracking()
            .Where(media =>
                media.CustomerId == customerId &&
                media.IsTemporary &&
                !media.IsDeleted &&
                !media.RefundRequestId.HasValue &&
                media.ExpiresAtUtc > DateTime.UtcNow &&
                media.ExpectedSizeBytes == expectedSizeBytes &&
                media.ContentType == contentType &&
                media.ContentHash == contentHash)
            .OrderByDescending(media => media.CreatedAt)
            .Take(3)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            try
            {
                var metadata = await _storageService.GetObjectMetadataAsync(candidate.ObjectKey, cancellationToken);
                if (metadata.ContentLength > 0)
                {
                    _logger.LogWarning(
                        "Duplicate refund evidence upload rejected | CustomerId: {CustomerId} | ExistingMediaId: {ExistingMediaId}",
                        customerId,
                        candidate.Id);

                    throw new CommerceHubCheckoutException(
                        "REFUND_MEDIA_DUPLICATE_UPLOAD",
                        "This file was already uploaded as refund evidence. Please reuse the existing upload or choose another file.",
                        StatusCodes.Status409Conflict);
                }
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Ignore stale staged rows that never finished uploading.
            }
        }
    }

    private async Task<R2ObjectMetadata> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _storageService.GetObjectMetadataAsync(objectKey, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                ex,
                "Refund evidence object missing from storage | ObjectKey: {ObjectKey}",
                objectKey);

            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_UPLOAD_INCOMPLETE",
                "One or more uploaded files are missing from storage. Please upload them again.",
                StatusCodes.Status400BadRequest);
        }
    }

    private async Task<byte[]> GetObjectPrefixAsync(
        string objectKey,
        string? ifMatchETag,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _storageService.GetObjectBytesAsync(
                objectKey,
                FileValidationService.SignatureProbeLength,
                ifMatchETag,
                cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                ex,
                "Refund evidence object missing when reading signature bytes | ObjectKey: {ObjectKey}",
                objectKey);

            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_UPLOAD_INCOMPLETE",
                "One or more uploaded files are missing from storage. Please upload them again.",
                StatusCodes.Status400BadRequest);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            _logger.LogWarning(
                ex,
                "Refund evidence object ETag mismatch when reading signature bytes | ObjectKey: {ObjectKey}",
                objectKey);

            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_UPLOAD_CHANGED",
                "One or more uploaded files changed during validation. Please upload them again.",
                StatusCodes.Status400BadRequest);
        }
    }

    private async Task EnsureObjectExistsAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.GetObjectMetadataAsync(objectKey, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_NOT_FOUND",
                "Refund evidence file is no longer available.",
                StatusCodes.Status404NotFound);
        }
    }

    private async Task DeleteObjectSafelyAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.DeleteObjectAsync(objectKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete refund evidence object after database failure | ObjectKey: {ObjectKey}",
                objectKey);
        }
    }

    private void MarkMediaFlagged(
        CommerceHubRefundEvidenceMedia media,
        Guid customerId,
        DateTime nowUtc,
        string reason)
    {
        media.IsFlagged = true;
        media.UpdatedAt = nowUtc;
        media.UpdatedBy = customerId;
        media.MetadataJson = SerializeMetadata(
            media.Id,
            media.MediaType,
            media.ContentType,
            media.SizeBytes,
            media.ContentHash,
            media.UploadedAtUtc,
            media.OriginalFileName,
            media.IsTemporary,
            thumbnailUrl: null);

        _logger.LogWarning(
            "Refund evidence flagged | MediaId: {MediaId} | CustomerId: {CustomerId} | Reason: {Reason}",
            media.Id,
            customerId,
            reason);
    }

    private TimeSpan GetEvidenceAccessExpiry() =>
        TimeSpan.FromMinutes(Math.Clamp(_settings.EvidenceAccessUrlExpiryMinutes, 5, 15));

    private (string MediaType, string Extension, long MaxBytes) ValidateContentType(string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => ("image", ".jpg", _settings.MaxImageBytes),
            "image/png" => ("image", ".png", _settings.MaxImageBytes),
            "video/mp4" => ("video", ".mp4", _settings.MaxVideoBytes),
            _ => throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_TYPE_NOT_ALLOWED",
                "Supported refund evidence files are JPG, PNG, and MP4.",
                StatusCodes.Status400BadRequest)
        };
    }

    private long ValidateRequestedFileSize(
        long requestedSizeBytes,
        (string MediaType, string Extension, long MaxBytes) validation)
    {
        if (requestedSizeBytes <= 0)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_SIZE_REQUIRED",
                "File size is required before uploading refund evidence.",
                StatusCodes.Status400BadRequest);
        }

        if (requestedSizeBytes > validation.MaxBytes)
        {
            _logger.LogWarning(
                "Refund evidence upload rejected due to file size | MediaType: {MediaType} | RequestedSizeBytes: {RequestedSizeBytes} | MaxBytes: {MaxBytes}",
                validation.MediaType,
                requestedSizeBytes,
                validation.MaxBytes);

            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_TOO_LARGE",
                BuildFileTooLargeMessage(validation.MediaType, validation.MaxBytes),
                StatusCodes.Status400BadRequest);
        }

        return requestedSizeBytes;
    }

    private static string BuildFileTooLargeMessage(string mediaType, long maxBytes)
    {
        var maxMegaBytes = Math.Max(1, maxBytes / (1024 * 1024));
        return string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase)
            ? $"Video files must be {maxMegaBytes}MB or smaller."
            : $"Image files must be {maxMegaBytes}MB or smaller.";
    }

    private static string NormalizeContentType(string? contentType)
    {
        return (contentType ?? string.Empty)
            .Split(';', 2)[0]
            .Trim()
            .ToLowerInvariant();
    }

    private static string NormalizeSha256(string? hash)
    {
        var normalized = (hash ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (normalized.Length != 64 || normalized.Any(ch => !IsHexCharacter(ch)))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_HASH_INVALID",
                "A valid SHA-256 fingerprint is required for refund evidence uploads.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static bool IsHexCharacter(char value) =>
        (value >= '0' && value <= '9') ||
        (value >= 'a' && value <= 'f');

    private static string SanitizeFileName(string? fileName, string extension)
    {
        var candidate = string.IsNullOrWhiteSpace(fileName)
            ? $"refund-evidence{extension}"
            : fileName.Trim();

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(candidate
            .Where(ch => !invalidChars.Contains(ch))
            .ToArray());

        sanitized = sanitized
            .Replace("..", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("\\", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = $"refund-evidence{extension}";
        }

        if (!sanitized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            sanitized = $"{Path.GetFileNameWithoutExtension(sanitized)}{extension}";
        }

        return sanitized.Length <= 255 ? sanitized : sanitized[..255];
    }

    internal static string SerializeMetadata(
        Guid mediaId,
        string mediaType,
        string mimeType,
        long sizeBytes,
        string? hash,
        DateTime? uploadedAtUtc,
        string fileName,
        bool isTemporary,
        string? thumbnailUrl)
    {
        return JsonSerializer.Serialize(new
        {
            url = $"/api/customers/refunds/media/{mediaId}",
            type = mimeType,
            mediaType,
            size = sizeBytes,
            hash,
            uploadedAt = uploadedAtUtc,
            fileName,
            thumbnailUrl,
            isTemporary
        });
    }
}
