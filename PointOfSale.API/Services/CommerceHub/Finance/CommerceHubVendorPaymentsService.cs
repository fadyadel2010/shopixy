using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub.Finance;

public interface ICommerceHubVendorPaymentsService
{
    Task<CommerceHubVendorPaymentsDashboardDto> GetVendorDashboardAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubVendorPaymentHistoryItemDto> CreateVendorPaymentRequestAsync(
        Guid vendorId,
        CommerceHubCreateVendorPaymentRequestFormDto request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubVendorPaymentProofAccessDto> GetVendorProofAccessAsync(
        Guid vendorId,
        Guid paymentRequestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SuperAdminVendorPaymentMethodDto>> GetPaymentMethodsAsync(
        CancellationToken cancellationToken = default);

    Task<SuperAdminVendorPaymentMethodDto> CreatePaymentMethodAsync(
        SuperAdminCreateVendorPaymentMethodRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPagedResult<SuperAdminVendorPaymentRequestItemDto>> GetPaymentRequestsAsync(
        SuperAdminVendorPaymentRequestQuery query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminVendorPaymentRequestItemDto> ApprovePaymentRequestAsync(
        Guid paymentRequestId,
        SuperAdminApproveVendorPaymentRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminVendorPaymentRequestItemDto> RejectPaymentRequestAsync(
        Guid paymentRequestId,
        SuperAdminRejectVendorPaymentRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubVendorPaymentProofAccessDto> GetSuperAdminProofAccessAsync(
        Guid paymentRequestId,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubVendorPaymentsService : ICommerceHubVendorPaymentsService
{
    private const int MaxTraceKeyLength = 200;
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const long MaxVideoBytes = 50 * 1024 * 1024;
    private const string VendorPaymentLiveTransferReferenceUnique = "UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference";
    private const string VendorPaymentProofContentHashUnique = "UX_CommerceHubVendorPaymentProofs_ContentHash";

    private static readonly IReadOnlyDictionary<string, ProofFileRule> ProofFileRules =
        new Dictionary<string, ProofFileRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = new("image", ".jpg", MaxImageBytes),
            ["image/png"] = new("image", ".png", MaxImageBytes),
            ["image/webp"] = new("image", ".webp", MaxImageBytes),
            ["video/mp4"] = new("video", ".mp4", MaxVideoBytes)
        };

    private readonly ApplicationDbContext _dbContext;
    private readonly IR2StorageService _storageService;
    private readonly IFileValidationService _fileValidationService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;
    private readonly ICommerceHubFinancialLedgerService _financialLedgerService;
    private readonly ICommerceHubFinancialSafetyGuardService _financialSafetyGuardService;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ILogger<CommerceHubVendorPaymentsService> _logger;

    public CommerceHubVendorPaymentsService(
        ApplicationDbContext dbContext,
        IR2StorageService storageService,
        IFileValidationService fileValidationService,
        ICommerceHubVendorDebtService vendorDebtService,
        ICommerceHubFinancialLedgerService financialLedgerService,
        ICommerceHubFinancialSafetyGuardService financialSafetyGuardService,
        IUserGovernanceService userGovernanceService,
        ILogger<CommerceHubVendorPaymentsService> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _fileValidationService = fileValidationService;
        _vendorDebtService = vendorDebtService;
        _financialLedgerService = financialLedgerService;
        _financialSafetyGuardService = financialSafetyGuardService;
        _userGovernanceService = userGovernanceService;
        _logger = logger;
    }

    public async Task<CommerceHubVendorPaymentsDashboardDto> GetVendorDashboardAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var vendor = await LoadVendorAsync(vendorId, cancellationToken);

        var debtSnapshot = await _vendorDebtService.GetVendorDebtSnapshotAsync(vendorId, cancellationToken);
        var methods = await _dbContext.CommerceHubVendorPaymentMethods
            .AsNoTracking()
            .Where(method => method.IsActive)
            .OrderBy(method => method.Name)
            .ThenBy(method => method.CreatedAtUtc)
            .Select(method => new CommerceHubVendorPaymentMethodDto
            {
                Id = method.Id,
                Name = method.Name,
                DestinationValue = method.DestinationValue,
                Instructions = method.Instructions,
                IsActive = method.IsActive,
                CreatedAtUtc = method.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var historyRows = await _dbContext.CommerceHubVendorPaymentRequests
            .AsNoTracking()
            .Where(paymentRequest => paymentRequest.VendorId == vendorId)
            .GroupJoin(
                _dbContext.CommerceHubVendorPaymentProofs.AsNoTracking(),
                paymentRequest => paymentRequest.Id,
                proof => proof.PaymentRequestId,
                (paymentRequest, proofs) => new { paymentRequest, proof = proofs.FirstOrDefault() })
            .OrderByDescending(row => row.paymentRequest.CreatedAtUtc)
            .ThenByDescending(row => row.paymentRequest.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        return new CommerceHubVendorPaymentsDashboardDto
        {
            VendorId = vendorId,
            AmountOwedByVendor = debtSnapshot.AmountOwedByVendor,
            Currency = NormalizeCurrency(FinancialCurrencyGuard.PlatformBaseCurrencyCode),
            DebtStatus = debtSnapshot.Status,
            RequiredAction = debtSnapshot.RequiredAction,
            CanSubmitPayment = debtSnapshot.AmountOwedByVendor > 0m && methods.Count > 0,
            PaymentMethods = methods,
            PaymentHistory = historyRows
                .Select(row => MapVendorHistoryItem(row.paymentRequest, row.proof))
                .ToList()
        };
    }

    public async Task<CommerceHubVendorPaymentHistoryItemDto> CreateVendorPaymentRequestAsync(
        Guid vendorId,
        CommerceHubCreateVendorPaymentRequestFormDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProofFile == null || request.ProofFile.Length <= 0)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_PROOF_REQUIRED",
                "Payment proof is required.",
                StatusCodes.Status400BadRequest);
        }

        var vendor = await LoadVendorAsync(vendorId, cancellationToken);
        try
        {
            await _userGovernanceService.EnsureVendorRepaymentAllowedAsync(vendorId, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            throw new CommerceHubVendorFinancialControlException(
                exception.Code,
                exception.Message,
                exception.StatusCode);
        }

        var normalizedAmount = NormalizeAmount(request.Amount);
        var normalizedCurrency = request.Currency != null
            ? ValidateRequestCurrency(request.Currency)
            : NormalizeCurrency(FinancialCurrencyGuard.PlatformBaseCurrencyCode);
        var normalizedIdempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var normalizedTransferReference = NormalizeOptional(request.TransferReference, 200);
        var proofFile = await ValidateProofFileAsync(request.ProofFile, cancellationToken);

        var existingRequest = await LoadPaymentRequestByVendorIdempotencyAsync(
            vendorId,
            normalizedIdempotencyKey,
            cancellationToken);

        if (existingRequest != null)
        {
            ValidateVendorSubmissionReplay(
                existingRequest,
                request.MethodId,
                normalizedAmount,
                normalizedCurrency,
                normalizedTransferReference,
                proofFile);

            return MapVendorHistoryItem(existingRequest, existingRequest.Proof);
        }

        var debtSnapshot = await _vendorDebtService.GetVendorDebtSnapshotAsync(vendorId, cancellationToken);
        EnsureVendorDebtAllowsSubmission(debtSnapshot, normalizedAmount);

        var paymentMethod = await LoadActivePaymentMethodAsync(request.MethodId, cancellationToken);
        var paymentRequestId = BuildDeterministicPaymentRequestId(vendorId, normalizedIdempotencyKey);
        await EnsureExternalVendorRepaymentEvidenceNotReusedAsync(
            paymentMethod.Id,
            normalizedTransferReference,
            proofFile.ContentHash,
            paymentRequestId,
            cancellationToken);

        var attemptId = Guid.NewGuid();

        await using var proofStream = request.ProofFile.OpenReadStream();
        var uploadResult = await _storageService.UploadFileAsync(
            proofStream,
            proofFile.ContentType,
            BuildProofUploadPathForAttempt(vendorId, paymentRequestId, attemptId),
            cancellationToken);

        var paymentRequest = CommerceHubVendorPaymentRequest.Create(
            vendorId,
            paymentMethod,
            normalizedAmount,
            normalizedCurrency,
            normalizedTransferReference,
            normalizedIdempotencyKey,
            requestId: paymentRequestId);

        var paymentProof = CommerceHubVendorPaymentProof.Create(
            paymentRequest.Id,
            uploadResult.ObjectKey,
            proofFile.FileName,
            proofFile.ContentType,
            proofFile.SizeBytes,
            proofFile.ContentHash);

        _dbContext.CommerceHubVendorPaymentRequests.Add(paymentRequest);
        _dbContext.CommerceHubVendorPaymentProofs.Add(paymentProof);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MapVendorHistoryItem(paymentRequest, paymentProof);
        }
        catch (DbUpdateException exception) when (IsVendorPaymentRequestDuplicateException(exception))
        {
            _dbContext.ChangeTracker.Clear();
            await DeleteObjectSafelyAsync(uploadResult.ObjectKey, cancellationToken);

            var replay = await LoadPaymentRequestByVendorIdempotencyAsync(
                vendorId,
                normalizedIdempotencyKey,
                cancellationToken);

            if (replay == null)
            {
                throw;
            }

            ValidateVendorSubmissionReplay(
                replay,
                request.MethodId,
                normalizedAmount,
                normalizedCurrency,
                normalizedTransferReference,
                proofFile);

            return MapVendorHistoryItem(replay, replay.Proof);
        }
        catch (DbUpdateException exception) when (IsVendorPaymentEvidenceUniqueViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            await DeleteObjectSafelyAsync(uploadResult.ObjectKey, cancellationToken);

            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_EVIDENCE_ALREADY_USED",
                "This transfer reference or proof file is already attached to another active vendor repayment request.",
                StatusCodes.Status409Conflict);
        }
        catch
        {
            await DeleteObjectSafelyAsync(uploadResult.ObjectKey, cancellationToken);
            throw;
        }
    }

    public async Task<CommerceHubVendorPaymentProofAccessDto> GetVendorProofAccessAsync(
        Guid vendorId,
        Guid paymentRequestId,
        CancellationToken cancellationToken = default)
    {
        var proof = await _dbContext.CommerceHubVendorPaymentProofs
            .AsNoTracking()
            .Join(
                _dbContext.CommerceHubVendorPaymentRequests.AsNoTracking(),
                paymentProof => paymentProof.PaymentRequestId,
                paymentRequest => paymentRequest.Id,
                (paymentProof, paymentRequest) => new { paymentProof, paymentRequest })
            .Where(row => row.paymentRequest.Id == paymentRequestId && row.paymentRequest.VendorId == vendorId)
            .Select(row => row.paymentProof)
            .SingleOrDefaultAsync(cancellationToken);

        if (proof == null)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_PROOF_NOT_FOUND",
                "Payment proof was not found.",
                StatusCodes.Status404NotFound);
        }

        return await BuildProofAccessDtoAsync(proof, cancellationToken);
    }

    public async Task<IReadOnlyList<SuperAdminVendorPaymentMethodDto>> GetPaymentMethodsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.CommerceHubVendorPaymentMethods
            .AsNoTracking()
            .OrderByDescending(method => method.IsActive)
            .ThenBy(method => method.Name)
            .ThenByDescending(method => method.CreatedAtUtc)
            .Select(method => new SuperAdminVendorPaymentMethodDto
            {
                Id = method.Id,
                Name = method.Name,
                DestinationValue = method.DestinationValue,
                Instructions = method.Instructions,
                IsActive = method.IsActive,
                CreatedAtUtc = method.CreatedAtUtc,
                CreatedBy = method.CreatedBy
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<SuperAdminVendorPaymentMethodDto> CreatePaymentMethodAsync(
        SuperAdminCreateVendorPaymentMethodRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedActorId = NormalizeActorId(actorId);
        var paymentMethod = CommerceHubVendorPaymentMethod.Create(
            request.Name,
            request.DestinationValue,
            request.Instructions,
            request.IsActive,
            normalizedActorId);

        _dbContext.CommerceHubVendorPaymentMethods.Add(paymentMethod);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsVendorPaymentMethodDuplicateException(exception))
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_METHOD_ALREADY_EXISTS",
                "A payment method with the same name and destination value already exists.",
                StatusCodes.Status409Conflict);
        }

        return new SuperAdminVendorPaymentMethodDto
        {
            Id = paymentMethod.Id,
            Name = paymentMethod.Name,
            DestinationValue = paymentMethod.DestinationValue,
            Instructions = paymentMethod.Instructions,
            IsActive = paymentMethod.IsActive,
            CreatedAtUtc = paymentMethod.CreatedAtUtc,
            CreatedBy = paymentMethod.CreatedBy
        };
    }

    public async Task<SuperAdminPagedResult<SuperAdminVendorPaymentRequestItemDto>> GetPaymentRequestsAsync(
        SuperAdminVendorPaymentRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var paymentRequests = _dbContext.CommerceHubVendorPaymentRequests.AsNoTracking();
        if (query.Status.HasValue)
        {
            paymentRequests = paymentRequests.Where(paymentRequest => paymentRequest.Status == query.Status.Value);
        }

        if (query.VendorId.HasValue && query.VendorId.Value != Guid.Empty)
        {
            paymentRequests = paymentRequests.Where(paymentRequest => paymentRequest.VendorId == query.VendorId.Value);
        }

        var rows = paymentRequests
            .Join(
                _dbContext.Tenants.AsNoTracking().Where(tenant => tenant.DeletedAtUtc == null),
                paymentRequest => paymentRequest.VendorId,
                tenant => tenant.Id,
                (paymentRequest, tenant) => new
                {
                    paymentRequest,
                    tenant.Name,
                    tenant.Slug,
                    tenant.ContactEmail
                })
            .GroupJoin(
                _dbContext.CommerceHubVendorPaymentProofs.AsNoTracking(),
                row => row.paymentRequest.Id,
                proof => proof.PaymentRequestId,
                (row, proofs) => new PaymentRequestProjection
                {
                    PaymentRequestId = row.paymentRequest.Id,
                    VendorId = row.paymentRequest.VendorId,
                    VendorName = row.Name,
                    VendorSlug = row.Slug,
                    VendorEmail = row.ContactEmail,
                    PaymentMethodId = row.paymentRequest.PaymentMethodId,
                    MethodName = row.paymentRequest.MethodNameSnapshot,
                    DestinationValue = row.paymentRequest.DestinationValueSnapshot,
                    Instructions = row.paymentRequest.InstructionsSnapshot,
                    Amount = row.paymentRequest.Amount,
                    Currency = row.paymentRequest.Currency,
                    Status = row.paymentRequest.Status,
                    TransferReference = row.paymentRequest.TransferReference,
                    CreatedAtUtc = row.paymentRequest.CreatedAtUtc,
                    ApprovedAtUtc = row.paymentRequest.ApprovedAtUtc,
                    ApprovedBy = row.paymentRequest.ApprovedBy,
                    RejectedAtUtc = row.paymentRequest.RejectedAtUtc,
                    RejectedBy = row.paymentRequest.RejectedBy,
                    ReviewReason = row.paymentRequest.ReviewReason,
                    LedgerCorrelationId = row.paymentRequest.LedgerCorrelationId,
                    LedgerIdempotencyKey = row.paymentRequest.LedgerIdempotencyKey,
                    Proof = proofs.Select(proof => new PaymentProofProjection
                    {
                        ContentType = proof.ContentType,
                        FileName = proof.OriginalFileName,
                        SizeBytes = proof.SizeBytes
                    }).FirstOrDefault()
                });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            rows = rows.Where(row =>
                row.MethodName.ToLower().Contains(search) ||
                (row.TransferReference != null && row.TransferReference.ToLower().Contains(search)) ||
                (row.VendorName != null && row.VendorName.ToLower().Contains(search)) ||
                (row.VendorSlug != null && row.VendorSlug.ToLower().Contains(search)) ||
                (row.VendorEmail != null && row.VendorEmail.ToLower().Contains(search)));
        }

        var totalCount = await rows.CountAsync(cancellationToken);
        var pageRows = await rows
            .OrderBy(row => row.Status)
            .ThenByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.PaymentRequestId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new SuperAdminPagedResult<SuperAdminVendorPaymentRequestItemDto>
        {
            Items = pageRows.Select(MapSuperAdminPaymentRequestItem).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize)
        };
    }

    public async Task<SuperAdminVendorPaymentRequestItemDto> ApprovePaymentRequestAsync(
        Guid paymentRequestId,
        SuperAdminApproveVendorPaymentRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedActorId = NormalizeActorId(actorId);
        var normalizedReason = NormalizeReason(request.Reason);
        var ownsTransaction = _dbContext.Database.CurrentTransaction == null && _dbContext.Database.IsRelational();

        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var paymentRequest = await LoadTrackedPaymentRequestAsync(
                paymentRequestId,
                lockForUpdate: _dbContext.Database.IsRelational(),
                cancellationToken);

            var vendor = await LoadVendorAsync(paymentRequest.VendorId, cancellationToken);
            var normalizedApprovalCurrency = NormalizeCurrency(paymentRequest.Currency);
            try
            {
                await _userGovernanceService.EnsureVendorRepaymentAllowedAsync(
                    paymentRequest.VendorId,
                    cancellationToken);
            }
            catch (UserGovernanceException exception)
            {
                throw new CommerceHubVendorFinancialControlException(
                    exception.Code,
                    exception.Message,
                    exception.StatusCode);
            }

            if (paymentRequest.Status == CommerceHubVendorPaymentRequestStatus.Approved)
            {
                if (paymentRequest.ApprovedBy != normalizedActorId)
                {
                    throw new CommerceHubVendorFinancialControlException(
                        "PAYMENT_REQUEST_ALREADY_APPROVED",
                        "Payment request was already approved by another admin.",
                        StatusCodes.Status409Conflict);
                }

                await EnsureApprovedPaymentIntegrityAsync(paymentRequest, cancellationToken);

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return await GetPaymentRequestItemAsync(paymentRequest.Id, cancellationToken);
            }

            if (paymentRequest.Status == CommerceHubVendorPaymentRequestStatus.Rejected)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "PAYMENT_REQUEST_ALREADY_REJECTED",
                    "Rejected payment requests cannot be approved.",
                    StatusCodes.Status409Conflict);
            }

            if (paymentRequest.Proof == null)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "PAYMENT_PROOF_REQUIRED",
                    "Approved vendor repayment requests must include payment proof.",
                    StatusCodes.Status409Conflict);
            }

            EnsureApprovedRepaymentHasExternalTransferReference(paymentRequest);

            await EnsureExternalVendorRepaymentEvidenceNotReusedAsync(
                paymentRequest.PaymentMethodId,
                paymentRequest.TransferReference,
                paymentRequest.Proof.ContentHash,
                paymentRequest.Id,
                cancellationToken);

            await EnsurePendingRequestHasNoLedgerEntriesAsync(paymentRequest, cancellationToken);

            var stateBefore = await _financialSafetyGuardService.CaptureStateForUpdateAsync(
                paymentRequest.VendorId,
                normalizedApprovalCurrency,
                cancellationToken);

            var debtSnapshot = await _vendorDebtService.GetVendorDebtSnapshotAsync(paymentRequest.VendorId, cancellationToken);
            EnsureApprovalAmountIsValid(paymentRequest, debtSnapshot, request.AllowOverpayment);
            if (!request.AllowOverpayment && paymentRequest.Amount > stateBefore.VendorDebt)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "PAYMENT_AMOUNT_EXCEEDS_DEBT",
                    "Approved amount exceeds the vendor's outstanding debt.",
                    StatusCodes.Status409Conflict);
            }

            var correlationId = BuildPaymentCorrelationId(paymentRequest.Id);
            var ledgerIdempotencyKey = paymentRequest.LedgerIdempotencyKey ?? BuildApprovalIdempotencyKey(paymentRequest.Id);
            var vendorLedgerIdempotencyKey = BuildScopedIdempotencyKey(ledgerIdempotencyKey, "vendor");
            var platformLedgerIdempotencyKey = BuildScopedIdempotencyKey(ledgerIdempotencyKey, "platform");

            await _financialLedgerService.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Vendor,
                paymentRequest.VendorId,
                CommerceHubFinancialTransactionType.Settlement,
                CommerceHubFinancialTransactionDirection.Credit,
                paymentRequest.Amount,
                normalizedApprovalCurrency,
                CommerceHubFinancialReferenceType.Settlement,
                paymentRequest.Id,
                BuildVendorApprovalDescription(vendor.Name, paymentRequest.Id, normalizedReason),
                vendorLedgerIdempotencyKey,
                correlationId,
                normalizedActorId,
                normalizedReason,
                cancellationToken: cancellationToken);

            await _financialLedgerService.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Platform,
                entityId: null,
                CommerceHubFinancialTransactionType.Settlement,
                CommerceHubFinancialTransactionDirection.Credit,
                paymentRequest.Amount,
                normalizedApprovalCurrency,
                CommerceHubFinancialReferenceType.Settlement,
                paymentRequest.Id,
                BuildPlatformApprovalDescription(vendor.Name, paymentRequest.Id, normalizedReason),
                platformLedgerIdempotencyKey,
                correlationId,
                normalizedActorId,
                normalizedReason,
                accountingTenantId: paymentRequest.VendorId,
                cancellationToken: cancellationToken);

            paymentRequest.Approve(
                normalizedActorId,
                normalizedReason,
                correlationId,
                ledgerIdempotencyKey);

            _dbContext.CommerceHubVendorPaymentAuditLogs.Add(
                CommerceHubVendorPaymentAuditLog.Create(
                    paymentRequest.Id,
                    normalizedActorId,
                    CommerceHubVendorPaymentAuditAction.Approved,
                    paymentRequest.Amount,
                    normalizedApprovalCurrency,
                    normalizedReason));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnsureApprovedPaymentIntegrityAsync(paymentRequest, cancellationToken);

            var stateAfter = await _financialSafetyGuardService.VerifyStateTransitionAsync(
                paymentRequest.VendorId,
                normalizedApprovalCurrency,
                stateBefore,
                new CommerceHubFinancialStateDelta(
                    PlatformCashDelta: paymentRequest.Amount,
                    VendorPayableDelta: 0m,
                    VendorDebtDelta: -paymentRequest.Amount),
                correlationId,
                expectedLedgerEntries: 2,
                CommerceHubFinancialReferenceType.Settlement,
                paymentRequest.Id,
                cancellationToken);

            _financialSafetyGuardService.AppendCommittedOperationAudit(
                paymentRequest.VendorId,
                normalizedActorId,
                "SUPER_ADMIN",
                "VendorRepayment",
                paymentRequest.Amount,
                normalizedApprovalCurrency,
                stateBefore,
                stateAfter,
                correlationId,
                ledgerIdempotencyKey,
                CommerceHubFinancialReferenceType.Settlement,
                paymentRequest.Id,
                normalizedReason);

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return await GetPaymentRequestItemAsync(paymentRequest.Id, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsLedgerDuplicateException(exception))
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            var replay = await LoadPaymentRequestSnapshotAsync(paymentRequestId, cancellationToken);
            if (replay?.Status == CommerceHubVendorPaymentRequestStatus.Approved && replay.ApprovedBy == normalizedActorId)
            {
                await EnsureApprovedPaymentIntegrityAsync(replay, cancellationToken);
                return MapSuperAdminPaymentRequestItem(replay);
            }

            if (replay?.Status == CommerceHubVendorPaymentRequestStatus.Approved)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "PAYMENT_REQUEST_ALREADY_APPROVED",
                    "Payment request was already approved.",
                    StatusCodes.Status409Conflict);
            }

            throw;
        }
        catch (DbUpdateException exception) when (IsVendorPaymentEvidenceUniqueViolation(exception))
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_EVIDENCE_ALREADY_USED",
                "This transfer reference or proof file is already attached to another active vendor repayment request.",
                StatusCodes.Status409Conflict);
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    public async Task<SuperAdminVendorPaymentRequestItemDto> RejectPaymentRequestAsync(
        Guid paymentRequestId,
        SuperAdminRejectVendorPaymentRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedActorId = NormalizeActorId(actorId);
        var normalizedReason = NormalizeReason(request.Reason);
        var ownsTransaction = _dbContext.Database.CurrentTransaction == null && _dbContext.Database.IsRelational();

        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            var paymentRequest = await LoadTrackedPaymentRequestAsync(
                paymentRequestId,
                lockForUpdate: _dbContext.Database.IsRelational(),
                cancellationToken);
            var normalizedRejectedCurrency = NormalizeCurrency(paymentRequest.Currency);

            if (paymentRequest.Status == CommerceHubVendorPaymentRequestStatus.Approved)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "PAYMENT_REQUEST_ALREADY_APPROVED",
                    "Approved payment requests cannot be rejected.",
                    StatusCodes.Status409Conflict);
            }

            if (paymentRequest.Status == CommerceHubVendorPaymentRequestStatus.Rejected)
            {
                if (paymentRequest.RejectedBy != normalizedActorId)
                {
                    throw new CommerceHubVendorFinancialControlException(
                        "PAYMENT_REQUEST_ALREADY_REJECTED",
                        "Payment request was already rejected by another admin.",
                        StatusCodes.Status409Conflict);
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return await GetPaymentRequestItemAsync(paymentRequest.Id, cancellationToken);
            }

            await EnsurePendingRequestHasNoLedgerEntriesAsync(paymentRequest, cancellationToken);

            paymentRequest.Reject(normalizedActorId, normalizedReason);
            _dbContext.CommerceHubVendorPaymentAuditLogs.Add(
                CommerceHubVendorPaymentAuditLog.Create(
                    paymentRequest.Id,
                    normalizedActorId,
                    CommerceHubVendorPaymentAuditAction.Rejected,
                    paymentRequest.Amount,
                    normalizedRejectedCurrency,
                    normalizedReason));

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return await GetPaymentRequestItemAsync(paymentRequest.Id, cancellationToken);
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    public async Task<CommerceHubVendorPaymentProofAccessDto> GetSuperAdminProofAccessAsync(
        Guid paymentRequestId,
        CancellationToken cancellationToken = default)
    {
        var proof = await _dbContext.CommerceHubVendorPaymentProofs
            .AsNoTracking()
            .SingleOrDefaultAsync(paymentProof => paymentProof.PaymentRequestId == paymentRequestId, cancellationToken);

        if (proof == null)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_PROOF_NOT_FOUND",
                "Payment proof was not found.",
                StatusCodes.Status404NotFound);
        }

        return await BuildProofAccessDtoAsync(proof, cancellationToken);
    }

    private async Task<CommerceHubVendorPaymentProofAccessDto> BuildProofAccessDtoAsync(
        CommerceHubVendorPaymentProof proof,
        CancellationToken cancellationToken)
    {
        var fileUrl = await _storageService.CreateDownloadUrlAsync(
            proof.ObjectKey,
            TimeSpan.FromMinutes(10),
            cancellationToken);

        return new CommerceHubVendorPaymentProofAccessDto
        {
            FileUrl = fileUrl,
            ContentType = proof.ContentType,
            FileName = proof.OriginalFileName,
            SizeBytes = proof.SizeBytes,
            UploadedAtUtc = proof.UploadedAtUtc
        };
    }

    private async Task<CommerceHubVendorPaymentRequest?> LoadPaymentRequestByVendorIdempotencyAsync(
        Guid vendorId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubVendorPaymentRequests
            .AsNoTracking()
            .Include(paymentRequest => paymentRequest.Proof)
            .SingleOrDefaultAsync(
                paymentRequest =>
                    paymentRequest.VendorId == vendorId &&
                    paymentRequest.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    private async Task<CommerceHubVendorPaymentMethod> LoadActivePaymentMethodAsync(
        Guid paymentMethodId,
        CancellationToken cancellationToken)
    {
        if (paymentMethodId == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_METHOD_REQUIRED",
                "Payment method is required.",
                StatusCodes.Status400BadRequest);
        }

        var paymentMethod = await _dbContext.CommerceHubVendorPaymentMethods
            .AsNoTracking()
            .SingleOrDefaultAsync(method => method.Id == paymentMethodId && method.IsActive, cancellationToken);

        if (paymentMethod == null)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_METHOD_NOT_FOUND",
                "Payment method was not found or is inactive.",
                StatusCodes.Status404NotFound);
        }

        return paymentMethod;
    }

    private async Task<VendorLookup> LoadVendorAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        if (vendorId == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_REQUIRED",
                "VendorId is required.",
                StatusCodes.Status400BadRequest);
        }

        var vendor = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == vendorId && tenant.DeletedAtUtc == null)
            .Select(tenant => new VendorLookup(
                tenant.Id,
                tenant.Name != null && tenant.Name != string.Empty
                    ? tenant.Name
                    : tenant.Slug != null && tenant.Slug != string.Empty
                        ? tenant.Slug
                        : tenant.ContactEmail != null && tenant.ContactEmail != string.Empty
                            ? tenant.ContactEmail
                            : tenant.Id.ToString(),
                FinancialCurrencyGuard.PlatformBaseCurrencyCode))
            .SingleOrDefaultAsync(cancellationToken);

        if (vendor == null)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_NOT_FOUND",
                "Vendor was not found.",
                StatusCodes.Status404NotFound);
        }

        return vendor;
    }

    private async Task<CommerceHubVendorPaymentRequest> LoadTrackedPaymentRequestAsync(
        Guid paymentRequestId,
        bool lockForUpdate,
        CancellationToken cancellationToken)
    {
        if (paymentRequestId == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_REQUEST_REQUIRED",
                "Payment request is required.",
                StatusCodes.Status400BadRequest);
        }

        CommerceHubVendorPaymentRequest? paymentRequest;
        if (lockForUpdate && _dbContext.Database.IsRelational())
        {
            paymentRequest = await _dbContext.CommerceHubVendorPaymentRequests
                .FromSqlInterpolated($@"SELECT * FROM ""CommerceHubVendorPaymentRequests"" WHERE ""Id"" = {paymentRequestId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);

            if (paymentRequest != null)
            {
                await _dbContext.Entry(paymentRequest)
                    .Reference(entry => entry.Proof)
                    .LoadAsync(cancellationToken);
            }
        }
        else
        {
            paymentRequest = await _dbContext.CommerceHubVendorPaymentRequests
                .Include(entry => entry.Proof)
                .SingleOrDefaultAsync(entry => entry.Id == paymentRequestId, cancellationToken);
        }

        if (paymentRequest == null)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_REQUEST_NOT_FOUND",
                "Payment request was not found.",
                StatusCodes.Status404NotFound);
        }

        return paymentRequest;
    }

    private async Task<SuperAdminVendorPaymentRequestItemDto> GetPaymentRequestItemAsync(
        Guid paymentRequestId,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadPaymentRequestSnapshotAsync(paymentRequestId, cancellationToken)
            ?? throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_REQUEST_NOT_FOUND",
                "Payment request was not found.",
                StatusCodes.Status404NotFound);

        return MapSuperAdminPaymentRequestItem(snapshot);
    }

    private async Task<PaymentRequestProjection?> LoadPaymentRequestSnapshotAsync(
        Guid paymentRequestId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubVendorPaymentRequests
            .AsNoTracking()
            .Where(paymentRequest => paymentRequest.Id == paymentRequestId)
            .Join(
                _dbContext.Tenants.AsNoTracking().Where(tenant => tenant.DeletedAtUtc == null),
                paymentRequest => paymentRequest.VendorId,
                tenant => tenant.Id,
                (paymentRequest, tenant) => new
                {
                    paymentRequest,
                    tenant.Name,
                    tenant.Slug,
                    tenant.ContactEmail
                })
            .GroupJoin(
                _dbContext.CommerceHubVendorPaymentProofs.AsNoTracking(),
                row => row.paymentRequest.Id,
                proof => proof.PaymentRequestId,
                (row, proofs) => new PaymentRequestProjection
                {
                    PaymentRequestId = row.paymentRequest.Id,
                    VendorId = row.paymentRequest.VendorId,
                    VendorName = row.Name,
                    VendorSlug = row.Slug,
                    VendorEmail = row.ContactEmail,
                    PaymentMethodId = row.paymentRequest.PaymentMethodId,
                    MethodName = row.paymentRequest.MethodNameSnapshot,
                    DestinationValue = row.paymentRequest.DestinationValueSnapshot,
                    Instructions = row.paymentRequest.InstructionsSnapshot,
                    Amount = row.paymentRequest.Amount,
                    Currency = row.paymentRequest.Currency,
                    Status = row.paymentRequest.Status,
                    TransferReference = row.paymentRequest.TransferReference,
                    CreatedAtUtc = row.paymentRequest.CreatedAtUtc,
                    ApprovedAtUtc = row.paymentRequest.ApprovedAtUtc,
                    ApprovedBy = row.paymentRequest.ApprovedBy,
                    RejectedAtUtc = row.paymentRequest.RejectedAtUtc,
                    RejectedBy = row.paymentRequest.RejectedBy,
                    ReviewReason = row.paymentRequest.ReviewReason,
                    LedgerCorrelationId = row.paymentRequest.LedgerCorrelationId,
                    LedgerIdempotencyKey = row.paymentRequest.LedgerIdempotencyKey,
                    Proof = proofs.Select(proof => new PaymentProofProjection
                    {
                        ContentType = proof.ContentType,
                        FileName = proof.OriginalFileName,
                        SizeBytes = proof.SizeBytes
                    }).FirstOrDefault()
                })
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task EnsurePendingRequestHasNoLedgerEntriesAsync(
        CommerceHubVendorPaymentRequest paymentRequest,
        CancellationToken cancellationToken)
    {
        var correlationId = BuildPaymentCorrelationId(paymentRequest.Id);
        var hasLedgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .AnyAsync(
                transaction =>
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Settlement &&
                     transaction.ReferenceId == paymentRequest.Id) ||
                    transaction.CorrelationId == correlationId,
                cancellationToken);

        if (hasLedgerEntries)
        {
            throw new CommerceHubVendorFinancialControlException(
                "FINANCIAL_INTEGRITY_VIOLATION",
                "Pending payment request already has CommerceHub ledger entries.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task EnsureExternalVendorRepaymentEvidenceNotReusedAsync(
        Guid paymentMethodId,
        string? transferReference,
        string contentHash,
        Guid currentPaymentRequestId,
        CancellationToken cancellationToken)
    {
        var normalizedTransferReference = NormalizeOptional(transferReference, 200);
        var normalizedContentHash = contentHash.Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(normalizedTransferReference))
        {
            var transferReferenceInUse = await _dbContext.CommerceHubVendorPaymentRequests
                .AsNoTracking()
                .AnyAsync(
                    paymentRequest =>
                        paymentRequest.Id != currentPaymentRequestId &&
                        paymentRequest.PaymentMethodId == paymentMethodId &&
                        paymentRequest.TransferReference == normalizedTransferReference &&
                        paymentRequest.Status != CommerceHubVendorPaymentRequestStatus.Rejected,
                    cancellationToken);

            if (transferReferenceInUse)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "PAYMENT_EVIDENCE_ALREADY_USED",
                    "This transfer reference is already attached to another active vendor repayment request.",
                    StatusCodes.Status409Conflict);
            }
        }

        if (!CommerceHubVendorPaymentProof.IsLegacyContentHash(normalizedContentHash))
        {
            var proofHashInUse = await _dbContext.CommerceHubVendorPaymentProofs
                .AsNoTracking()
                .Join(
                    _dbContext.CommerceHubVendorPaymentRequests.AsNoTracking(),
                    proof => proof.PaymentRequestId,
                    paymentRequest => paymentRequest.Id,
                    (proof, paymentRequest) => new { proof, paymentRequest })
                .AnyAsync(
                    row =>
                        row.paymentRequest.Id != currentPaymentRequestId &&
                        row.proof.ContentHash == normalizedContentHash,
                    cancellationToken);

            if (proofHashInUse)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "PAYMENT_EVIDENCE_ALREADY_USED",
                    "This proof file is already attached to another active vendor repayment request.",
                    StatusCodes.Status409Conflict);
            }
        }
    }

    private static void EnsureApprovedRepaymentHasExternalTransferReference(
        CommerceHubVendorPaymentRequest paymentRequest)
    {
        if (!string.IsNullOrWhiteSpace(paymentRequest.TransferReference))
        {
            return;
        }

        throw new CommerceHubVendorFinancialControlException(
            "PAYMENT_TRANSFER_REFERENCE_REQUIRED",
            "Approved vendor repayment requests require the external bank or provider transfer reference that proves money moved.",
            StatusCodes.Status409Conflict);
    }

    private async Task EnsureApprovedPaymentIntegrityAsync(
        CommerceHubVendorPaymentRequest paymentRequest,
        CancellationToken cancellationToken)
    {
        EnsureApprovedRepaymentHasExternalTransferReference(paymentRequest);

        var approvedBy = paymentRequest.ApprovedBy
            ?? throw new CommerceHubVendorFinancialControlException(
                "FINANCIAL_INTEGRITY_VIOLATION",
                "Approved payment request is missing the approving admin trace.",
                StatusCodes.Status409Conflict);

        var correlationId = paymentRequest.LedgerCorrelationId ?? BuildPaymentCorrelationId(paymentRequest.Id);
        var baseIdempotencyKey = paymentRequest.LedgerIdempotencyKey ?? BuildApprovalIdempotencyKey(paymentRequest.Id);
        var vendorIdempotencyKey = BuildScopedIdempotencyKey(baseIdempotencyKey, "vendor");
        var platformIdempotencyKey = BuildScopedIdempotencyKey(baseIdempotencyKey, "platform");

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction => transaction.CorrelationId == correlationId)
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);

        if (entries.Count != 2 ||
            entries.Count(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Vendor) != 1 ||
            entries.Count(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Platform) != 1)
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                paymentRequest.Id,
                "Approved payment requests must persist exactly one vendor settlement entry and one platform settlement entry.");
        }

        var vendorEntry = entries.Single(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Vendor);
        var platformEntry = entries.Single(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Platform);

        if (vendorEntry.EntityId != paymentRequest.VendorId ||
            vendorEntry.TransactionType != CommerceHubFinancialTransactionType.Settlement ||
            platformEntry.TransactionType != CommerceHubFinancialTransactionType.Settlement ||
            vendorEntry.Direction != CommerceHubFinancialTransactionDirection.Credit ||
            platformEntry.Direction != CommerceHubFinancialTransactionDirection.Credit ||
            vendorEntry.ReferenceType != CommerceHubFinancialReferenceType.Settlement ||
            platformEntry.ReferenceType != CommerceHubFinancialReferenceType.Settlement ||
            vendorEntry.ReferenceId != paymentRequest.Id ||
            platformEntry.ReferenceId != paymentRequest.Id)
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                paymentRequest.Id,
                "Approved payment request ledger rows do not match the expected settlement posting pattern.");
        }

        if (vendorEntry.Amount != paymentRequest.Amount ||
            platformEntry.Amount != paymentRequest.Amount ||
            !string.Equals(vendorEntry.Currency, paymentRequest.Currency, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(platformEntry.Currency, paymentRequest.Currency, StringComparison.OrdinalIgnoreCase))
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                paymentRequest.Id,
                "Approved payment request ledger rows do not mirror the approved amount and currency.");
        }

        if (vendorEntry.CreatedBy != approvedBy ||
            platformEntry.CreatedBy != approvedBy ||
            !string.Equals(vendorEntry.IdempotencyKey, vendorIdempotencyKey, StringComparison.Ordinal) ||
            !string.Equals(platformEntry.IdempotencyKey, platformIdempotencyKey, StringComparison.Ordinal))
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                paymentRequest.Id,
                "Approved payment request ledger rows do not share the expected traceability markers.");
        }

        var integrityNet = Math.Round(
            ComputeSettlementIntegrityContribution(vendorEntry) + ComputeSettlementIntegrityContribution(platformEntry),
            2,
            MidpointRounding.AwayFromZero);

        if (integrityNet != 0m)
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                paymentRequest.Id,
                "Approved payment request ledger rows are financially unbalanced.");
        }
    }

    private async Task EnsureApprovedPaymentIntegrityAsync(
        PaymentRequestProjection paymentRequest,
        CancellationToken cancellationToken)
    {
        var trackedRequest = await _dbContext.CommerceHubVendorPaymentRequests
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == paymentRequest.PaymentRequestId, cancellationToken);

        await EnsureApprovedPaymentIntegrityAsync(trackedRequest, cancellationToken);
    }

    private void ThrowFinancialIntegrityViolation(
        string correlationId,
        Guid paymentRequestId,
        string message)
    {
        _logger.LogCritical(
            "CommerceHub payment integrity violation | PaymentRequestId: {PaymentRequestId} | CorrelationId: {CorrelationId}",
            paymentRequestId,
            correlationId);

        throw new CommerceHubVendorFinancialControlException(
            "FINANCIAL_INTEGRITY_VIOLATION",
            message,
            StatusCodes.Status409Conflict);
    }

    private async Task DeleteObjectSafelyAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.DeleteObjectAsync(objectKey, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to delete vendor payment proof after persistence failure | ObjectKey: {ObjectKey}",
                objectKey);
        }
    }

    private static void EnsureVendorDebtAllowsSubmission(
        CommerceHubVendorDebtSnapshot debtSnapshot,
        decimal amount)
    {
        if (debtSnapshot.AmountOwedByVendor <= 0m)
        {
            throw new CommerceHubVendorFinancialControlException(
                "NO_VENDOR_DEBT",
                "Vendor has no outstanding debt to repay.",
                StatusCodes.Status409Conflict);
        }

        if (amount > debtSnapshot.AmountOwedByVendor)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_AMOUNT_EXCEEDS_DEBT",
                "Payment amount cannot exceed the vendor's current debt.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void EnsureApprovalAmountIsValid(
        CommerceHubVendorPaymentRequest paymentRequest,
        CommerceHubVendorDebtSnapshot debtSnapshot,
        bool allowOverpayment)
    {
        if (debtSnapshot.AmountOwedByVendor <= 0m)
        {
            throw new CommerceHubVendorFinancialControlException(
                "NO_VENDOR_DEBT",
                "Vendor has no outstanding debt to settle.",
                StatusCodes.Status409Conflict);
        }

        if (!allowOverpayment && paymentRequest.Amount > debtSnapshot.AmountOwedByVendor)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_AMOUNT_EXCEEDS_DEBT",
                "Approved amount exceeds the vendor's outstanding debt. Enable overpayment explicitly to continue.",
                StatusCodes.Status409Conflict);
        }
    }

    private static void ValidateVendorSubmissionReplay(
        CommerceHubVendorPaymentRequest existingRequest,
        Guid paymentMethodId,
        decimal amount,
        string currency,
        string? transferReference,
        ProofFileValidation proofFile)
    {
        if (existingRequest.PaymentMethodId != paymentMethodId ||
            existingRequest.Amount != amount ||
            !string.Equals(existingRequest.Currency, currency, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existingRequest.TransferReference, transferReference, StringComparison.Ordinal))
        {
            throw new CommerceHubVendorFinancialControlException(
                "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD",
                "This idempotency key was already used with a different vendor payment request.",
                StatusCodes.Status409Conflict);
        }

        var proof = existingRequest.Proof
            ?? throw new CommerceHubVendorFinancialControlException(
                "FINANCIAL_INTEGRITY_VIOLATION",
                "Vendor payment request is missing its proof record.",
                StatusCodes.Status409Conflict);

        // Skip ContentHash validation for legacy records (migrated before hash tracking)
        var isLegacyRecord = CommerceHubVendorPaymentProof.IsLegacyContentHash(proof.ContentHash);

        if (!string.Equals(proof.ContentType, proofFile.ContentType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(proof.OriginalFileName, proofFile.FileName, StringComparison.Ordinal) ||
            proof.SizeBytes != proofFile.SizeBytes ||
            (!isLegacyRecord && !string.Equals(proof.ContentHash, proofFile.ContentHash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CommerceHubVendorFinancialControlException(
                "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD",
                "This idempotency key was already used with a different proof upload.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task<ProofFileValidation> ValidateProofFileAsync(
        Microsoft.AspNetCore.Http.IFormFile proofFile,
        CancellationToken cancellationToken)
    {
        if (proofFile.Length <= 0)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_PROOF_EMPTY",
                "Payment proof file is empty.",
                StatusCodes.Status400BadRequest);
        }

        await using var stream = proofFile.OpenReadStream();
        var validationResult = await _fileValidationService.ValidateFileAsync(
            stream,
            proofFile.FileName,
            proofFile.ContentType,
            FileValidationProfiles.VendorPaymentProof,
            cancellationToken);

        if (!validationResult.IsValid ||
            string.IsNullOrWhiteSpace(validationResult.CanonicalContentType) ||
            string.IsNullOrWhiteSpace(validationResult.SanitizedFileName))
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_PROOF_TYPE_NOT_ALLOWED",
                "Supported payment proof files are JPG, PNG, WEBP, and MP4.",
                StatusCodes.Status400BadRequest);
        }

        var rule = ResolveProofFileRule(validationResult.CanonicalContentType);
        if (proofFile.Length > rule.MaxBytes)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_PROOF_TOO_LARGE",
                rule.MediaType == "video"
                    ? "Payment proof videos must be 50MB or smaller."
                    : "Payment proof images must be 10MB or smaller.",
                StatusCodes.Status400BadRequest);
        }

        // Compute SHA-256 hash of file content
        stream.Position = 0;
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        var contentHash = Convert.ToHexString(hashBytes);

        return new ProofFileValidation(
            validationResult.SanitizedFileName,
            validationResult.CanonicalContentType,
            proofFile.Length,
            contentHash);
    }

    private static ProofFileRule ResolveProofFileRule(string contentType)
    {
        if (ProofFileRules.TryGetValue(contentType, out var rule))
        {
            return rule;
        }

        throw new CommerceHubVendorFinancialControlException(
            "PAYMENT_PROOF_TYPE_NOT_ALLOWED",
            "Supported payment proof files are JPG, PNG, WEBP, and MP4.",
            StatusCodes.Status400BadRequest);
    }

    private static string BuildProofUploadPath(Guid vendorId, Guid paymentRequestId) =>
        $"commercehub/vendor-payments/{vendorId:N}/{paymentRequestId:N}";

    private static string BuildProofUploadPathForAttempt(Guid vendorId, Guid paymentRequestId, Guid attemptId) =>
        $"commercehub/vendor-payments/{vendorId:N}/{paymentRequestId:N}/{attemptId:N}";

    private static Guid BuildDeterministicPaymentRequestId(Guid vendorId, string idempotencyKey)
    {
        var raw = $"commercehub-vendor-payment-request:{vendorId:N}:{idempotencyKey.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static string BuildApprovalIdempotencyKey(Guid paymentRequestId) =>
        $"commercehub:vendor-payment:approval:{paymentRequestId:N}";

    private static string BuildPaymentCorrelationId(Guid paymentRequestId) =>
        paymentRequestId.ToString("D");

    private static string BuildScopedIdempotencyKey(string baseIdempotencyKey, string scope)
    {
        var scopedIdempotencyKey = $"{baseIdempotencyKey}:{scope}";
        if (scopedIdempotencyKey.Length <= MaxTraceKeyLength)
        {
            return scopedIdempotencyKey;
        }

        using var sha256 = SHA256.Create();
        var hash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(scopedIdempotencyKey)))
            .ToLowerInvariant();
        var prefixBudget = MaxTraceKeyLength - scope.Length - hash.Length - 2;
        var prefix = prefixBudget > 0
            ? baseIdempotencyKey[..Math.Min(prefixBudget, baseIdempotencyKey.Length)]
            : string.Empty;

        return $"{prefix}:{scope}:{hash}".TrimStart(':');
    }

    private static decimal ComputeSettlementIntegrityContribution(CommerceHubFinancialTransaction transaction)
    {
        return transaction.EntityType switch
        {
            CommerceHubFinancialEntityType.Vendor => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? -transaction.Amount
                : transaction.Amount,
            CommerceHubFinancialEntityType.Platform => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? transaction.Amount
                : -transaction.Amount,
            _ => 0m
        };
    }

    private static string BuildVendorApprovalDescription(string vendorName, Guid paymentRequestId, string reason) =>
        $"Vendor {vendorName} debt repayment approved from manual payment request {paymentRequestId}. Reason: {reason}.";

    private static string BuildPlatformApprovalDescription(string vendorName, Guid paymentRequestId, string reason) =>
        $"Platform cash inflow received from vendor {vendorName} via approved manual repayment {paymentRequestId}. Reason: {reason}.";

    private static Guid NormalizeActorId(Guid? actorId)
    {
        if (!actorId.HasValue || actorId.Value == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "SUPER_ADMIN_ACTOR_REQUIRED",
                "Authenticated Super Admin actor is required for traceable CommerceHub payment approvals.",
                StatusCodes.Status401Unauthorized);
        }

        return actorId.Value;
    }

    private static decimal NormalizeAmount(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new CommerceHubVendorFinancialControlException(
                "AMOUNT_INVALID",
                "Amount must be greater than zero.",
                StatusCodes.Status400BadRequest);
        }

        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private string ValidateRequestCurrency(string? currency)
    {
        try
        {
            return FinancialCurrencyGuard.RequireEgpCurrency(
                currency,
                "vendor payment request currency",
                "CommerceHubVendorPaymentsService");
        }
        catch (FinancialCurrencyMissingException exception)
        {
            _logger.LogWarning(
                exception,
                "Vendor payment request currency was missing.");

            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_CURRENCY_REQUIRED",
                "Payment currency is required.",
                StatusCodes.Status400BadRequest);
        }
        catch (FinancialCurrencyInvalidException exception)
        {
            _logger.LogWarning(
                exception,
                "Vendor payment request currency was invalid: {Currency}",
                currency);

            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_CURRENCY_INVALID",
                "Payment currency is invalid.",
                StatusCodes.Status400BadRequest);
        }
        catch (FinancialCurrencyMismatchException exception)
        {
            _logger.LogWarning(
                exception,
                "Vendor payment request currency is not supported: {Currency}",
                currency);

            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_CURRENCY_NOT_SUPPORTED",
                "Only EGP is supported for CommerceHub vendor repayments.",
                StatusCodes.Status400BadRequest);
        }
    }

    private string NormalizeCurrency(string? currency)
    {
        try
        {
            return FinancialCurrencyGuard.RequireEgpCurrency(
                currency,
                "vendor repayment currency",
                "CommerceHubVendorPaymentsService");
        }
        catch (FinancialCurrencyMissingException exception)
        {
            _logger.LogError(
                exception,
                "Vendor payment currency was missing during normalization.");

            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_CURRENCY_REQUIRED",
                "Payment currency is required.",
                StatusCodes.Status500InternalServerError);
        }
        catch (FinancialCurrencyInvalidException exception)
        {
            _logger.LogError(
                exception,
                "Vendor payment currency was invalid during normalization.");

            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_CURRENCY_INVALID",
                "Payment currency is invalid.",
                StatusCodes.Status500InternalServerError);
        }
        catch (FinancialCurrencyMismatchException exception)
        {
            _logger.LogError(
                exception,
                "Vendor payment currency is not supported during normalization.");

            throw new CommerceHubVendorFinancialControlException(
                "PAYMENT_CURRENCY_NOT_SUPPORTED",
                "Only EGP is supported for CommerceHub vendor repayments.",
                StatusCodes.Status409Conflict);
        }
    }

    private static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CommerceHubVendorFinancialControlException(
                "IDEMPOTENCY_KEY_REQUIRED",
                "IdempotencyKey is required.",
                StatusCodes.Status400BadRequest);
        }

        var normalizedIdempotencyKey = idempotencyKey.Trim();
        if (normalizedIdempotencyKey.Length > MaxTraceKeyLength)
        {
            throw new CommerceHubVendorFinancialControlException(
                "IDEMPOTENCY_KEY_INVALID",
                $"IdempotencyKey must not exceed {MaxTraceKeyLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalizedIdempotencyKey;
    }

    private static string NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new CommerceHubVendorFinancialControlException(
                "REASON_REQUIRED",
                "Reason is required.",
                StatusCodes.Status400BadRequest);
        }

        return reason.Trim();
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Length > maxLength)
        {
            throw new CommerceHubVendorFinancialControlException(
                "FIELD_TOO_LONG",
                $"Value must not exceed {maxLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalizedValue;
    }

    private static string NormalizeContentType(string? contentType) =>
        (contentType ?? string.Empty)
            .Split(';', 2)[0]
            .Trim()
            .ToLowerInvariant();

    private static string SanitizeFileName(string? fileName, string extension)
    {
        var candidate = string.IsNullOrWhiteSpace(fileName)
            ? $"payment-proof{extension}"
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
            sanitized = $"payment-proof{extension}";
        }

        if (!sanitized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            sanitized = $"{Path.GetFileNameWithoutExtension(sanitized)}{extension}";
        }

        return sanitized.Length <= 255 ? sanitized : sanitized[..255];
    }

    private static bool IsLedgerDuplicateException(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               (postgresException.ConstraintName?.StartsWith("UX_CommerceHubFinancialTransactions_", StringComparison.Ordinal) ?? false);
    }

    private static bool IsVendorPaymentRequestDuplicateException(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               string.Equals(
                   postgresException.ConstraintName,
                   "UX_CommerceHubVendorPaymentRequests_VendorId_IdempotencyKey",
                   StringComparison.Ordinal);
    }

    private static bool IsVendorPaymentEvidenceUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                   (string.Equals(
                        postgresException.ConstraintName,
                        VendorPaymentLiveTransferReferenceUnique,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        postgresException.ConstraintName,
                        VendorPaymentProofContentHashUnique,
                        StringComparison.Ordinal));
        }

        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains(VendorPaymentLiveTransferReferenceUnique, StringComparison.Ordinal) ||
               message.Contains(VendorPaymentProofContentHashUnique, StringComparison.Ordinal);
    }

    private static bool IsVendorPaymentMethodDuplicateException(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               string.Equals(
                   postgresException.ConstraintName,
                   "UX_CommerceHubVendorPaymentMethods_Name_DestinationValue",
                   StringComparison.Ordinal);
    }

    private static CommerceHubVendorPaymentHistoryItemDto MapVendorHistoryItem(
        CommerceHubVendorPaymentRequest paymentRequest,
        CommerceHubVendorPaymentProof? proof)
    {
        return new CommerceHubVendorPaymentHistoryItemDto
        {
            PaymentRequestId = paymentRequest.Id,
            PaymentMethodId = paymentRequest.PaymentMethodId,
            Amount = paymentRequest.Amount,
            Currency = paymentRequest.Currency,
            Status = paymentRequest.Status.ToString(),
            TransferReference = paymentRequest.TransferReference,
            MethodName = paymentRequest.MethodNameSnapshot,
            DestinationValue = paymentRequest.DestinationValueSnapshot,
            Instructions = paymentRequest.InstructionsSnapshot,
            ReviewReason = paymentRequest.ReviewReason,
            CreatedAtUtc = paymentRequest.CreatedAtUtc,
            ApprovedAtUtc = paymentRequest.ApprovedAtUtc,
            RejectedAtUtc = paymentRequest.RejectedAtUtc,
            HasProof = proof != null,
            ProofContentType = proof?.ContentType,
            ProofFileName = proof?.OriginalFileName,
            ProofSizeBytes = proof?.SizeBytes
        };
    }

    private static SuperAdminVendorPaymentRequestItemDto MapSuperAdminPaymentRequestItem(PaymentRequestProjection row)
    {
        return new SuperAdminVendorPaymentRequestItemDto
        {
            PaymentRequestId = row.PaymentRequestId,
            VendorId = row.VendorId,
            VendorName = ResolveVendorDisplayName(row.VendorName, row.VendorSlug, row.VendorEmail, row.VendorId),
            PaymentMethodId = row.PaymentMethodId,
            MethodName = row.MethodName,
            DestinationValue = row.DestinationValue,
            Instructions = row.Instructions,
            Amount = row.Amount,
            Currency = row.Currency,
            Status = row.Status.ToString(),
            TransferReference = row.TransferReference,
            CreatedAtUtc = row.CreatedAtUtc,
            ApprovedAtUtc = row.ApprovedAtUtc,
            ApprovedBy = row.ApprovedBy,
            RejectedAtUtc = row.RejectedAtUtc,
            RejectedBy = row.RejectedBy,
            ReviewReason = row.ReviewReason,
            HasProof = row.Proof != null,
            ProofContentType = row.Proof?.ContentType,
            ProofFileName = row.Proof?.FileName,
            ProofSizeBytes = row.Proof?.SizeBytes
        };
    }

    private static string ResolveVendorDisplayName(
        string? vendorName,
        string? vendorSlug,
        string? vendorEmail,
        Guid vendorId)
    {
        if (!string.IsNullOrWhiteSpace(vendorName))
        {
            return vendorName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(vendorSlug))
        {
            return vendorSlug.Trim();
        }

        if (!string.IsNullOrWhiteSpace(vendorEmail))
        {
            return vendorEmail.Trim();
        }

        return vendorId.ToString();
    }

    private sealed record VendorLookup(Guid Id, string Name, string? Currency);
    private sealed record ProofFileRule(string MediaType, string Extension, long MaxBytes);
    private sealed record ProofFileValidation(string FileName, string ContentType, long SizeBytes, string ContentHash);

    private sealed class PaymentRequestProjection
    {
        public Guid PaymentRequestId { get; init; }
        public Guid VendorId { get; init; }
        public string? VendorName { get; init; }
        public string? VendorSlug { get; init; }
        public string? VendorEmail { get; init; }
        public Guid PaymentMethodId { get; init; }
        public string MethodName { get; init; } = string.Empty;
        public string DestinationValue { get; init; } = string.Empty;
        public string Instructions { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string Currency { get; init; } = string.Empty;
        public CommerceHubVendorPaymentRequestStatus Status { get; init; }
        public string? TransferReference { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? ApprovedAtUtc { get; init; }
        public Guid? ApprovedBy { get; init; }
        public DateTime? RejectedAtUtc { get; init; }
        public Guid? RejectedBy { get; init; }
        public string? ReviewReason { get; init; }
        public string? LedgerCorrelationId { get; init; }
        public string? LedgerIdempotencyKey { get; init; }
        public PaymentProofProjection? Proof { get; init; }
    }

    private sealed class PaymentProofProjection
    {
        public string ContentType { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
    }
}
