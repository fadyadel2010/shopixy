using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubEarningsService
{
    Task<CommerceHubVendorEarningsResponseDto> GetVendorEarningsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);
}

public class CommerceHubEarningsService : ICommerceHubEarningsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubVendorPayoutReadService _payoutReadService;

    public CommerceHubEarningsService(
        ApplicationDbContext dbContext,
        ICommerceHubVendorPayoutReadService payoutReadService)
    {
        _dbContext = dbContext;
        _payoutReadService = payoutReadService;
    }

    public async Task<CommerceHubVendorEarningsResponseDto> GetVendorEarningsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var earnings = await _dbContext.CommerceHubVendorEarnings
            .AsNoTracking()
            .Include(earning => earning.SubOrder)
            .Where(earning =>
                earning.TenantId == storeId &&
                !earning.IsDeleted &&
                !earning.SubOrder.IsDeleted &&
                !earning.SubOrder.MarketplaceOrder.IsDeleted)
            .OrderByDescending(earning => earning.CreatedAt)
            .ToListAsync(cancellationToken);

        var payoutSnapshot = await _payoutReadService.GetVendorSnapshotAsync(storeId, cancellationToken);
        var governanceProfile = await _dbContext.UserGovernanceProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.Role == UserGovernanceRole.Vendor &&
                profile.SubjectId == storeId)
            .OrderByDescending(profile => profile.UpdatedAtUtc ?? profile.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var payoutHistory = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Where(payout => payout.TenantId == storeId && !payout.IsDeleted)
            .OrderByDescending(payout => payout.ProcessedAtUtc ?? payout.CreatedAt)
            .Take(50)
            .Select(payout => new CommerceHubPayoutDto
            {
                PayoutId = payout.Id,
                TenantId = payout.TenantId,
                PayoutMethodId = payout.PayoutMethodId,
                Amount = payout.Amount,
                Currency = payout.Currency,
                Status = payout.Status.ToString(),
                Method = payout.Method.ToString(),
                DestinationValueSnapshot = payout.DestinationValueSnapshot,
                Reference = payout.Reference,
                ExternalReferenceId = payout.ExternalReferenceId,
                BankTransactionId = payout.BankTransactionId,
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey,
                ProcessedBy = payout.ProcessedBy,
                FailureReason = payout.FailureReason,
                IdempotencyKey = payout.IdempotencyKey,
                CreatedAtUtc = payout.CreatedAt,
                ProcessedAtUtc = payout.ProcessedAtUtc,
                Items = payout.Items
                    .OrderBy(item => item.Earning.SubOrder.OrderNumber)
                    .Select(item => new CommerceHubPayoutItemDto
                    {
                        TenantId = payout.TenantId,
                        PayoutId = payout.Id,
                        PayoutItemId = item.Id,
                        EarningId = item.EarningId,
                        SubOrderId = item.Earning.SubOrderId,
                        OrderNumber = item.Earning.SubOrder.OrderNumber,
                        Amount = item.Amount
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var totalEarnings = earnings
            .Where(earning => earning.Amount > 0m)
            .Sum(earning => earning.Amount);

        var responseCurrency = RequireEgpCurrency(
            payoutSnapshot.Currency,
            "commercehub.vendor_earnings.snapshot.currency",
            $"vendorId:{storeId:D}");

        return new CommerceHubVendorEarningsResponseDto
        {
            VendorId = storeId,
            PendingTotal = earnings
                .Where(earning => earning.Status == CommerceHubVendorEarningStatus.Pending)
                .Sum(earning => earning.Amount),
            AvailableTotal = earnings
                .Where(earning => earning.Status == CommerceHubVendorEarningStatus.Available)
                .Sum(earning => earning.Amount),
            PaidOutTotal = earnings
                .Where(earning => earning.Status == CommerceHubVendorEarningStatus.PaidOut)
                .Sum(earning => earning.Amount),
            InPayoutTotal = earnings
                .Where(earning => earning.Status == CommerceHubVendorEarningStatus.InPayout)
                .Sum(earning => earning.Amount),
            ReversedTotal = earnings
                .Where(earning => earning.Status == CommerceHubVendorEarningStatus.Reversed)
                .Sum(earning => earning.Amount),
            TotalEarnings = totalEarnings,
            TotalPayable = payoutSnapshot.TotalPayable,
            EligiblePayoutAmount = payoutSnapshot.EligibleAmount,
            HoldbackAmount = payoutSnapshot.HoldbackAmount,
            CompletedPayoutsTotal = payoutSnapshot.CompletedPayoutsTotal,
            PendingPayoutsTotal = payoutSnapshot.PendingPayoutsTotal,
            Currency = responseCurrency,
            LastPayoutAtUtc = payoutSnapshot.LastPayoutAtUtc,
            Governance = MapGovernance(governanceProfile),
            RiskFlags = payoutSnapshot.RiskFlags.ToList(),
            PayoutHistory = payoutHistory,
            Items = earnings.Select(earning => new CommerceHubVendorEarningItemDto
            {
                TenantId = earning.TenantId,
                EarningId = earning.Id,
                SubOrderId = earning.SubOrderId,
                OriginalEarningId = earning.OriginalEarningId,
                RefundId = earning.RefundId,
                PayoutId = earning.PayoutId,
                OrderNumber = earning.SubOrder.OrderNumber,
                Amount = earning.Amount,
                Currency = RequireEgpCurrency(
                    earning.SubOrder.Currency,
                    "commercehub.vendor_earning_item.currency",
                    $"vendorId:{storeId:D};earningId:{earning.Id:D};subOrderId:{earning.SubOrderId:D}"),
                Status = earning.Status.ToString(),
                CreatedAt = earning.CreatedAt,
                AvailableAtUtc = earning.AvailableAtUtc,
                PaidOutAtUtc = earning.PaidOutAtUtc,
                ReversedAtUtc = earning.ReversedAtUtc
            }).ToList()
        };
    }

    private static string RequireEgpCurrency(string? currency, string component, string context) =>
        FinancialCurrencyGuard.RequireEgpCurrency(currency, component, context);

    private static CommerceHubVendorGovernanceStatusDto MapGovernance(UserGovernanceProfile? profile)
    {
        var status = profile?.Status ?? UserGovernanceStatus.Active;

        return status switch
        {
            UserGovernanceStatus.Suspended => new CommerceHubVendorGovernanceStatusDto
            {
                Status = status.ToString(),
                OperationalMode = "FulfillmentOnly",
                ProductsHidden = true,
                BlocksNewOrders = true,
                BlocksListingChanges = true,
                BlocksPayouts = true,
                BlocksLogin = false,
                Reason = profile?.StatusReason,
                BannerMessage = "Your account is suspended. Your products are hidden from customers."
            },
            UserGovernanceStatus.Banned => new CommerceHubVendorGovernanceStatusDto
            {
                Status = status.ToString(),
                OperationalMode = "Banned",
                ProductsHidden = true,
                BlocksNewOrders = true,
                BlocksListingChanges = true,
                BlocksPayouts = true,
                BlocksLogin = true,
                Reason = profile?.StatusReason,
                BannerMessage = "Your account is banned. CommerceHub access and financial activity are frozen."
            },
            UserGovernanceStatus.FinanciallyLocked => new CommerceHubVendorGovernanceStatusDto
            {
                Status = status.ToString(),
                OperationalMode = "FinancialReview",
                ProductsHidden = false,
                BlocksNewOrders = false,
                BlocksListingChanges = false,
                BlocksPayouts = true,
                BlocksLogin = false,
                Reason = profile?.StatusReason,
                BannerMessage = "Your account is under financial review. Payout actions are temporarily blocked."
            },
            _ => new CommerceHubVendorGovernanceStatusDto
            {
                Status = UserGovernanceStatus.Active.ToString(),
                OperationalMode = "Active",
                ProductsHidden = false,
                BlocksNewOrders = false,
                BlocksListingChanges = false,
                BlocksPayouts = false,
                BlocksLogin = false,
                Reason = profile?.StatusReason
            }
        };
    }
}
