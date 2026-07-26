using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.Governance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICustomerRefundService
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

    Task<string> GetCustomerMediaAccessUrlAsync(
        Guid customerId,
        Guid mediaId,
        CancellationToken cancellationToken = default);

    Task<CustomerRefundSummaryDto> CreateRefundRequestAsync(
        Guid customerId,
        CustomerCreateRefundRequestDto request,
        string traceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerRefundSummaryDto>> GetRefundRequestsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}

public class CustomerRefundService : ICustomerRefundService
{
    private const int MaxCreateRetries = 3;
    private readonly ApplicationDbContext _dbContext;
    private readonly RefundSettings _settings;
    private readonly IRefundEvidenceService _refundEvidenceService;
    private readonly ICustomerPayoutDetailsService _customerPayoutDetailsService;
    private readonly IRefundPayoutSecurityService _refundPayoutSecurityService;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ICommerceHubFinancialSafetyGuardService _financialSafetyGuardService;
    private readonly ILogger<CustomerRefundService> _logger;

    public CustomerRefundService(
        ApplicationDbContext dbContext,
        IOptions<RefundSettings> settings,
        IRefundEvidenceService refundEvidenceService,
        ICustomerPayoutDetailsService customerPayoutDetailsService,
        IRefundPayoutSecurityService refundPayoutSecurityService,
        IUserGovernanceService userGovernanceService,
        ICommerceHubFinancialSafetyGuardService financialSafetyGuardService,
        ILogger<CustomerRefundService> logger)
    {
        _dbContext = dbContext;
        _settings = settings.Value;
        _refundEvidenceService = refundEvidenceService;
        _customerPayoutDetailsService = customerPayoutDetailsService;
        _refundPayoutSecurityService = refundPayoutSecurityService;
        _userGovernanceService = userGovernanceService;
        _financialSafetyGuardService = financialSafetyGuardService;
        _logger = logger;
    }

    public Task<RefundEvidenceUploadLease> CreateUploadLeaseAsync(
        Guid customerId,
        RefundEvidenceUploadUrlRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return _refundEvidenceService.CreateUploadLeaseAsync(customerId, request, cancellationToken);
    }

    public Task<RefundEvidenceUploadedMedia> UploadEvidenceAsync(
        Guid customerId,
        IFormFile file,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        return _refundEvidenceService.UploadEvidenceAsync(customerId, file, sha256, cancellationToken);
    }

    public Task<string> GetCustomerMediaAccessUrlAsync(
        Guid customerId,
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        return _refundEvidenceService.GetCustomerMediaAccessUrlAsync(customerId, mediaId, cancellationToken);
    }

    public async Task<CustomerRefundSummaryDto> CreateRefundRequestAsync(
        Guid customerId,
        CustomerCreateRefundRequestDto request,
        string traceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await _userGovernanceService.EnsureCustomerRefundRequestAllowedAsync(
                customerId,
                cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            throw new CommerceHubCheckoutException(
                exception.Code,
                exception.Message,
                exception.StatusCode);
        }

        if (request.OrderItemId == Guid.Empty)
        {
            throw new CommerceHubCheckoutException(
                "ORDER_ITEM_REQUIRED",
                "Order item id is required.",
                StatusCodes.Status400BadRequest);
        }

        if (request.Quantity <= 0)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_QUANTITY_INVALID",
                "Refund quantity must be greater than zero.",
                StatusCodes.Status400BadRequest);
        }

        var normalizedReasonCode = NormalizeReasonCode(request.ReasonCode);
        var normalizedReasonText = NormalizeReasonText(request.ReasonText);
        var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        var requestedMediaUrls = NormalizeMediaUrls(request.MediaUrls);
        var resolvedPayout = await _customerPayoutDetailsService.ResolveRefundPayoutAsync(
            customerId,
            request.SavedPayoutDetailsId,
            request.PayoutDetails,
            cancellationToken);
        if (normalizedIdempotencyKey == null)
        {
            throw new CommerceHubCheckoutException(
                "IDEMPOTENCY_KEY_REQUIRED",
                "Idempotency-Key header is required.",
                StatusCodes.Status400BadRequest);
        }

        var existingReplay = await FindRefundByIdempotencyAsync(customerId, normalizedIdempotencyKey, cancellationToken);
        if (existingReplay != null)
        {
            EnsureReplayMatches(
                existingReplay,
                request.OrderItemId,
                request.Quantity,
                normalizedReasonCode,
                normalizedReasonText,
                requestedMediaUrls,
                resolvedPayout);

            return await MapSummaryAsync(existingReplay, cancellationToken);
        }

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            try
            {
                return await CreateRefundRequestCoreAsync(
                    customerId,
                    request.OrderItemId,
                    request.Quantity,
                    normalizedReasonCode,
                    normalizedReasonText,
                    resolvedPayout,
                    requestedMediaUrls,
                    normalizedIdempotencyKey,
                    traceId,
                    cancellationToken);
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < MaxCreateRetries)
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogWarning(
                    ex,
                    "Customer refund request serialization conflict, retrying | CustomerId: {CustomerId} | OrderItemId: {OrderItemId} | Attempt: {Attempt}/{MaxAttempts} | TraceId: {TraceId}",
                    customerId,
                    request.OrderItemId,
                    attempt,
                    MaxCreateRetries,
                    traceId);

                await Task.Delay(50 * attempt, cancellationToken);
            }
        }

        throw new InvalidOperationException("Refund request retry loop exited unexpectedly.");
    }

    public async Task<IReadOnlyList<CustomerRefundSummaryDto>> GetRefundRequestsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var refundRequests = await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Include(refundRequest => refundRequest.Order)
            .Include(refundRequest => refundRequest.OrderItem)
            .Where(refundRequest => refundRequest.CustomerId == customerId && !refundRequest.IsDeleted)
            .OrderByDescending(refundRequest => refundRequest.CreatedAt)
            .ThenByDescending(refundRequest => refundRequest.Id)
            .ToListAsync(cancellationToken);

        var evidenceLookup = await _refundEvidenceService.GetEvidenceLookupAsync(
            refundRequests.Select(refundRequest => refundRequest.Id),
            cancellationToken);
        var refundIds = refundRequests.Select(refundRequest => refundRequest.Id).ToList();
        var returnLookup = await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Where(returnRequest =>
                refundIds.Contains(returnRequest.RefundId) &&
                !returnRequest.IsDeleted)
            .ToDictionaryAsync(returnRequest => returnRequest.RefundId, cancellationToken);
        var (executionLookup, trackedRefundLookup) = await LoadPayoutConfirmationLookupsAsync(refundIds, cancellationToken);

        return refundRequests
            .Select(refundRequest => MapSummary(refundRequest, evidenceLookup, returnLookup, executionLookup, trackedRefundLookup))
            .ToList();
    }

    private async Task<CustomerRefundSummaryDto> CreateRefundRequestCoreAsync(
        Guid customerId,
        Guid orderItemId,
        int quantity,
        RefundReasonCode reasonCode,
        string? reasonText,
        ResolvedRefundPayoutDetails payoutDetails,
        IReadOnlyCollection<string> requestedMediaUrls,
        string idempotencyKey,
        string traceId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var orderItem = await LoadOrderItemForUpdateAsync(orderItemId, cancellationToken);

            if (orderItem == null || orderItem.SubOrder.IsDeleted || orderItem.SubOrder.MarketplaceOrder.IsDeleted)
            {
                throw new CommerceHubCheckoutException(
                    "ORDER_ITEM_NOT_FOUND",
                    "Order item was not found.",
                    StatusCodes.Status404NotFound);
            }

            var order = orderItem.SubOrder.MarketplaceOrder;
            if (order.CustomerId != customerId)
            {
                throw new CommerceHubCheckoutException(
                    "ORDER_ITEM_NOT_FOUND",
                    "Order item was not found.",
                    StatusCodes.Status404NotFound);
            }

            CommerceHubRefundReturnCurrencyAuthority.RequireConsistentExecutionCurrency(
                order,
                orderItem.SubOrder,
                refund: null,
                payment: null,
                $"refund-request:{orderItem.Id:D}");

            await _financialSafetyGuardService.CaptureStateForUpdateAsync(
                orderItem.SubOrder.StoreId,
                order.Currency,
                cancellationToken);

            if (quantity > orderItem.Quantity)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_QUANTITY_EXCEEDS_PURCHASED",
                    "Refund quantity exceeds the purchased quantity.",
                    StatusCodes.Status400BadRequest,
                    new
                    {
                        purchasedQuantity = orderItem.Quantity
                    });
            }

            var existingReplay = await FindRefundByIdempotencyAsync(customerId, idempotencyKey, cancellationToken);
            if (existingReplay != null)
            {
                EnsureReplayMatches(
                    existingReplay,
                    orderItemId,
                    quantity,
                    reasonCode,
                    reasonText,
                    requestedMediaUrls,
                    payoutDetails);

                await transaction.CommitAsync(cancellationToken);
                return await MapSummaryAsync(existingReplay, cancellationToken);
            }

            var existingRefundRequests = await _dbContext.CommerceHubRefundRequests
                .Where(refundRequest =>
                    refundRequest.OrderItemId == orderItem.Id &&
                    refundRequest.CustomerId == customerId &&
                    !refundRequest.IsDeleted)
                .OrderByDescending(refundRequest => refundRequest.CreatedAt)
                .ToListAsync(cancellationToken);

            var duplicateRequest = existingRefundRequests.FirstOrDefault(refundRequest =>
                CustomerRefundRequestPolicy.CountsAgainstAvailableQuantity(refundRequest.Status) &&
                refundRequest.Quantity == quantity &&
                refundRequest.ReasonCode == reasonCode &&
                string.Equals(refundRequest.ReasonText ?? string.Empty, reasonText ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            if (duplicateRequest != null)
            {
                throw new CommerceHubCheckoutException(
                    "DUPLICATE_REFUND_REQUEST",
                    "An active refund request with the same quantity and reason already exists for this item.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        refundId = duplicateRequest.Id,
                        status = duplicateRequest.Status
                    });
            }

            var nowUtc = DateTime.UtcNow;
            var eligibility = CustomerRefundRequestPolicy.Evaluate(
                orderItem.Status,
                orderItem.DeliveredAt,
                orderItem.Quantity,
                existingRefundRequests,
                _settings,
                nowUtc);

            if (!eligibility.CanRefund)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_NOT_ALLOWED",
                    eligibility.DisabledReason ?? "This item is not eligible for a refund request.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        orderItemStatus = orderItem.Status,
                        deliveredAt = orderItem.DeliveredAt,
                        maxRefundableQuantity = eligibility.MaxRefundableQuantity
                    });
            }

            if (quantity > eligibility.MaxRefundableQuantity)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_QUANTITY_EXCEEDED",
                    "Refund quantity exceeds the remaining refundable quantity.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        maxRefundableQuantity = eligibility.MaxRefundableQuantity
                    });
            }

            var evidenceMedia = await _refundEvidenceService.ValidateOwnedMediaAsync(
                customerId,
                requestedMediaUrls,
                cancellationToken);

            var refundRequest = new CommerceHubRefundRequest
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Order = order,
                OrderItemId = orderItem.Id,
                OrderItem = orderItem,
                CustomerId = customerId,
                Customer = order.Customer,
                VendorId = orderItem.SubOrder.StoreId,
                Quantity = quantity,
                UnitPriceSnapshot = RoundCurrency(orderItem.UnitPrice),
                TotalAmount = RoundCurrency(orderItem.UnitPrice * quantity),
                ReasonCode = reasonCode,
                ReasonText = reasonText,
                Reason = reasonText ?? reasonCode.ToDisplayLabel(),
                PayoutMethodSnapshot = payoutDetails.Method,
                PayoutAccountSnapshot = payoutDetails.EncryptedAccountIdentifier,
                PayoutAccountMaskedSnapshot = payoutDetails.MaskedAccountIdentifier,
                PayoutAccountHolderNameSnapshot = payoutDetails.AccountHolderName,
                IdempotencyKey = idempotencyKey,
                Status = CommerceHubRefundRequestStatus.Requested,
                CreatedBy = customerId,
                UpdatedBy = customerId
            };

            foreach (var media in evidenceMedia)
            {
                media.RefundRequestId = refundRequest.Id;
                media.AttachedAtUtc = nowUtc;
                media.UploadedAtUtc ??= nowUtc;
                media.ExpiresAtUtc = nowUtc.AddDays(30);
                media.IsTemporary = false;
                media.UpdatedAt = nowUtc;
                media.UpdatedBy = customerId;
                media.MetadataJson = RefundEvidenceService.SerializeMetadata(
                    media.Id,
                    media.MediaType,
                    media.ContentType,
                    media.SizeBytes,
                    media.ContentHash,
                    media.UploadedAtUtc,
                    media.OriginalFileName,
                    isTemporary: false,
                    thumbnailUrl: null);
            }

            var remainingQuantity = eligibility.MaxRefundableQuantity - quantity;
            var refundWindowValid = CustomerRefundRequestPolicy.IsWithinRefundWindow(orderItem.DeliveredAt, _settings, nowUtc);

            _dbContext.CommerceHubRefundRequests.Add(refundRequest);
            _dbContext.AuditLogs.Add(new AuditLog
            {
                TenantId = refundRequest.VendorId,
                TimestampUtc = nowUtc,
                UserId = customerId,
                UserNameSnapshot = order.Customer.Email.Length > 150
                    ? order.Customer.Email[..150]
                    : order.Customer.Email,
                ActionType = "REFUND_REQUEST_CREATED",
                EntityType = "CommerceHubRefundRequest",
                EntityId = refundRequest.Id.ToString(),
                Description = $"Customer refund request created for order {order.OrderNumber}.",
                AfterDataJson = JsonSerializer.Serialize(new
                {
                    CustomerId = customerId,
                    OrderId = order.Id,
                    OrderItemId = orderItem.Id,
                    Quantity = refundRequest.Quantity,
                    Amount = refundRequest.TotalAmount,
                    ReasonCode = refundRequest.ReasonCode.ToString(),
                    ReasonText = refundRequest.ReasonText,
                    PayoutMethod = refundRequest.PayoutMethodSnapshot?.ToString(),
                    PayoutAccountMasked = refundRequest.PayoutAccountMaskedSnapshot,
                    PayoutSourceId = payoutDetails.SavedPayoutDetailsId,
                    MediaCount = evidenceMedia.Count,
                    DeliveredAt = orderItem.DeliveredAt,
                    RefundWindowValid = refundWindowValid,
                    RemainingQuantity = remainingQuantity
                }),
                ReferenceNumber = order.OrderNumber,
                Source = "CommerceHubCustomer",
                SuccessFlag = true
            });
            _dbContext.AuditLogs.Add(new AuditLog
            {
                TenantId = refundRequest.VendorId,
                TimestampUtc = nowUtc,
                UserId = customerId,
                UserNameSnapshot = order.Customer.Email.Length > 150
                    ? order.Customer.Email[..150]
                    : order.Customer.Email,
                ActionType = "PAYOUT_DETAILS_USED_FOR_REFUND",
                EntityType = "CommerceHubRefundRequest",
                EntityId = refundRequest.Id.ToString(),
                Description = $"Refund payout details snapshot stored for order {order.OrderNumber}.",
                AfterDataJson = JsonSerializer.Serialize(new
                {
                    RefundId = refundRequest.Id,
                    PayoutMethod = refundRequest.PayoutMethodSnapshot?.ToString(),
                    PayoutAccountMasked = refundRequest.PayoutAccountMaskedSnapshot,
                    PayoutSourceId = payoutDetails.SavedPayoutDetailsId,
                    IsVerified = payoutDetails.IsVerified
                }),
                ReferenceNumber = order.OrderNumber,
                Source = "CommerceHubCustomer",
                SuccessFlag = true
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Customer refund request created | RefundRequestId: {RefundRequestId} | OrderId: {OrderId} | OrderItemId: {OrderItemId} | CustomerId: {CustomerId} | Quantity: {Quantity} | MediaCount: {MediaCount} | TraceId: {TraceId}",
                refundRequest.Id,
                order.Id,
                orderItem.Id,
                customerId,
                refundRequest.Quantity,
                evidenceMedia.Count,
                traceId);

            var evidenceLookup = await _refundEvidenceService.GetEvidenceLookupAsync(
                new[] { refundRequest.Id },
                cancellationToken);

            return MapSummary(refundRequest, evidenceLookup, (CommerceHubReturnRequest?)null, null, null);
        }
        catch (DbUpdateException ex) when (IsRefundRequestIdempotencyUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            var existingReplay = await FindRefundByIdempotencyAsync(customerId, idempotencyKey, cancellationToken);
            if (existingReplay != null)
            {
                EnsureReplayMatches(
                    existingReplay,
                    orderItemId,
                    quantity,
                    reasonCode,
                    reasonText,
                    requestedMediaUrls,
                    payoutDetails);

                return await MapSummaryAsync(existingReplay, cancellationToken);
            }

            throw;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<CommerceHubRefundRequest?> FindRefundByIdempotencyAsync(
        Guid customerId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Include(refundRequest => refundRequest.Order)
            .Include(refundRequest => refundRequest.OrderItem)
            .Include(refundRequest => refundRequest.EvidenceMedia)
            .Where(refundRequest =>
                refundRequest.CustomerId == customerId &&
                refundRequest.IdempotencyKey == idempotencyKey &&
                !refundRequest.IsDeleted)
            .OrderByDescending(refundRequest => refundRequest.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CommerceHubSubOrderItem?> LoadOrderItemForUpdateAsync(
        Guid orderItemId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubSubOrderItem> query = _dbContext.Database.IsRelational()
            ? _dbContext.CommerceHubSubOrderItems.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubSubOrderItems""
                WHERE ""Id"" = {orderItemId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE")
            : _dbContext.CommerceHubSubOrderItems.Where(item => item.Id == orderItemId && !item.IsDeleted);

        return await query
            .Include(item => item.SubOrder)
            .ThenInclude(subOrder => subOrder.MarketplaceOrder)
            .ThenInclude(order => order.Customer)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private void EnsureReplayMatches(
        CommerceHubRefundRequest existingRefund,
        Guid orderItemId,
        int quantity,
        RefundReasonCode reasonCode,
        string? reasonText,
        IReadOnlyCollection<string> requestedMediaUrls,
        ResolvedRefundPayoutDetails payoutDetails)
    {
        var existingMediaIds = existingRefund.EvidenceMedia
            .Where(media => !media.IsDeleted)
            .Select(media => media.Id)
            .OrderBy(id => id)
            .ToArray();

        var requestedMediaIds = requestedMediaUrls
            .Select(url => _refundEvidenceService.TryParseMediaId(url))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .OrderBy(id => id)
            .ToArray();

        if (existingRefund.OrderItemId == orderItemId &&
            existingRefund.Quantity == quantity &&
            existingRefund.ReasonCode == reasonCode &&
            string.Equals(existingRefund.ReasonText ?? string.Empty, reasonText ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            existingMediaIds.SequenceEqual(requestedMediaIds) &&
            RefundPayoutSupport.SnapshotMatches(existingRefund, payoutDetails, _refundPayoutSecurityService))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "IDEMPOTENCY_KEY_REUSED",
            "This idempotency key was already used for a different refund request.",
            StatusCodes.Status409Conflict,
            new
            {
                refundId = existingRefund.Id
            });
    }

    private RefundReasonCode NormalizeReasonCode(RefundReasonCode? reasonCode)
    {
        if (!reasonCode.HasValue)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_REASON_CODE_REQUIRED",
                "Please select a refund reason.",
                StatusCodes.Status400BadRequest);
        }

        if (!Enum.IsDefined(reasonCode.Value))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_REASON_CODE_INVALID",
                "Refund reason is invalid.",
                StatusCodes.Status400BadRequest);
        }

        return reasonCode.Value;
    }

    private string? NormalizeReasonText(string? reasonText)
    {
        if (string.IsNullOrWhiteSpace(reasonText))
        {
            return null;
        }

        var normalized = reasonText.Trim();
        if (normalized.Length > _settings.MaxReasonLength)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_REASON_TOO_LONG",
                $"Refund details cannot exceed {_settings.MaxReasonLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private IReadOnlyCollection<string> NormalizeMediaUrls(IReadOnlyCollection<string>? mediaUrls)
    {
        if (mediaUrls == null || mediaUrls.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = mediaUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .ToList();

        if (normalized.Count > _settings.MaxEvidenceFiles)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_MEDIA_LIMIT_EXCEEDED",
                $"You can attach up to {_settings.MaxEvidenceFiles} files to a refund request.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var normalized = idempotencyKey.Trim();
        return normalized.Length > 100 ? normalized[..100] : normalized;
    }

    private static bool IsRefundRequestIdempotencyUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException postgresException &&
               postgresException.SqlState == "23505" &&
               string.Equals(
                   postgresException.ConstraintName,
                   "IX_CommerceHubRefundRequests_IdempotencyKey",
                   StringComparison.Ordinal);
    }

    private static bool IsSerializationFailure(Exception ex)
    {
        return ex switch
        {
            PostgresException postgresException when postgresException.SqlState == "40001" => true,
            DbUpdateException dbUpdateException when dbUpdateException.InnerException is PostgresException postgresException &&
                                                   postgresException.SqlState == "40001" => true,
            _ => false
        };
    }

    private async Task<CustomerRefundSummaryDto> MapSummaryAsync(
        CommerceHubRefundRequest refundRequest,
        CancellationToken cancellationToken)
    {
        var evidenceLookup = await _refundEvidenceService.GetEvidenceLookupAsync(
            new[] { refundRequest.Id },
            cancellationToken);
        var returnRequest = await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.RefundId == refundRequest.Id && !item.IsDeleted, cancellationToken);
        var (executionLookup, trackedRefundLookup) = await LoadPayoutConfirmationLookupsAsync(
            new[] { refundRequest.Id },
            cancellationToken);

        executionLookup.TryGetValue(refundRequest.Id, out var executionRecord);
        trackedRefundLookup.TryGetValue(refundRequest.Id, out var trackedRefund);

        return MapSummary(refundRequest, evidenceLookup, returnRequest, executionRecord, trackedRefund);
    }

    private static CustomerRefundSummaryDto MapSummary(
        CommerceHubRefundRequest refundRequest,
        IReadOnlyDictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>> evidenceLookup,
        IReadOnlyDictionary<Guid, CommerceHubReturnRequest> returnLookup,
        IReadOnlyDictionary<Guid, CommerceHubRefundExecution> executionLookup,
        IReadOnlyDictionary<Guid, CommerceHubRefund> trackedRefundLookup)
    {
        returnLookup.TryGetValue(refundRequest.Id, out var returnRequest);
        executionLookup.TryGetValue(refundRequest.Id, out var executionRecord);
        trackedRefundLookup.TryGetValue(refundRequest.Id, out var trackedRefund);
        return MapSummary(refundRequest, evidenceLookup, returnRequest, executionRecord, trackedRefund);
    }

    private static CustomerRefundSummaryDto MapSummary(
        CommerceHubRefundRequest refundRequest,
        IReadOnlyDictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>> evidenceLookup,
        CommerceHubReturnRequest? returnRequest,
        CommerceHubRefundExecution? executionRecord,
        CommerceHubRefund? trackedRefund)
    {
        var media = evidenceLookup.TryGetValue(refundRequest.Id, out var matchedMedia)
            ? matchedMedia
            : Array.Empty<RefundEvidenceMediaDto>();

        return new CustomerRefundSummaryDto
        {
            RefundId = refundRequest.Id,
            OrderId = refundRequest.OrderId,
            OrderNumber = refundRequest.Order.OrderNumber,
            OrderItemId = refundRequest.OrderItemId,
            VendorId = refundRequest.VendorId,
            ProductName = refundRequest.OrderItem.ProductName,
            Quantity = refundRequest.Quantity,
            UnitPriceSnapshot = refundRequest.UnitPriceSnapshot,
            Amount = refundRequest.TotalAmount,
            Currency = CommerceHubRefundReturnCurrencyAuthority.RequireOrderCurrency(
                refundRequest.Order,
                $"refund-response:{refundRequest.Id:D}"),
            Status = CommerceHubRefundRequestWorkflowPolicy.NormalizeStatus(refundRequest.Status),
            Reason = BuildReasonSummary(refundRequest.ReasonCode, refundRequest.ReasonText),
            ReasonCode = refundRequest.ReasonCode,
            ReasonText = refundRequest.ReasonText,
            Payout = BuildPayoutSnapshot(refundRequest, executionRecord, trackedRefund),
            ReturnRequest = returnRequest == null
                ? null
                : CommerceHubReturnMapper.MapDetails(returnRequest, refundRequest),
            MediaUrls = media.Select(item => item.Url).ToList(),
            Media = media.ToList(),
            CreatedAt = refundRequest.CreatedAt,
            UpdatedAt = refundRequest.UpdatedAt
        };
    }

    private async Task<(Dictionary<Guid, CommerceHubRefundExecution> ExecutionLookup, Dictionary<Guid, CommerceHubRefund> TrackedRefundLookup)>
        LoadPayoutConfirmationLookupsAsync(IEnumerable<Guid> refundIds, CancellationToken cancellationToken)
    {
        var ids = refundIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return (new Dictionary<Guid, CommerceHubRefundExecution>(), new Dictionary<Guid, CommerceHubRefund>());
        }

        var executionLookup = await _dbContext.CommerceHubRefundExecutions
            .AsNoTracking()
            .Where(item => ids.Contains(item.RefundId) && !item.IsDeleted)
            .ToDictionaryAsync(item => item.RefundId, cancellationToken);

        var trackedRefundLookup = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(item => item.RefundRequestId.HasValue && ids.Contains(item.RefundRequestId.Value) && !item.IsDeleted)
            .ToDictionaryAsync(item => item.RefundRequestId!.Value, cancellationToken);

        return (executionLookup, trackedRefundLookup);
    }

    internal static CustomerRefundPayoutSnapshotDto? BuildPayoutSnapshot(
        CommerceHubRefundRequest refundRequest,
        CommerceHubRefundExecution? executionRecord = null,
        CommerceHubRefund? trackedRefund = null)
    {
        if (!refundRequest.PayoutMethodSnapshot.HasValue ||
            string.IsNullOrWhiteSpace(refundRequest.PayoutAccountMaskedSnapshot))
        {
            return null;
        }

        return new CustomerRefundPayoutSnapshotDto
        {
            Method = executionRecord?.Method ?? refundRequest.PayoutMethodSnapshot.Value,
            MaskedAccountIdentifier = refundRequest.PayoutAccountMaskedSnapshot,
            AccountHolderName = refundRequest.PayoutAccountHolderNameSnapshot,
            TransactionReference = executionRecord?.TransactionReference ?? trackedRefund?.Reference,
            PaidAt = executionRecord?.ExecutedAt ?? trackedRefund?.ProcessedAtUtc ?? refundRequest.CompletedAtUtc
        };
    }

    internal static string BuildReasonSummary(RefundReasonCode reasonCode, string? reasonText)
    {
        var label = reasonCode.ToDisplayLabel();
        return string.IsNullOrWhiteSpace(reasonText)
            ? label
            : $"{label}: {reasonText.Trim()}";
    }

    private static decimal RoundCurrency(decimal amount) =>
        CommissionCalculator.RoundAmount(amount);
}
