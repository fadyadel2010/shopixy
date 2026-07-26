using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminRefundsService
{
    Task<SuperAdminPagedResult<SuperAdminRefundListItemDto>> GetRefundsAsync(
        SuperAdminRefundsListQuery query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminRefundDetailsDto?> GetRefundDetailsAsync(
        Guid refundId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminRefundDetailsDto> ApproveAsync(
        Guid refundId,
        SuperAdminApproveRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminRefundDetailsDto> RejectAsync(
        Guid refundId,
        SuperAdminRejectRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default);

    Task<SuperAdminRefundDetailsDto> RetryAsync(
        Guid refundId,
        SuperAdminRetryRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default);

    Task<SuperAdminRefundDetailsDto> ExecuteAsync(
        Guid refundId,
        SuperAdminExecuteRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminRefundsService : ISuperAdminRefundsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IReturnManagementService _returnManagementService;
    private readonly IRefundExecutionService _refundExecutionService;
    private readonly IRefundEvidenceService _refundEvidenceService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;

    public SuperAdminRefundsService(
        ApplicationDbContext dbContext,
        IReturnManagementService returnManagementService,
        IRefundExecutionService refundExecutionService,
        IRefundEvidenceService refundEvidenceService,
        ISuperAdminAuditService auditService,
        ICommerceHubVendorDebtService vendorDebtService)
    {
        _dbContext = dbContext;
        _returnManagementService = returnManagementService;
        _refundExecutionService = refundExecutionService;
        _refundEvidenceService = refundEvidenceService;
        _auditService = auditService;
        _vendorDebtService = vendorDebtService;
    }

    public async Task<SuperAdminPagedResult<SuperAdminRefundListItemDto>> GetRefundsAsync(
        SuperAdminRefundsListQuery query,
        CancellationToken cancellationToken = default)
    {
        var filteredQuery = BuildFilteredQuery(query);
        var totalCount = await filteredQuery.CountAsync(cancellationToken);

        var rows = await filteredQuery
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(item => new
            {
                item.Id,
                item.OrderId,
                OrderNumber = item.Order.OrderNumber,
                item.VendorId,
                VendorName = item.OrderItem.SubOrder.StoreName,
                item.CustomerId,
                CustomerName = item.Customer.Name,
                ProductName = item.OrderItem.ProductName,
                item.Quantity,
                Amount = item.TotalAmount,
                Currency = item.Order.Currency,
                item.Status,
                item.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(item => new SuperAdminRefundListItemDto
            {
                RefundId = item.Id,
                OrderId = item.OrderId,
                OrderNumber = item.OrderNumber,
                VendorId = item.VendorId,
                VendorName = item.VendorName,
                CustomerId = item.CustomerId,
                CustomerName = item.CustomerName,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                Amount = item.Amount,
                Currency = FinancialCurrencyGuard.RequireEgpCurrency(
                    item.Currency,
                    "commercehub.refund.admin_list.currency",
                    $"refund:{item.Id:D}"),
                Status = CommerceHubRefundRequestWorkflowPolicy.NormalizeStatus(item.Status),
                CreatedAt = item.CreatedAt
            })
            .ToList();

        return new SuperAdminPagedResult<SuperAdminRefundListItemDto>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize)
        };
    }

    public async Task<SuperAdminRefundDetailsDto?> GetRefundDetailsAsync(
        Guid refundId,
        CancellationToken cancellationToken = default)
    {
        var refundRequest = await LoadRefundDetailsGraphAsync(refundId, cancellationToken);
        if (refundRequest == null)
        {
            return null;
        }

        return await MapDetailsAsync(refundRequest, cancellationToken);
    }

    public async Task<SuperAdminRefundDetailsDto> ApproveAsync(
        Guid refundId,
        SuperAdminApproveRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var note = NormalizeOptional(request.Note);
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            var refundRequest = await LoadRefundForUpdateAsync(refundId, cancellationToken);
            if (refundRequest == null)
            {
                throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
            }

            if (CommerceHubRefundRequestWorkflowPolicy.IsExecuted(refundRequest.Status) ||
                refundRequest.Status == CommerceHubRefundRequestStatus.Processing)
            {
                await transaction.CommitAsync(cancellationToken);
                var existing = await GetRefundDetailsAsync(refundId, cancellationToken);
                return existing ?? throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
            }

            if (refundRequest.Status is not (
                    CommerceHubRefundRequestStatus.Requested or
                    CommerceHubRefundRequestStatus.UnderReview or
                    CommerceHubRefundRequestStatus.Approved))
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_APPROVAL_NOT_ALLOWED",
                    $"Refund request status {refundRequest.Status} cannot be approved.",
                    StatusCodes.Status409Conflict);
            }

            if (refundRequest.Status != CommerceHubRefundRequestStatus.Approved)
            {
                refundRequest.ReviewedBy = actorId;
                refundRequest.ReviewedAtUtc = DateTime.UtcNow;
                refundRequest.ApprovedAtUtc = DateTime.UtcNow;
            }

            refundRequest.AdminNote = note ?? refundRequest.AdminNote;
            refundRequest.UpdatedAt = DateTime.UtcNow;
            refundRequest.UpdatedBy = actorId;
            refundRequest.Status = CommerceHubRefundRequestStatus.WaitingForReturn;

            await _returnManagementService.EnsureReturnRequestForApprovedRefundAsync(
                refundRequest,
                actorId,
                actorEmail,
                correlationId,
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await _auditService.LogActionAsync(
            action: "REFUND_APPROVED",
            entityType: "CommerceHubRefundRequest",
            entityId: refundId.ToString(),
            description: "Super Admin approved a CommerceHub refund request and moved it into the return-driven execution workflow.",
            success: true,
            details: new
            {
                RefundId = refundId,
                Note = note
            },
            actorId: actorId,
            actorEmail: actorEmail,
            cancellationToken: cancellationToken);

        return await GetRefundDetailsAsync(refundId, cancellationToken)
               ?? throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
    }

    public async Task<SuperAdminRefundDetailsDto> RejectAsync(
        Guid refundId,
        SuperAdminRejectRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default)
    {
        var reason = NormalizeRequired(request.Reason, "Refund rejection reason is required.");
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var refundRequest = await LoadRefundForUpdateAsync(refundId, cancellationToken);
        if (refundRequest == null)
        {
            throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
        }

        if (refundRequest.Status == CommerceHubRefundRequestStatus.Rejected)
        {
            await transaction.CommitAsync(cancellationToken);
            return await GetRefundDetailsAsync(refundId, cancellationToken)
                   ?? throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
        }

        if (refundRequest.Status is not (CommerceHubRefundRequestStatus.Requested or CommerceHubRefundRequestStatus.UnderReview))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_REJECTION_NOT_ALLOWED",
                $"Refund request status {refundRequest.Status} cannot be rejected.",
                StatusCodes.Status409Conflict);
        }

        refundRequest.Status = CommerceHubRefundRequestStatus.Rejected;
        refundRequest.ReviewedBy = actorId;
        refundRequest.ReviewedAtUtc = DateTime.UtcNow;
        refundRequest.RejectedAtUtc = DateTime.UtcNow;
        refundRequest.AdminNote = reason;
        refundRequest.UpdatedAt = DateTime.UtcNow;
        refundRequest.UpdatedBy = actorId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _auditService.LogActionAsync(
            action: "REFUND_REJECTED",
            entityType: "CommerceHubRefundRequest",
            entityId: refundId.ToString(),
            description: "Super Admin rejected a CommerceHub refund request.",
            success: true,
            details: new
            {
                RefundId = refundId,
                Reason = reason
            },
            actorId: actorId,
            actorEmail: actorEmail,
            cancellationToken: cancellationToken);

        return await GetRefundDetailsAsync(refundId, cancellationToken)
               ?? throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
    }

    public async Task<SuperAdminRefundDetailsDto> RetryAsync(
        Guid refundId,
        SuperAdminRetryRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default)
    {
        var note = NormalizeOptional(request.Note);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var refundRequest = await LoadRefundForUpdateAsync(refundId, cancellationToken);
        if (refundRequest == null)
        {
            throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
        }

        if (CommerceHubRefundRequestWorkflowPolicy.IsExecuted(refundRequest.Status) ||
            refundRequest.Status == CommerceHubRefundRequestStatus.Processing)
        {
            await transaction.CommitAsync(cancellationToken);
            return await GetRefundDetailsAsync(refundId, cancellationToken)
                   ?? throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
        }

        if (refundRequest.Status != CommerceHubRefundRequestStatus.Failed)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_RETRY_NOT_ALLOWED",
                $"Refund request status {refundRequest.Status} cannot be retried.",
                StatusCodes.Status409Conflict);
        }

        refundRequest.Status = CommerceHubRefundRequestStatus.WaitingForReturn;
        refundRequest.AdminNote = note ?? refundRequest.AdminNote;
        refundRequest.FailureReason = null;
        refundRequest.FailedAtUtc = null;
        refundRequest.ProcessingStartedAtUtc = null;
        refundRequest.CompletedAtUtc = null;
        refundRequest.UpdatedAt = DateTime.UtcNow;
        refundRequest.UpdatedBy = actorId;

        await _returnManagementService.EnsureReturnRequestForApprovedRefundAsync(
            refundRequest,
            actorId,
            actorEmail,
            cancellationToken: cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _auditService.LogActionAsync(
            action: "REFUND_RETRY_PREPARED",
            entityType: "CommerceHubRefundRequest",
            entityId: refundId.ToString(),
            description: "Super Admin reopened a failed CommerceHub refund and returned it to the return-driven execution workflow.",
            success: true,
            details: new
            {
                RefundId = refundId,
                Note = note
            },
            actorId: actorId,
            actorEmail: actorEmail,
            cancellationToken: cancellationToken);

        return await GetRefundDetailsAsync(refundId, cancellationToken)
               ?? throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
    }

    public async Task<SuperAdminRefundDetailsDto> ExecuteAsync(
        Guid refundId,
        SuperAdminExecuteRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await _refundExecutionService.ExecuteAsync(
            refundId,
            request,
            actorId,
            actorEmail,
            correlationId,
            cancellationToken);

        return await GetRefundDetailsAsync(refundId, cancellationToken)
               ?? throw new CommerceHubCheckoutException("REFUND_NOT_FOUND", "Refund request was not found.", StatusCodes.Status404NotFound);
    }

    private IQueryable<CommerceHubRefundRequest> BuildFilteredQuery(SuperAdminRefundsListQuery query)
    {
        var filteredQuery = _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(item => !item.IsDeleted);

        if (query.Status.HasValue)
        {
            if (query.Status.Value == CommerceHubRefundRequestStatus.Paid)
            {
                filteredQuery = filteredQuery.Where(item =>
                    item.Status == CommerceHubRefundRequestStatus.Paid ||
                    item.Status == CommerceHubRefundRequestStatus.Executed ||
                    item.Status == CommerceHubRefundRequestStatus.Completed);
            }
            else
            {
                filteredQuery = filteredQuery.Where(item => item.Status == query.Status.Value);
            }
        }

        if (query.VendorId.HasValue)
        {
            filteredQuery = filteredQuery.Where(item => item.VendorId == query.VendorId.Value);
        }

        if (query.OrderId.HasValue)
        {
            filteredQuery = filteredQuery.Where(item => item.OrderId == query.OrderId.Value);
        }

        if (query.FromDate.HasValue)
        {
            var fromDateUtc = DateTime.SpecifyKind(query.FromDate.Value.Date, DateTimeKind.Utc);
            filteredQuery = filteredQuery.Where(item => item.CreatedAt >= fromDateUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toDateExclusiveUtc = DateTime.SpecifyKind(query.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            filteredQuery = filteredQuery.Where(item => item.CreatedAt < toDateExclusiveUtc);
        }

        return filteredQuery;
    }

    private async Task<CommerceHubRefundRequest?> LoadRefundForUpdateAsync(Guid refundId, CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubRefundRequest> query = _dbContext.Database.IsRelational()
            ? _dbContext.CommerceHubRefundRequests.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubRefundRequests""
                WHERE ""Id"" = {refundId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE")
            : _dbContext.CommerceHubRefundRequests.Where(item => item.Id == refundId && !item.IsDeleted);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private Task<CommerceHubRefundRequest?> LoadRefundDetailsGraphAsync(Guid refundId, CancellationToken cancellationToken)
    {
        return _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Include(item => item.Order)
            .Include(item => item.Customer)
            .Include(item => item.OrderItem)
            .ThenInclude(item => item.SubOrder)
            .FirstOrDefaultAsync(item => item.Id == refundId && !item.IsDeleted, cancellationToken);
    }

    private async Task<SuperAdminRefundDetailsDto> MapDetailsAsync(
        CommerceHubRefundRequest refundRequest,
        CancellationToken cancellationToken)
    {
        var subOrder = refundRequest.OrderItem.SubOrder;
        var refundCurrency = CommerceHubRefundReturnCurrencyAuthority.RequireConsistentExecutionCurrency(
            refundRequest.Order,
            subOrder,
            refund: null,
            payment: null,
            $"refund-admin-details:{refundRequest.Id:D}");
        var executedRefund = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.RefundRequestId == refundRequest.Id && !item.IsDeleted, cancellationToken);
        var executionRecord = await _dbContext.CommerceHubRefundExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.RefundId == refundRequest.Id && !item.IsDeleted, cancellationToken);
        var returnRequest = await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.RefundId == refundRequest.Id && !item.IsDeleted, cancellationToken);

        var subOrderEarnings = await _dbContext.CommerceHubVendorEarnings
            .AsNoTracking()
            .Where(item => item.SubOrderId == subOrder.Id && !item.IsDeleted)
            .ToListAsync(cancellationToken);

        var payment = await ResolveRefundPaymentAsync(refundRequest.OrderId, subOrderEarnings, cancellationToken);
        var evidenceLookup = await _refundEvidenceService.GetEvidenceLookupAsync(
            new[] { refundRequest.Id },
            cancellationToken);
        var media = evidenceLookup.TryGetValue(refundRequest.Id, out var matchedMedia)
            ? matchedMedia
            : Array.Empty<RefundEvidenceMediaDto>();
        var refundBreakdown = RefundFinancialCalculator.CalculateItemBreakdown(refundRequest.OrderItem, refundRequest.Quantity);
        var commissionReversalAmount = refundBreakdown.CommissionReversalAmount;
        var vendorEarningChange = refundBreakdown.VendorEarningReversalAmount;
        var grossCommissionReversalAmount = refundBreakdown.GrossCommissionReversalAmount;
        var vendorNetEarningsBefore = RefundFinancialCalculator.RoundCurrency(subOrderEarnings.Sum(item => item.Amount));
        var hasPaidOutVendorEarnings = subOrderEarnings.Any(item => item.Status == CommerceHubVendorEarningStatus.PaidOut);
        var hasPendingPayoutReservation = subOrderEarnings.Any(item => item.Status == CommerceHubVendorEarningStatus.InPayout);
        var vendorPayableBefore = RefundFinancialCalculator.RoundCurrency(
            subOrderEarnings
                .Where(item => item.Status is not CommerceHubVendorEarningStatus.PaidOut)
                .Sum(item => item.Amount));
        var vendorDebtSnapshot = await _vendorDebtService.GetVendorDebtSnapshotAsync(refundRequest.VendorId, cancellationToken);
        var decision = RefundFinancialDecisionEngine.Create(
            subOrder.PaymentMethod,
            hasPaidOutVendorEarnings,
            refundRequest.TotalAmount,
            vendorEarningChange,
            commissionReversalAmount,
            grossCommissionReversalAmount,
            refundCurrency);
        var normalizedStatus = CommerceHubRefundRequestWorkflowPolicy.NormalizeStatus(refundRequest.Status);

        return new SuperAdminRefundDetailsDto
        {
            RefundId = refundRequest.Id,
            Status = normalizedStatus,
            Reason = CustomerRefundService.BuildReasonSummary(refundRequest.ReasonCode, refundRequest.ReasonText),
            ReasonCode = refundRequest.ReasonCode,
            ReasonText = refundRequest.ReasonText,
            Payout = refundRequest.PayoutMethodSnapshot.HasValue &&
                     !string.IsNullOrWhiteSpace(refundRequest.PayoutAccountMaskedSnapshot)
                ? new SuperAdminRefundPayoutInfoDto
                {
                    Method = refundRequest.PayoutMethodSnapshot.Value,
                    MaskedAccountIdentifier = refundRequest.PayoutAccountMaskedSnapshot!,
                    AccountHolderName = refundRequest.PayoutAccountHolderNameSnapshot,
                    IsVerified = false
                }
                : null,
            MediaUrls = media.Select(item => item.Url).ToArray(),
            Media = media,
            AdminNote = refundRequest.AdminNote,
            FailureReason = refundRequest.FailureReason ?? executionRecord?.FailureReason ?? executedRefund?.FailureReason,
            Quantity = refundRequest.Quantity,
            UnitPriceSnapshot = refundRequest.UnitPriceSnapshot,
            Amount = refundRequest.TotalAmount,
            Currency = refundCurrency,
            CreatedAt = refundRequest.CreatedAt,
            UpdatedAt = refundRequest.UpdatedAt,
            ReviewedAtUtc = refundRequest.ReviewedAtUtc,
            ApprovedAtUtc = refundRequest.ApprovedAtUtc,
            RejectedAtUtc = refundRequest.RejectedAtUtc,
            ProcessingStartedAtUtc = refundRequest.ProcessingStartedAtUtc,
            CompletedAtUtc = refundRequest.CompletedAtUtc,
            FailedAtUtc = refundRequest.FailedAtUtc,
            ExecutionAttempts = refundRequest.ExecutionAttempts,
            CanApprove = refundRequest.Status is CommerceHubRefundRequestStatus.Requested or CommerceHubRefundRequestStatus.UnderReview,
            CanReject = refundRequest.Status is CommerceHubRefundRequestStatus.Requested or CommerceHubRefundRequestStatus.UnderReview,
            CanExecute = refundRequest.PayoutMethodSnapshot.HasValue &&
                         !string.IsNullOrWhiteSpace(refundRequest.PayoutAccountSnapshot) &&
                         !string.IsNullOrWhiteSpace(refundRequest.PayoutAccountMaskedSnapshot) &&
                         (normalizedStatus is CommerceHubRefundRequestStatus.AwaitingPayout or CommerceHubRefundRequestStatus.Approved) &&
                         CommerceHubReturnPolicy.CanExecuteRefund(
                             refundRequest.RefundExecutionTrigger,
                             returnRequest?.Status) &&
                         !CommerceHubRefundRequestWorkflowPolicy.IsExecuted(refundRequest.Status),
            CanRetry = refundRequest.Status == CommerceHubRefundRequestStatus.Failed,
            Order = new SuperAdminRefundOrderInfoDto
            {
                OrderId = refundRequest.OrderId,
                OrderNumber = refundRequest.Order.OrderNumber,
                PaymentMethodName = refundRequest.Order.PaymentMethodName,
                PaymentStatus = refundRequest.Order.PaymentStatus,
                TotalAmount = refundRequest.Order.TotalAmount,
                ShippingTotal = refundRequest.Order.ShippingTotal,
                Currency = refundCurrency,
                CreatedAt = refundRequest.Order.CreatedAt
            },
            Customer = new SuperAdminRefundCustomerInfoDto
            {
                CustomerId = refundRequest.CustomerId,
                Name = refundRequest.Customer.Name,
                Email = refundRequest.Customer.Email,
                PhoneNumber = refundRequest.Customer.PhoneNumber
            },
            Item = new SuperAdminRefundItemInfoDto
            {
                OrderItemId = refundRequest.OrderItemId,
                SubOrderId = subOrder.Id,
                VendorId = refundRequest.VendorId,
                VendorName = subOrder.StoreName,
                ProductName = refundRequest.OrderItem.ProductName,
                VariantName = refundRequest.OrderItem.VariantName,
                ImageUrl = refundRequest.OrderItem.ImageUrl,
                PurchasedQuantity = refundRequest.OrderItem.Quantity,
                UnitPrice = refundRequest.OrderItem.UnitPrice,
                TotalPrice = refundRequest.OrderItem.Total,
                PlatformCommissionAmount = refundRequest.OrderItem.PlatformCommissionAmount,
                VendorEarningAmount = RefundFinancialCalculator.RoundCurrency(refundRequest.OrderItem.Total - refundRequest.OrderItem.PlatformCommissionAmount),
                Status = refundRequest.OrderItem.Status,
                DeliveredAt = refundRequest.OrderItem.DeliveredAt
            },
            FinancialPreview = new SuperAdminRefundFinancialPreviewDto
            {
                PaymentType = subOrder.PaymentMethod.ToString(),
                HasPayoutOccurred = hasPaidOutVendorEarnings,
                CommissionReversalAmount = commissionReversalAmount,
                VendorEarningChange = vendorEarningChange,
                PlatformCashDelta = decision.PlatformCashDelta,
                PlatformPayableDelta = decision.PlatformPayableDelta,
                VendorDebtDelta = decision.VendorDebtDelta,
                CommissionReceivableDelta = decision.CommissionReceivableDelta,
                GatewayRefundRequired = payment != null,
                PaymentId = payment?.Id,
                PaymentProvider = payment?.Provider,
                PaymentRecordStatus = payment?.Status,
                PaymentRefundedAmountBefore = payment?.RefundedAmount ?? 0m,
                PaymentRefundedAmountAfter = payment == null
                    ? 0m
                    : RefundFinancialCalculator.RoundCurrency(payment.RefundedAmount + refundRequest.TotalAmount),
                VendorNetEarningsBefore = vendorNetEarningsBefore,
                VendorNetEarningsAfter = RefundFinancialCalculator.RoundCurrency(vendorNetEarningsBefore - vendorEarningChange),
                VendorPayableBefore = vendorPayableBefore,
                VendorPayableAfter = RefundFinancialCalculator.RoundCurrency(
                    Math.Max(0m, vendorPayableBefore + decision.PlatformPayableDelta)),
                VendorDebtBefore = vendorDebtSnapshot.AmountOwedByVendor,
                VendorDebtAfter = RefundFinancialCalculator.RoundCurrency(
                    Math.Max(0m, vendorDebtSnapshot.AmountOwedByVendor + decision.VendorDebtDelta)),
                HasPaidOutVendorEarnings = hasPaidOutVendorEarnings,
                HasPendingPayoutReservation = hasPendingPayoutReservation,
                ExecutionPath = decision.ExecutionPath,
                ImpactSummary = decision.ImpactSummary
            },
            ReturnRequest = returnRequest == null
                ? null
                : CommerceHubReturnMapper.MapDetails(returnRequest, refundRequest),
            Execution = executionRecord == null && executedRefund == null
                ? null
                : new SuperAdminRefundExecutionDto
                {
                    ExecutionId = executionRecord?.Id ?? Guid.Empty,
                    Method = executionRecord?.Method ?? refundRequest.PayoutMethodSnapshot ?? RefundPayoutMethod.VodafoneCash,
                    MaskedAccountIdentifier = refundRequest.PayoutAccountMaskedSnapshot ?? "****",
                    TransactionReference = executionRecord?.TransactionReference ?? executedRefund?.Reference ?? string.Empty,
                    Notes = executionRecord?.Notes,
                    ExecutedBy = executionRecord?.ExecutedBy ?? Guid.Empty,
                    ExecutedAt = executionRecord?.ExecutedAt ?? executedRefund?.ProcessedAtUtc ?? refundRequest.CompletedAtUtc ?? refundRequest.CreatedAt,
                    IsSuccessful = executionRecord?.IsSuccessful ?? executedRefund?.Status == CommerceHubRefundStatus.Completed,
                    FailureReason = executionRecord?.FailureReason ?? executedRefund?.FailureReason,
                    FinancialRefundId = executedRefund?.Id ?? Guid.Empty,
                    Status = executedRefund?.Status ?? CommerceHubRefundStatus.Pending,
                    PaymentId = executedRefund?.PaymentId,
                    GatewayStatus = executedRefund?.GatewayStatus,
                    GatewayReference = executedRefund?.Reference,
                    CommissionReversalAmount = executedRefund?.CommissionReversalAmount ?? 0m,
                    VendorEarningReversalAmount = executedRefund?.VendorEarningReversalAmount ?? 0m,
                    IsPostPayoutAdjustment = executedRefund?.IsPostPayoutAdjustment ?? false,
                    ProcessingStartedAtUtc = executedRefund?.ProcessingStartedAtUtc,
                    ProcessedAtUtc = executedRefund?.ProcessedAtUtc
                }
        };
    }

    private async Task<CommerceHubPayment?> ResolveRefundPaymentAsync(
        Guid marketplaceOrderId,
        IReadOnlyCollection<CommerceHubVendorEarning> subOrderEarnings,
        CancellationToken cancellationToken)
    {
        var primaryPaymentId = subOrderEarnings
            .Where(item => item.OriginalEarningId == null && item.PaymentId.HasValue)
            .Select(item => item.PaymentId)
            .FirstOrDefault();

        if (primaryPaymentId.HasValue)
        {
            var payment = await _dbContext.CommerceHubPayments
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == primaryPaymentId.Value && !item.IsDeleted, cancellationToken);
            if (payment != null)
            {
                return payment;
            }
        }

        return await _dbContext.CommerceHubPayments
            .AsNoTracking()
            .Where(payment =>
                payment.OrderId == marketplaceOrderId &&
                payment.Status == CommerceHubPaymentRecordStatus.Paid &&
                !payment.IsDeleted)
            .OrderByDescending(payment => payment.PaidAtUtc ?? payment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private static string NormalizeRequired(string? value, string message)
    {
        var normalized = NormalizeOptional(value);
        if (normalized != null)
        {
            return normalized;
        }

        throw new CommerceHubCheckoutException("INVALID_REQUEST", message, StatusCodes.Status400BadRequest);
    }
}
