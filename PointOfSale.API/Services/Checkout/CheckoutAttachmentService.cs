using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Checkout;

public interface ICheckoutAttachmentService
{
    Task<CheckoutAttachmentUploadLeaseResponseDto> CreateUploadLeaseAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        CheckoutAttachmentUploadLeaseRequestDto request,
        CancellationToken cancellationToken = default);

    Task<CheckoutAttachmentDto> ConfirmUploadAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        CheckoutAttachmentConfirmRequestDto request,
        CancellationToken cancellationToken = default);

    Task RemovePendingAttachmentAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        Guid cartId,
        Guid fieldId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CheckoutAttachment>> ValidateAndAttachAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        Guid cartId,
        Guid orderId,
        CheckoutFieldDefinition field,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default);
}

public sealed class CheckoutAttachmentService : ICheckoutAttachmentService
{
    internal const int UploadUrlExpiryMinutes = 15;
    private const int PendingUploadExpiryHours = 24;
    private const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024;
    private const int DefaultMultiImageMaxFiles = 5;
    private const int PendingUploadLimitMultiplier = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IR2StorageService _storageService;
    private readonly ICartService _cartService;
    private readonly IFileValidationService _fileValidationService;
    private readonly ILogger<CheckoutAttachmentService> _logger;

    public CheckoutAttachmentService(
        ApplicationDbContext dbContext,
        IR2StorageService storageService,
        ICartService cartService,
        IFileValidationService fileValidationService,
        ILogger<CheckoutAttachmentService> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _cartService = cartService;
        _fileValidationService = fileValidationService;
        _logger = logger;
    }

    public async Task<CheckoutAttachmentUploadLeaseResponseDto> CreateUploadLeaseAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        CheckoutAttachmentUploadLeaseRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCheckoutOwner(ownerId, ownerType, request.CartId);

        var field = await ResolveUploadFieldAsync(tenantId, request.FieldId, cancellationToken);
        var policy = BuildPolicy(field);
        var contentType = NormalizeContentType(request.ContentType);
        var allowedType = ResolveAllowedFileType(contentType, policy.AllowedFileTypes);
        ValidateFileSize(request.FileSizeBytes, policy);
        await ValidateCartAuthorityAsync(
            tenantId,
            ownerId,
            ownerType,
            request.CartId,
            "checkout-attachment-lease",
            cancellationToken);

        var activeCount = await CountActiveAttachmentsAsync(
            tenantId,
            ownerId,
            ownerType,
            request.CartId,
            field.Id,
            DateTime.UtcNow,
            cancellationToken);
        var hardPendingLimit = Math.Max(policy.MaxFiles, policy.MaxFiles * PendingUploadLimitMultiplier);
        if (activeCount >= hardPendingLimit)
        {
            throw new CheckoutDynamicFieldValidationException(
                "CHECKOUT_ATTACHMENT_PENDING_LIMIT_EXCEEDED",
                "Too many pending checkout uploads were found. Remove older uploads or wait for them to expire.");
        }

        var fileName = FileValidationService.SanitizeFileName(
            request.FileName,
            GetPrimaryExtension(allowedType));
        var nowUtc = DateTime.UtcNow;
        var uploadExpiresAtUtc = nowUtc.AddMinutes(UploadUrlExpiryMinutes);
        var uploadTarget = await _storageService.CreateUploadUrlAsync(
            GetCanonicalContentType(allowedType),
            $"tenants/{tenantId:N}/checkout/{request.CartId:N}/fields/{field.Id:N}/attachments/{nowUtc:yyyy/MM/dd}",
            TimeSpan.FromMinutes(UploadUrlExpiryMinutes),
            cancellationToken);

        var attachment = new CheckoutAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = request.CartId,
            OwnerId = ownerId.Trim(),
            OwnerType = ownerType.Trim().ToLowerInvariant(),
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim(),
            OnlineCustomerId = customerId,
            CheckoutFieldDefinitionId = field.Id,
            ObjectKey = uploadTarget.ObjectKey,
            OriginalFileName = fileName,
            ContentType = GetCanonicalContentType(allowedType),
            MediaType = IsImageType(allowedType) ? "image" : "file",
            ExpectedSizeBytes = request.FileSizeBytes,
            SizeBytes = 0,
            Status = CheckoutAttachmentStatus.Pending,
            ExpiresAtUtc = uploadExpiresAtUtc,
            CreatedAt = nowUtc,
            MetadataJson = SerializeMetadata(null, fileName, GetCanonicalContentType(allowedType), 0, CheckoutAttachmentStatus.Pending)
        };

        _dbContext.CheckoutAttachments.Add(attachment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Checkout attachment lease created | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status} | ContentType: {ContentType} | SizeBytes: {SizeBytes}",
            tenantId,
            request.CartId,
            field.Id,
            attachment.Id,
            attachment.Status,
            attachment.ContentType,
            request.FileSizeBytes);

        return new CheckoutAttachmentUploadLeaseResponseDto
        {
            AttachmentId = attachment.Id,
            UploadUrl = uploadTarget.UploadUrl,
            UploadMethod = uploadTarget.HttpMethod,
            ContentType = uploadTarget.ContentType,
            ExpiresAtUtc = uploadExpiresAtUtc,
            AttachmentExpiresAtUtc = attachment.ExpiresAtUtc
        };
    }

    public async Task<CheckoutAttachmentDto> ConfirmUploadAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        CheckoutAttachmentConfirmRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCheckoutOwner(ownerId, ownerType, request.CartId);

        var field = await ResolveUploadFieldAsync(tenantId, request.FieldId, cancellationToken);
        await ValidateCartAuthorityAsync(
            tenantId,
            ownerId,
            ownerType,
            request.CartId,
            "checkout-attachment-confirm",
            cancellationToken);
        var attachment = await ResolveOwnedAttachmentAsync(
            tenantId,
            ownerId,
            ownerType,
            sessionId,
            customerId,
            request.CartId,
            request.FieldId,
            request.AttachmentId,
            cancellationToken);
        var expectedStatus = attachment.Status;

        try
        {
            var persistExpectedStatus = await ConfirmAttachmentObjectAsync(attachment, field, expectedStatus, cancellationToken);
            await PersistAttachmentStateAsync(attachment, persistExpectedStatus, cancellationToken);
        }
        catch (CheckoutDynamicFieldValidationException) when (
            attachment.Status is CheckoutAttachmentStatus.Expired or CheckoutAttachmentStatus.Failed)
        {
            _logger.LogWarning(
                "Checkout attachment finalization rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCategory: validation",
                tenantId,
                request.CartId,
                request.FieldId,
                request.AttachmentId,
                attachment.Status);
            await PersistTerminalFailureAsync(attachment, expectedStatus, CancellationToken.None);
            throw;
        }
        catch (Exception) when (attachment.Status == CheckoutAttachmentStatus.Failed)
        {
            _logger.LogError(
                "Checkout attachment finalization failed | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCategory: storage",
                tenantId,
                request.CartId,
                request.FieldId,
                request.AttachmentId,
                attachment.Status);
            await PersistTerminalFailureAsync(attachment, expectedStatus, CancellationToken.None);
            throw;
        }

        _logger.LogInformation(
            "Checkout attachment finalized | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status}",
            tenantId,
            request.CartId,
            request.FieldId,
            request.AttachmentId,
            attachment.Status);

        return Map(attachment);
    }

    public async Task RemovePendingAttachmentAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        Guid cartId,
        Guid fieldId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateCheckoutOwner(ownerId, ownerType, cartId);
        var attachment = await ResolveOwnedAttachmentAsync(
            tenantId,
            ownerId,
            ownerType,
            sessionId,
            customerId,
            cartId,
            fieldId,
            attachmentId,
            cancellationToken);

        if (attachment.Status == CheckoutAttachmentStatus.Attached)
        {
            _logger.LogWarning(
                "Checkout attachment removal rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCategory: invalid_state",
                tenantId,
                cartId,
                fieldId,
                attachmentId,
                attachment.Status);
            throw new CheckoutDynamicFieldValidationException(
                "CHECKOUT_ATTACHMENT_ALREADY_ATTACHED",
                "This uploaded file is already attached to an order.");
        }

        if (attachment.Status is not (CheckoutAttachmentStatus.Pending or CheckoutAttachmentStatus.Uploaded or CheckoutAttachmentStatus.Finalized))
        {
            _logger.LogWarning(
                "Checkout attachment removal rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCategory: invalid_state",
                tenantId,
                cartId,
                fieldId,
                attachmentId,
                attachment.Status);
            throw new CheckoutDynamicFieldValidationException(
                "CHECKOUT_ATTACHMENT_INVALID_STATE",
                "This uploaded file cannot be removed in its current state.");
        }

        if (_dbContext.Database.IsRelational())
        {
            var claimed = await _dbContext.CheckoutAttachments
                .IgnoreQueryFilters()
                .Where(item => item.Id == attachment.Id &&
                               !item.IsDeleted &&
                               item.OrderId == null &&
                               (item.Status == CheckoutAttachmentStatus.Pending ||
                                item.Status == CheckoutAttachmentStatus.Uploaded ||
                                item.Status == CheckoutAttachmentStatus.Finalized))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, CheckoutAttachmentStatus.Deleting)
                    .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
            if (claimed != 1)
            {
                _logger.LogWarning(
                    "Checkout attachment removal claim failed | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | ExpectedStatuses: {ExpectedStatuses} | ErrorCategory: conflict",
                    tenantId,
                    cartId,
                    fieldId,
                    attachmentId,
                    "Pending,Uploaded,Finalized");
                throw new CheckoutDynamicFieldValidationException(
                    "CHECKOUT_ATTACHMENT_INVALID_STATE",
                    "This uploaded file changed state before it could be removed.");
            }
            _dbContext.Entry(attachment).State = EntityState.Detached;
        }
        else
        {
            attachment.Status = CheckoutAttachmentStatus.Deleting;
            attachment.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await DeleteAttachmentObjectsAsync(attachment, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Checkout attachment object was already missing during removal | AttachmentId: {AttachmentId}", attachment.Id);
        }

        var deletedAtUtc = DateTime.UtcNow;
        var deletedMetadata = SerializeMetadata(attachment, attachment.OriginalFileName, attachment.ContentType, attachment.SizeBytes, CheckoutAttachmentStatus.Deleted);
        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.CheckoutAttachments
                .IgnoreQueryFilters()
                .Where(item => item.Id == attachment.Id && item.Status == CheckoutAttachmentStatus.Deleting && !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, CheckoutAttachmentStatus.Deleted)
                    .SetProperty(item => item.IsDeleted, true)
                    .SetProperty(item => item.UpdatedAt, deletedAtUtc)
                    .SetProperty(item => item.MetadataJson, deletedMetadata), cancellationToken);
        }
        else
        {
            attachment.Status = CheckoutAttachmentStatus.Deleted;
            attachment.IsDeleted = true;
            attachment.UpdatedAt = deletedAtUtc;
            attachment.MetadataJson = deletedMetadata;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Checkout attachment removed | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status}",
            tenantId,
            cartId,
            fieldId,
            attachmentId,
            CheckoutAttachmentStatus.Deleted);
    }

    private async Task DeleteAttachmentObjectsAsync(CheckoutAttachment attachment, CancellationToken cancellationToken)
    {
        await DeleteAttachmentObjectIfPresentAsync(attachment, attachment.ObjectKey, cancellationToken);

        if (!string.IsNullOrWhiteSpace(attachment.FinalObjectKey) &&
            !string.Equals(attachment.FinalObjectKey, attachment.ObjectKey, StringComparison.Ordinal))
        {
            await DeleteAttachmentObjectIfPresentAsync(attachment, attachment.FinalObjectKey, cancellationToken);
        }
    }

    private async Task DeleteAttachmentObjectIfPresentAsync(
        CheckoutAttachment attachment,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.DeleteObjectAsync(objectKey, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Checkout attachment object was already missing during removal | AttachmentId: {AttachmentId}", attachment.Id);
        }
    }

    public async Task<IReadOnlyList<CheckoutAttachment>> ValidateAndAttachAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        Guid cartId,
        Guid orderId,
        CheckoutFieldDefinition field,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default)
    {
        ValidateCheckoutOwner(ownerId, ownerType, cartId);
        var policy = BuildPolicy(field);
        var distinctIds = attachmentIds.Distinct().ToArray();
        if (distinctIds.Length != attachmentIds.Count)
        {
            _logger.LogWarning(
                "Checkout attachment attach rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | OrderId: {OrderId} | ErrorCode: CHECKOUT_ATTACHMENT_DUPLICATE | ErrorCategory: validation",
                tenantId,
                cartId,
                field.Id,
                orderId);
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_DUPLICATE", "Uploaded files must not be submitted more than once.");
        }

        if (distinctIds.Length > policy.MaxFiles)
        {
            _logger.LogWarning(
                "Checkout attachment attach rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | OrderId: {OrderId} | SubmittedCount: {SubmittedCount} | MaxFiles: {MaxFiles} | ErrorCode: CHECKOUT_ATTACHMENT_TOO_MANY | ErrorCategory: validation",
                tenantId,
                cartId,
                field.Id,
                orderId,
                distinctIds.Length,
                policy.MaxFiles);
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_TOO_MANY", $"{field.Label} allows at most {policy.MaxFiles} file(s).");
        }

        var attachments = await _dbContext.CheckoutAttachments
            .IgnoreQueryFilters()
            .Where(attachment =>
                distinctIds.Contains(attachment.Id) &&
                attachment.TenantId == tenantId &&
                attachment.CartId == cartId &&
                attachment.CheckoutFieldDefinitionId == field.Id &&
                attachment.OwnerId == ownerId.Trim() &&
                attachment.OwnerType == ownerType.Trim().ToLowerInvariant() &&
                !attachment.IsDeleted)
            .OrderBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .ToListAsync(cancellationToken);

        if (attachments.Count != distinctIds.Length)
        {
            _logger.LogWarning(
                "Checkout attachment attach rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | OrderId: {OrderId} | SubmittedCount: {SubmittedCount} | MatchedCount: {MatchedCount} | ErrorCode: CHECKOUT_ATTACHMENT_NOT_FOUND | ErrorCategory: authorization",
                tenantId,
                cartId,
                field.Id,
                orderId,
                distinctIds.Length,
                attachments.Count);
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_NOT_FOUND", "One or more uploaded files could not be verified for this checkout.");
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var attachment in attachments)
        {
            EnsureIdentityMatches(attachment, ownerType, sessionId, customerId);
            if (attachment.ExpiresAtUtc <= nowUtc)
            {
                attachment.Status = CheckoutAttachmentStatus.Expired;
                _logger.LogWarning(
                    "Checkout attachment attach rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | OrderId: {OrderId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCode: CHECKOUT_ATTACHMENT_EXPIRED | ErrorCategory: invalid_state",
                    tenantId,
                    cartId,
                    field.Id,
                    orderId,
                    attachment.Id,
                    attachment.Status);
                throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_EXPIRED", "One or more uploaded files expired. Please upload them again.");
            }

            if (attachment.Status == CheckoutAttachmentStatus.Pending)
            {
                _logger.LogWarning(
                    "Checkout attachment attach rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | OrderId: {OrderId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCode: CHECKOUT_ATTACHMENT_UPLOAD_INCOMPLETE | ErrorCategory: invalid_state",
                    tenantId,
                    cartId,
                    field.Id,
                    orderId,
                    attachment.Id,
                    attachment.Status);
                throw new CheckoutDynamicFieldValidationException(
                    "CHECKOUT_ATTACHMENT_UPLOAD_INCOMPLETE",
                    "One or more uploaded files must be confirmed before checkout.");
            }

            if (attachment.Status != CheckoutAttachmentStatus.Finalized ||
                string.IsNullOrWhiteSpace(attachment.FinalObjectKey))
            {
                _logger.LogWarning(
                    "Checkout attachment attach rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | OrderId: {OrderId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCode: CHECKOUT_ATTACHMENT_INVALID_STATE | ErrorCategory: invalid_state",
                    tenantId,
                    cartId,
                    field.Id,
                    orderId,
                    attachment.Id,
                    attachment.Status);
                throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_INVALID_STATE", "One or more uploaded files cannot be attached to this order.");
            }
        }

        if (_dbContext.Database.IsRelational() && attachments.Count > 0)
        {
            var claimed = await _dbContext.CheckoutAttachments
                .IgnoreQueryFilters()
                .Where(item => distinctIds.Contains(item.Id) &&
                               item.Status == CheckoutAttachmentStatus.Finalized &&
                               item.FinalObjectKey != null &&
                               item.OrderId == null &&
                               !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.OrderId, orderId)
                    .SetProperty(item => item.Status, CheckoutAttachmentStatus.Attached)
                    .SetProperty(item => item.AttachedAtUtc, nowUtc)
                    .SetProperty(item => item.UpdatedAt, nowUtc), cancellationToken);
            if (claimed != attachments.Count)
            {
                _logger.LogWarning(
                    "Checkout attachment attach claim failed | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | OrderId: {OrderId} | ExpectedCount: {ExpectedCount} | ClaimedCount: {ClaimedCount} | ErrorCategory: conflict",
                    tenantId,
                    cartId,
                    field.Id,
                    orderId,
                    attachments.Count,
                    claimed);
                throw new CheckoutDynamicFieldValidationException(
                    "CHECKOUT_ATTACHMENT_INVALID_STATE",
                    "One or more uploaded files changed state before they could be attached.");
            }
        }

        foreach (var attachment in attachments)
        {
            attachment.OrderId = orderId;
            attachment.Status = CheckoutAttachmentStatus.Attached;
            attachment.AttachedAtUtc = nowUtc;
            attachment.UpdatedAt = nowUtc;
            attachment.MetadataJson = SerializeMetadata(attachment, attachment.OriginalFileName, attachment.ContentType, attachment.SizeBytes, attachment.Status);
        }

        _logger.LogInformation(
            "Checkout attachments attached to order | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | OrderId: {OrderId} | AttachmentCount: {AttachmentCount} | Status: {Status}",
            tenantId,
            cartId,
            field.Id,
            orderId,
            attachments.Count,
            CheckoutAttachmentStatus.Attached);

        return attachments;
    }

    private async Task<CheckoutAttachmentStatus> ConfirmAttachmentObjectAsync(
        CheckoutAttachment attachment,
        CheckoutFieldDefinition field,
        CheckoutAttachmentStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        if (attachment.Status == CheckoutAttachmentStatus.Attached || attachment.Status == CheckoutAttachmentStatus.Deleted)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_INVALID_STATE", "Uploaded file state is not valid for this operation.");
        }

        if (attachment.Status == CheckoutAttachmentStatus.Finalized)
        {
            if (string.IsNullOrWhiteSpace(attachment.FinalObjectKey))
            {
                attachment.Status = CheckoutAttachmentStatus.Failed;
                throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_INVALID_STATE", "Uploaded file finalization evidence is incomplete.");
            }

            return CheckoutAttachmentStatus.Finalized;
        }

        if (attachment.ExpiresAtUtc <= DateTime.UtcNow)
        {
            attachment.Status = CheckoutAttachmentStatus.Expired;
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_EXPIRED", "Uploaded file expired before it was confirmed.");
        }

        R2ObjectMetadata metadata;
        try
        {
            metadata = await _storageService.GetObjectMetadataAsync(attachment.ObjectKey, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_UPLOAD_INCOMPLETE", "Uploaded file is not available yet. Please retry the upload.");
        }

        var policy = BuildPolicy(field);
        var normalizedContentType = NormalizeContentType(metadata.ContentType);
        if (!string.Equals(normalizedContentType, attachment.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            attachment.Status = CheckoutAttachmentStatus.Failed;
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_CONTENT_TYPE_MISMATCH", "Uploaded file type does not match the approved upload.");
        }

        AllowedFileType allowedType;
        try
        {
            allowedType = ResolveAllowedFileType(normalizedContentType, policy.AllowedFileTypes);
        }
        catch (CheckoutDynamicFieldValidationException)
        {
            attachment.Status = CheckoutAttachmentStatus.Failed;
            throw;
        }

        if (metadata.ContentLength <= 0 || metadata.ContentLength != attachment.ExpectedSizeBytes)
        {
            attachment.Status = CheckoutAttachmentStatus.Failed;
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_SIZE_MISMATCH", "Uploaded file size does not match the approved upload.");
        }

        if (string.IsNullOrWhiteSpace(metadata.ETag))
        {
            attachment.Status = CheckoutAttachmentStatus.Failed;
            throw new CheckoutDynamicFieldValidationException(
                "CHECKOUT_ATTACHMENT_INTEGRITY_EVIDENCE_MISSING",
                "Uploaded file integrity could not be verified.");
        }

        try
        {
            ValidateFileSize(metadata.ContentLength, policy);
        }
        catch (CheckoutDynamicFieldValidationException)
        {
            attachment.Status = CheckoutAttachmentStatus.Failed;
            throw;
        }
        var prefix = await _storageService.GetObjectBytesAsync(
            attachment.ObjectKey,
            FileValidationService.SignatureProbeLength,
            metadata.ETag,
            cancellationToken);

        await using var prefixStream = new MemoryStream(prefix, writable: false);
        var validation = await _fileValidationService.ValidateFileAsync(
            prefixStream,
            attachment.OriginalFileName,
            normalizedContentType,
            new[] { allowedType },
            cancellationToken);

        if (!validation.IsValid || !string.Equals(validation.CanonicalContentType, normalizedContentType, StringComparison.OrdinalIgnoreCase))
        {
            attachment.Status = CheckoutAttachmentStatus.Failed;
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_SIGNATURE_MISMATCH", "Uploaded file content does not match the approved file type.");
        }

        attachment.ContentType = normalizedContentType;
        attachment.MediaType = IsImageType(allowedType) ? "image" : "file";
        attachment.SizeBytes = metadata.ContentLength;
        attachment.StagedETag = metadata.ETag;
        attachment.Status = CheckoutAttachmentStatus.Uploaded;
        attachment.UploadedAtUtc ??= DateTime.UtcNow;
        attachment.UpdatedAt = DateTime.UtcNow;
        attachment.MetadataJson = SerializeMetadata(attachment, attachment.OriginalFileName, normalizedContentType, metadata.ContentLength, attachment.Status);

        if (_dbContext.Database.IsRelational())
        {
            var claimed = await _dbContext.CheckoutAttachments
                .IgnoreQueryFilters()
                .Where(item => item.Id == attachment.Id && item.Status == expectedStatus && !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ContentType, attachment.ContentType)
                    .SetProperty(item => item.MediaType, attachment.MediaType)
                    .SetProperty(item => item.SizeBytes, attachment.SizeBytes)
                    .SetProperty(item => item.StagedETag, attachment.StagedETag)
                    .SetProperty(item => item.Status, CheckoutAttachmentStatus.Uploaded)
                    .SetProperty(item => item.UploadedAtUtc, attachment.UploadedAtUtc)
                    .SetProperty(item => item.UpdatedAt, attachment.UpdatedAt)
                    .SetProperty(item => item.MetadataJson, attachment.MetadataJson), cancellationToken);

            if (claimed != 1)
            {
                var current = await _dbContext.CheckoutAttachments
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == attachment.Id && !item.IsDeleted, cancellationToken);
                if (current?.Status == CheckoutAttachmentStatus.Finalized && !string.IsNullOrWhiteSpace(current.FinalObjectKey))
                {
                    CopyPersistedFinalization(attachment, current);
                    return CheckoutAttachmentStatus.Finalized;
                }

                throw new CheckoutDynamicFieldValidationException(
                    "CHECKOUT_ATTACHMENT_INVALID_STATE",
                    "Uploaded file state changed while finalization was being claimed.");
            }
        }

        var finalObjectKey = BuildFinalObjectKey(attachment, allowedType);
        // Persist this deterministic key on every terminal path. A provider can
        // create the copy even when its response is lost, so cleanup must know it.
        attachment.FinalObjectKey = finalObjectKey;
        R2ObjectMetadata finalMetadata;
        try
        {
            finalMetadata = await _storageService.CopyObjectAsync(
                attachment.ObjectKey,
                finalObjectKey,
                normalizedContentType,
                metadata.ETag,
                cancellationToken);
        }
        catch
        {
            _logger.LogError(
                "Checkout attachment final object copy failed | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | ErrorCategory: storage",
                attachment.TenantId,
                attachment.CartId,
                attachment.CheckoutFieldDefinitionId,
                attachment.Id);
            attachment.Status = CheckoutAttachmentStatus.Failed;
            attachment.UpdatedAt = DateTime.UtcNow;
            attachment.MetadataJson = SerializeMetadata(
                attachment,
                attachment.OriginalFileName,
                normalizedContentType,
                metadata.ContentLength,
                attachment.Status);
            throw;
        }

        if (finalMetadata.ContentLength != metadata.ContentLength ||
            !string.Equals(NormalizeContentType(finalMetadata.ContentType), normalizedContentType, StringComparison.OrdinalIgnoreCase))
        {
            attachment.Status = CheckoutAttachmentStatus.Failed;
            _logger.LogWarning(
                "Checkout attachment final object verification failed | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | ErrorCode: CHECKOUT_ATTACHMENT_FINALIZATION_MISMATCH | ErrorCategory: storage",
                attachment.TenantId,
                attachment.CartId,
                attachment.CheckoutFieldDefinitionId,
                attachment.Id);
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_FINALIZATION_MISMATCH", "Uploaded file could not be finalized safely.");
        }

        var finalizedAtUtc = DateTime.UtcNow;
        attachment.SizeBytes = finalMetadata.ContentLength;
        attachment.FinalETag = finalMetadata.ETag;
        attachment.FinalChecksumSha256 = finalMetadata.ChecksumSha256;
        attachment.Status = CheckoutAttachmentStatus.Finalized;
        attachment.UploadedAtUtc ??= finalizedAtUtc;
        attachment.FinalizedAtUtc ??= finalizedAtUtc;
        attachment.ExpiresAtUtc = finalizedAtUtc.AddHours(PendingUploadExpiryHours);
        attachment.UpdatedAt = finalizedAtUtc;
        attachment.MetadataJson = SerializeMetadata(attachment, attachment.OriginalFileName, normalizedContentType, finalMetadata.ContentLength, attachment.Status);
        return _dbContext.Database.IsRelational()
            ? CheckoutAttachmentStatus.Uploaded
            : expectedStatus;
    }

    private async Task PersistAttachmentStateAsync(
        CheckoutAttachment attachment,
        CheckoutAttachmentStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var updated = await _dbContext.CheckoutAttachments
            .IgnoreQueryFilters()
            .Where(item => item.Id == attachment.Id && item.Status == expectedStatus && !item.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ContentType, attachment.ContentType)
                .SetProperty(item => item.MediaType, attachment.MediaType)
                .SetProperty(item => item.SizeBytes, attachment.SizeBytes)
                .SetProperty(item => item.FinalObjectKey, attachment.FinalObjectKey)
                .SetProperty(item => item.StagedETag, attachment.StagedETag)
                .SetProperty(item => item.FinalETag, attachment.FinalETag)
                .SetProperty(item => item.FinalChecksumSha256, attachment.FinalChecksumSha256)
                .SetProperty(item => item.Status, attachment.Status)
                .SetProperty(item => item.UploadedAtUtc, attachment.UploadedAtUtc)
                .SetProperty(item => item.FinalizedAtUtc, attachment.FinalizedAtUtc)
                .SetProperty(item => item.ExpiresAtUtc, attachment.ExpiresAtUtc)
                .SetProperty(item => item.UpdatedAt, attachment.UpdatedAt)
                .SetProperty(item => item.MetadataJson, attachment.MetadataJson), cancellationToken);
        if (updated != 1)
        {
            _logger.LogWarning(
                "Checkout attachment state persistence claim failed | AttachmentId: {AttachmentId} | ExpectedStatus: {ExpectedStatus} | TargetStatus: {TargetStatus} | ErrorCategory: conflict",
                attachment.Id,
                expectedStatus,
                attachment.Status);
            throw new CheckoutDynamicFieldValidationException(
                "CHECKOUT_ATTACHMENT_INVALID_STATE",
                "Uploaded file state changed while the operation was in progress.");
        }
        _dbContext.Entry(attachment).State = EntityState.Detached;
    }

    private async Task PersistTerminalFailureAsync(
        CheckoutAttachment attachment,
        CheckoutAttachmentStatus originalExpectedStatus,
        CancellationToken cancellationToken)
    {
        attachment.UpdatedAt = DateTime.UtcNow;
        attachment.MetadataJson = SerializeMetadata(
            attachment,
            attachment.OriginalFileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.Status);

        try
        {
            await PersistAttachmentStateAsync(attachment, originalExpectedStatus, cancellationToken);
        }
        catch (CheckoutDynamicFieldValidationException) when (originalExpectedStatus != CheckoutAttachmentStatus.Uploaded)
        {
            await PersistAttachmentStateAsync(attachment, CheckoutAttachmentStatus.Uploaded, cancellationToken);
        }
    }

    private async Task<CheckoutFieldDefinition> ResolveUploadFieldAsync(Guid tenantId, Guid fieldId, CancellationToken cancellationToken)
    {
        if (fieldId == Guid.Empty)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_FIELD_REQUIRED", "Checkout upload field is required.");
        }

        var field = await _dbContext.CheckoutFieldDefinitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == fieldId && !item.IsDeleted, cancellationToken);

        if (field == null)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_FIELD_NOT_FOUND", "Checkout upload field was not found for this store.");
        }

        if (!field.IsEnabled)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_FIELD_DISABLED", "Checkout upload field is disabled for this store.");
        }

        if (field.FieldKind != CheckoutFieldKind.Custom || !IsUploadField(field.FieldType))
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_FIELD_TYPE_INVALID", "Checkout uploads are only allowed for upload custom fields.");
        }

        return field;
    }

    private async Task<CheckoutAttachment> ResolveOwnedAttachmentAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        Guid cartId,
        Guid fieldId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await _dbContext.CheckoutAttachments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item =>
                item.Id == attachmentId &&
                item.TenantId == tenantId &&
                item.CartId == cartId &&
                item.CheckoutFieldDefinitionId == fieldId &&
                item.OwnerId == ownerId.Trim() &&
                item.OwnerType == ownerType.Trim().ToLowerInvariant() &&
                !item.IsDeleted,
                cancellationToken);

        if (attachment == null)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_NOT_FOUND", "Uploaded file was not found for this checkout.");
        }

        EnsureIdentityMatches(attachment, ownerType, sessionId, customerId);
        return attachment;
    }

    private static void EnsureIdentityMatches(CheckoutAttachment attachment, string ownerType, string? sessionId, Guid? customerId)
    {
        var normalizedOwnerType = ownerType.Trim().ToLowerInvariant();
        if (normalizedOwnerType == "customer")
        {
            if (!customerId.HasValue || attachment.OnlineCustomerId != customerId.Value)
            {
                throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_OWNER_MISMATCH", "Uploaded file does not belong to this customer.");
            }

            return;
        }

        if (!string.Equals(attachment.SessionId, sessionId?.Trim(), StringComparison.Ordinal))
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_OWNER_MISMATCH", "Uploaded file does not belong to this checkout session.");
        }
    }

    private static void ValidateCheckoutOwner(string ownerId, string ownerType, Guid cartId)
    {
        if (cartId == Guid.Empty || string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(ownerType))
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_CONTEXT_INVALID", "Checkout upload context is invalid.");
        }
    }

    private async Task<int> CountActiveAttachmentsAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        Guid cartId,
        Guid fieldId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var normalizedOwnerId = ownerId.Trim();
        var normalizedOwnerType = ownerType.Trim().ToLowerInvariant();
        var pendingLeaseCutoffUtc = nowUtc.AddMinutes(-UploadUrlExpiryMinutes);

        return await _dbContext.CheckoutAttachments.IgnoreQueryFilters().CountAsync(attachment =>
            attachment.TenantId == tenantId &&
            attachment.OwnerId == normalizedOwnerId &&
            attachment.OwnerType == normalizedOwnerType &&
            attachment.CartId == cartId &&
            attachment.CheckoutFieldDefinitionId == fieldId &&
            !attachment.IsDeleted &&
            attachment.OrderId == null &&
            attachment.ExpiresAtUtc > nowUtc &&
            ((attachment.Status == CheckoutAttachmentStatus.Pending && attachment.CreatedAt > pendingLeaseCutoffUtc) ||
             attachment.Status == CheckoutAttachmentStatus.Uploaded ||
             attachment.Status == CheckoutAttachmentStatus.Finalized),
            cancellationToken);
    }

    private async Task ValidateCartAuthorityAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        Guid cartId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedOwnerType = ownerType.Trim().ToLowerInvariant();
        var normalizedOwnerId = ownerId.Trim();
        var cart = await _cartService.GetCartAsync(
            tenantId,
            new CartOwner { OwnerType = normalizedOwnerType, OwnerId = normalizedOwnerId },
            correlationId,
            cancellationToken);

        if (cart.CartId != cartId ||
            cart.TenantId != tenantId ||
            !string.Equals(cart.OwnerType, normalizedOwnerType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(cart.OwnerId, normalizedOwnerId, StringComparison.Ordinal) ||
            cart.Items == null ||
            cart.Items.Count == 0 ||
            cart.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new CheckoutDynamicFieldValidationException(
                "CHECKOUT_ATTACHMENT_CART_NOT_AUTHORIZED",
                "Checkout uploads require a current cart that belongs to this checkout session.");
        }
    }

    private static CheckoutAttachmentPolicy BuildPolicy(CheckoutFieldDefinition field)
    {
        var configuration = DeserializeConfiguration(field.ConfigurationJson);
        var defaultTypes = field.FieldType switch
        {
            CheckoutFieldType.Image or CheckoutFieldType.MultiImage => new[] { AllowedFileType.Jpeg, AllowedFileType.Png, AllowedFileType.Webp },
            CheckoutFieldType.File => new[] { AllowedFileType.Jpeg, AllowedFileType.Png, AllowedFileType.Webp, AllowedFileType.Pdf, AllowedFileType.Doc },
            _ => Array.Empty<AllowedFileType>()
        };
        var configuredTypes = (configuration.AllowedMimeTypes ?? Array.Empty<string>())
            .Select(type => TryMapAllowedFileType(type, out var mapped) ? mapped : (AllowedFileType?)null)
            .Where(type => type.HasValue)
            .Select(type => type!.Value)
            .Distinct()
            .ToArray();

        var allowedTypes = configuredTypes.Length > 0
            ? configuredTypes.Where(defaultTypes.Contains).ToArray()
            : defaultTypes;

        if (allowedTypes.Length == 0)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_CONFIGURATION_INVALID", "Checkout upload field configuration does not allow any supported file types.");
        }

        var maxFiles = field.FieldType == CheckoutFieldType.MultiImage
            ? Math.Clamp(configuration.MaxFiles ?? DefaultMultiImageMaxFiles, 1, 50)
            : 1;
        var maxFileSize = configuration.MaxFileSize ?? DefaultMaxFileSizeBytes;
        return new CheckoutAttachmentPolicy(allowedTypes, maxFileSize, maxFiles);
    }

    private static StorefrontCheckoutFieldConfigurationDto DeserializeConfiguration(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StorefrontCheckoutFieldConfigurationDto();
        }

        try
        {
            return JsonSerializer.Deserialize<StorefrontCheckoutFieldConfigurationDto>(json, JsonOptions)
                ?? new StorefrontCheckoutFieldConfigurationDto();
        }
        catch (JsonException)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_CONFIGURATION_INVALID", "Checkout upload field configuration is invalid.");
        }
    }

    private static void ValidateFileSize(long sizeBytes, CheckoutAttachmentPolicy policy)
    {
        if (sizeBytes <= 0 || sizeBytes > policy.MaxFileSizeBytes)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_TOO_LARGE", $"Uploaded files must be {policy.MaxFileSizeBytes} bytes or smaller.");
        }
    }

    private static AllowedFileType ResolveAllowedFileType(string contentType, IReadOnlyCollection<AllowedFileType> allowedTypes)
    {
        if (!TryMapAllowedFileType(contentType, out var mapped) || !allowedTypes.Contains(mapped))
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_TYPE_NOT_ALLOWED", "Uploaded file type is not allowed for this checkout field.");
        }

        return mapped;
    }

    private static bool TryMapAllowedFileType(string? contentType, out AllowedFileType fileType)
    {
        var normalized = NormalizeContentType(contentType);
        fileType = normalized switch
        {
            "image/jpeg" or "image/jpg" => AllowedFileType.Jpeg,
            "image/png" => AllowedFileType.Png,
            "image/webp" => AllowedFileType.Webp,
            "application/pdf" => AllowedFileType.Pdf,
            "application/msword" => AllowedFileType.Doc,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => AllowedFileType.Docx,
            _ => default
        };

        return normalized is "image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "application/pdf" or "application/msword" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }

    private static string NormalizeContentType(string? contentType) =>
        (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

    private static string GetCanonicalContentType(AllowedFileType type) => type switch
    {
        AllowedFileType.Jpeg => "image/jpeg",
        AllowedFileType.Png => "image/png",
        AllowedFileType.Webp => "image/webp",
        AllowedFileType.Pdf => "application/pdf",
        AllowedFileType.Doc => "application/msword",
        AllowedFileType.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_TYPE_NOT_ALLOWED", "Uploaded file type is not supported.")
    };

    private static string GetPrimaryExtension(AllowedFileType type) => type switch
    {
        AllowedFileType.Jpeg => ".jpg",
        AllowedFileType.Png => ".png",
        AllowedFileType.Webp => ".webp",
        AllowedFileType.Pdf => ".pdf",
        AllowedFileType.Doc => ".doc",
        AllowedFileType.Docx => ".docx",
        _ => ".bin"
    };

    private static bool IsImageType(AllowedFileType type) =>
        type is AllowedFileType.Jpeg or AllowedFileType.Png or AllowedFileType.Webp;

    private static bool IsUploadField(CheckoutFieldType fieldType) =>
        fieldType is CheckoutFieldType.Image or CheckoutFieldType.File or CheckoutFieldType.MultiImage;

    private static string BuildFinalObjectKey(CheckoutAttachment attachment, AllowedFileType allowedType)
    {
        var finalizedAt = DateTime.UtcNow;
        return $"tenants/{attachment.TenantId:N}/checkout-final/{attachment.CartId:N}/fields/{attachment.CheckoutFieldDefinitionId:N}/attachments/{finalizedAt:yyyy/MM/dd}/{attachment.Id:N}{GetPrimaryExtension(allowedType)}";
    }

    private static void CopyPersistedFinalization(CheckoutAttachment target, CheckoutAttachment source)
    {
        target.FinalObjectKey = source.FinalObjectKey;
        target.ContentType = source.ContentType;
        target.MediaType = source.MediaType;
        target.SizeBytes = source.SizeBytes;
        target.Status = source.Status;
        target.UploadedAtUtc = source.UploadedAtUtc;
        target.FinalizedAtUtc = source.FinalizedAtUtc;
        target.StagedETag = source.StagedETag;
        target.FinalETag = source.FinalETag;
        target.FinalChecksumSha256 = source.FinalChecksumSha256;
        target.MetadataJson = source.MetadataJson;
        target.UpdatedAt = source.UpdatedAt;
    }

    private static CheckoutAttachmentDto Map(CheckoutAttachment attachment) => new()
    {
        AttachmentId = attachment.Id,
        FieldId = attachment.CheckoutFieldDefinitionId,
        FileName = attachment.OriginalFileName,
        ContentType = attachment.ContentType,
        SizeBytes = attachment.SizeBytes,
        Status = attachment.Status.ToString().ToUpperInvariant(),
        UploadedAtUtc = attachment.UploadedAtUtc
    };

    private static string SerializeMetadata(
        CheckoutAttachment? attachment,
        string fileName,
        string contentType,
        long sizeBytes,
        CheckoutAttachmentStatus status) =>
        JsonSerializer.Serialize(new
        {
            attachmentId = attachment?.Id,
            fileName,
            contentType,
            sizeBytes,
            status = status.ToString().ToUpperInvariant(),
            uploadedAtUtc = attachment?.UploadedAtUtc,
            finalizedAtUtc = attachment?.FinalizedAtUtc,
            finalETag = attachment?.FinalETag,
            finalChecksumSha256 = attachment?.FinalChecksumSha256,
            attachedAtUtc = attachment?.AttachedAtUtc
        }, JsonOptions);

    private sealed record CheckoutAttachmentPolicy(
        IReadOnlyCollection<AllowedFileType> AllowedFileTypes,
        long MaxFileSizeBytes,
        int MaxFiles);
}
