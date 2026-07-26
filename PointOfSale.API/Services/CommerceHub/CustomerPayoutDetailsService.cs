using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICustomerPayoutDetailsService
{
    Task<IReadOnlyList<CustomerPayoutDetailsDto>> GetPayoutDetailsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerPayoutDetailsDto> CreatePayoutDetailsAsync(
        Guid customerId,
        CustomerUpsertPayoutDetailsRequestDto request,
        CancellationToken cancellationToken = default);

    Task<CustomerPayoutDetailsDto> UpdatePayoutDetailsAsync(
        Guid customerId,
        Guid payoutDetailsId,
        CustomerUpsertPayoutDetailsRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ResolvedRefundPayoutDetails> ResolveRefundPayoutAsync(
        Guid customerId,
        Guid? savedPayoutDetailsId,
        CustomerRefundPayoutInputDto? payoutDetails,
        CancellationToken cancellationToken = default);
}

public sealed class CustomerPayoutDetailsService : ICustomerPayoutDetailsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRefundPayoutSecurityService _securityService;

    public CustomerPayoutDetailsService(
        ApplicationDbContext dbContext,
        IRefundPayoutSecurityService securityService)
    {
        _dbContext = dbContext;
        _securityService = securityService;
    }

    public async Task<IReadOnlyList<CustomerPayoutDetailsDto>> GetPayoutDetailsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var payoutDetails = await _dbContext.CustomerPayoutDetails
            .AsNoTracking()
            .Where(item => item.CustomerId == customerId && !item.IsDeleted)
            .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

        return payoutDetails
            .Select(item => Map(item))
            .ToList();
    }

    public async Task<CustomerPayoutDetailsDto> CreatePayoutDetailsAsync(
        Guid customerId,
        CustomerUpsertPayoutDetailsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = await LoadCustomerAsync(customerId, cancellationToken);
        var normalized = RefundPayoutSupport.NormalizeRequired(
            request.Method,
            request.AccountIdentifier,
            request.AccountHolderName);

        var existingDetails = await _dbContext.CustomerPayoutDetails
            .Where(item => item.CustomerId == customerId && !item.IsDeleted)
            .ToListAsync(cancellationToken);

        var duplicate = FindDuplicate(existingDetails, normalized, excludeId: null);
        if (duplicate != null)
        {
            var holderChanged = !string.Equals(
                duplicate.AccountHolderName ?? string.Empty,
                normalized.AccountHolderName ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            if (holderChanged)
            {
                var beforeMasked = RefundPayoutSupport.MaskAccountIdentifier(
                    _securityService.Decrypt(duplicate.AccountIdentifier));
                var previousAccountHolderName = duplicate.AccountHolderName;

                duplicate.AccountHolderName = normalized.AccountHolderName;
                duplicate.UpdatedAt = DateTime.UtcNow;
                duplicate.UpdatedBy = customerId;

                _dbContext.AuditLogs.Add(BuildAuditLog(
                    customer,
                    duplicate.Id,
                    "PAYOUT_DETAILS_UPDATED",
                    "Customer payout details updated.",
                    new
                    {
                        method = duplicate.Method.ToString(),
                        maskedAccount = beforeMasked,
                        accountHolderName = previousAccountHolderName
                    },
                    new
                    {
                        method = duplicate.Method.ToString(),
                        maskedAccount = normalized.MaskedAccountIdentifier,
                        accountHolderName = normalized.AccountHolderName
                    }));

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new CustomerPayoutDetailsDto
            {
                Id = duplicate.Id,
                Method = duplicate.Method,
                MaskedAccountIdentifier = normalized.MaskedAccountIdentifier,
                AccountHolderName = duplicate.AccountHolderName,
                IsVerified = duplicate.IsVerified,
                CreatedAt = duplicate.CreatedAt,
                UpdatedAt = duplicate.UpdatedAt
            };
        }

        var payoutDetails = new CustomerPayoutDetails
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Customer = customer,
            Method = normalized.Method,
            AccountIdentifier = _securityService.Encrypt(normalized.AccountIdentifier),
            AccountHolderName = normalized.AccountHolderName,
            IsVerified = false,
            CreatedBy = customerId,
            UpdatedBy = customerId
        };

        _dbContext.CustomerPayoutDetails.Add(payoutDetails);
        _dbContext.AuditLogs.Add(BuildAuditLog(
            customer,
            payoutDetails.Id,
            "PAYOUT_DETAILS_ADDED",
            "Customer payout details added.",
            beforeData: null,
            afterData: new
            {
                method = payoutDetails.Method.ToString(),
                maskedAccount = normalized.MaskedAccountIdentifier,
                accountHolderName = payoutDetails.AccountHolderName
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(payoutDetails, normalized.MaskedAccountIdentifier);
    }

    public async Task<CustomerPayoutDetailsDto> UpdatePayoutDetailsAsync(
        Guid customerId,
        Guid payoutDetailsId,
        CustomerUpsertPayoutDetailsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = await LoadCustomerAsync(customerId, cancellationToken);
        var payoutDetails = await _dbContext.CustomerPayoutDetails
            .FirstOrDefaultAsync(
                item => item.Id == payoutDetailsId &&
                        item.CustomerId == customerId &&
                        !item.IsDeleted,
                cancellationToken);

        if (payoutDetails == null)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_DETAILS_NOT_FOUND",
                "Saved payout details were not found.",
                StatusCodes.Status404NotFound);
        }

        var currentAccount = _securityService.Decrypt(payoutDetails.AccountIdentifier);
        var targetMethod = request.Method ?? payoutDetails.Method;
        var targetAccount = string.IsNullOrWhiteSpace(request.AccountIdentifier)
            ? targetMethod == payoutDetails.Method
                ? currentAccount
                : null
            : request.AccountIdentifier;

        if (targetAccount == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_ACCOUNT_REQUIRED",
                "Please enter the payout account for the selected method.",
                StatusCodes.Status400BadRequest);
        }

        var normalized = RefundPayoutSupport.NormalizeRequired(
            targetMethod,
            targetAccount,
            request.AccountHolderName ?? payoutDetails.AccountHolderName);

        var duplicates = await _dbContext.CustomerPayoutDetails
            .Where(item =>
                item.CustomerId == customerId &&
                item.Id != payoutDetailsId &&
                !item.IsDeleted)
            .ToListAsync(cancellationToken);

        if (FindDuplicate(duplicates, normalized, payoutDetailsId) != null)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_DETAILS_DUPLICATE",
                "These payout details are already saved.",
                StatusCodes.Status409Conflict);
        }

        var beforeData = new
        {
            method = payoutDetails.Method.ToString(),
            maskedAccount = RefundPayoutSupport.MaskAccountIdentifier(currentAccount),
            accountHolderName = payoutDetails.AccountHolderName
        };

        payoutDetails.Method = normalized.Method;
        payoutDetails.AccountIdentifier = _securityService.Encrypt(normalized.AccountIdentifier);
        payoutDetails.AccountHolderName = normalized.AccountHolderName;
        payoutDetails.UpdatedAt = DateTime.UtcNow;
        payoutDetails.UpdatedBy = customerId;

        _dbContext.AuditLogs.Add(BuildAuditLog(
            customer,
            payoutDetails.Id,
            "PAYOUT_DETAILS_UPDATED",
            "Customer payout details updated.",
            beforeData,
            new
            {
                method = payoutDetails.Method.ToString(),
                maskedAccount = normalized.MaskedAccountIdentifier,
                accountHolderName = payoutDetails.AccountHolderName
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(payoutDetails, normalized.MaskedAccountIdentifier);
    }

    public async Task<ResolvedRefundPayoutDetails> ResolveRefundPayoutAsync(
        Guid customerId,
        Guid? savedPayoutDetailsId,
        CustomerRefundPayoutInputDto? payoutDetails,
        CancellationToken cancellationToken = default)
    {
        var hasSavedPayout = savedPayoutDetailsId.HasValue && savedPayoutDetailsId.Value != Guid.Empty;
        var hasInlinePayout = payoutDetails != null;

        if (hasSavedPayout == hasInlinePayout)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_SELECTION_INVALID",
                "Choose either a saved payout method or enter payout details for this refund.",
                StatusCodes.Status400BadRequest);
        }

        if (hasSavedPayout)
        {
            var savedPayout = await _dbContext.CustomerPayoutDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item =>
                        item.Id == savedPayoutDetailsId!.Value &&
                        item.CustomerId == customerId &&
                        !item.IsDeleted,
                    cancellationToken);

            if (savedPayout == null)
            {
                throw new CommerceHubCheckoutException(
                    "PAYOUT_DETAILS_NOT_FOUND",
                    "Saved payout details were not found.",
                    StatusCodes.Status404NotFound);
            }

            var normalizedSavedPayout = RefundPayoutSupport.NormalizeRequired(
                savedPayout.Method,
                _securityService.Decrypt(savedPayout.AccountIdentifier),
                savedPayout.AccountHolderName);

            return new ResolvedRefundPayoutDetails
            {
                SavedPayoutDetailsId = savedPayout.Id,
                Method = normalizedSavedPayout.Method,
                AccountIdentifier = normalizedSavedPayout.AccountIdentifier,
                EncryptedAccountIdentifier = _securityService.Encrypt(normalizedSavedPayout.AccountIdentifier),
                MaskedAccountIdentifier = normalizedSavedPayout.MaskedAccountIdentifier,
                AccountHolderName = normalizedSavedPayout.AccountHolderName,
                IsVerified = savedPayout.IsVerified
            };
        }

        var normalizedInlinePayout = RefundPayoutSupport.NormalizeRequired(
            payoutDetails!.Method,
            payoutDetails.AccountIdentifier,
            payoutDetails.AccountHolderName);

        return new ResolvedRefundPayoutDetails
        {
            Method = normalizedInlinePayout.Method,
            AccountIdentifier = normalizedInlinePayout.AccountIdentifier,
            EncryptedAccountIdentifier = _securityService.Encrypt(normalizedInlinePayout.AccountIdentifier),
            MaskedAccountIdentifier = normalizedInlinePayout.MaskedAccountIdentifier,
            AccountHolderName = normalizedInlinePayout.AccountHolderName,
            IsVerified = false
        };
    }

    private async Task<CommerceHubCustomer> LoadCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await _dbContext.CommerceHubCustomers
            .FirstOrDefaultAsync(item => item.Id == customerId && !item.IsDeleted, cancellationToken);

        return customer ?? throw new CommerceHubCheckoutException(
            "CUSTOMER_NOT_FOUND",
            "Customer account was not found.",
            StatusCodes.Status404NotFound);
    }

    private CustomerPayoutDetails? FindDuplicate(
        IReadOnlyCollection<CustomerPayoutDetails> items,
        NormalizedRefundPayoutInput target,
        Guid? excludeId)
    {
        return items.FirstOrDefault(item =>
            item.Id != excludeId &&
            item.Method == target.Method &&
            string.Equals(
                _securityService.Decrypt(item.AccountIdentifier),
                target.AccountIdentifier,
                StringComparison.Ordinal));
    }

    private CustomerPayoutDetailsDto Map(CustomerPayoutDetails payoutDetails, string? maskedOverride = null)
    {
        var maskedAccount = maskedOverride;
        if (string.IsNullOrWhiteSpace(maskedAccount))
        {
            maskedAccount = RefundPayoutSupport.MaskAccountIdentifier(
                _securityService.Decrypt(payoutDetails.AccountIdentifier));
        }

        return new CustomerPayoutDetailsDto
        {
            Id = payoutDetails.Id,
            Method = payoutDetails.Method,
            MaskedAccountIdentifier = maskedAccount!,
            AccountHolderName = payoutDetails.AccountHolderName,
            IsVerified = payoutDetails.IsVerified,
            CreatedAt = payoutDetails.CreatedAt,
            UpdatedAt = payoutDetails.UpdatedAt
        };
    }

    private static AuditLog BuildAuditLog(
        CommerceHubCustomer customer,
        Guid payoutDetailsId,
        string actionType,
        string description,
        object? beforeData,
        object? afterData)
    {
        return new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserId = customer.Id,
            UserNameSnapshot = customer.Email.Length > 150
                ? customer.Email[..150]
                : customer.Email,
            ActionType = actionType,
            EntityType = "CustomerPayoutDetails",
            EntityId = payoutDetailsId.ToString(),
            Description = description,
            BeforeDataJson = beforeData == null ? null : JsonSerializer.Serialize(beforeData),
            AfterDataJson = afterData == null ? null : JsonSerializer.Serialize(afterData),
            Source = "CommerceHubCustomer",
            SuccessFlag = true
        };
    }
}
