using PointOfSale.Core.Entities.Affiliate;
using Xunit;

namespace PointOfSale.Tests.Core.Entities.Affiliate;

public class AffiliatePayoutRequestWalletSnapshotTests
{
    [Fact]
    public void Create_CapturesImmutablePayoutWalletSnapshot()
    {
        var walletUpdatedAt = DateTime.UtcNow.AddMinutes(-5);

        var request = AffiliatePayoutRequest.Create(
            storeTenantId: Guid.NewGuid(),
            affiliateAccountId: Guid.NewGuid(),
            requestedAmount: 150m,
            availableForWithdrawal: 200m,
            currency: "EGP",
            payoutWalletNumber: "01012345678",
            payoutWalletType: PayoutWalletTypes.VodafoneCash,
            payoutWalletUpdatedAtUtc: walletUpdatedAt);

        Assert.Equal("01012345678", request.PayoutWalletNumberSnapshot);
        Assert.Equal(PayoutWalletTypes.VodafoneCash, request.PayoutWalletTypeSnapshot);
        Assert.Equal(walletUpdatedAt, request.PayoutWalletUpdatedAtUtcSnapshot);
        Assert.NotNull(request.PayoutWalletSnapshotCapturedAtUtc);
    }

    [Fact]
    public void EnsurePayoutWalletSnapshotMatches_WhenWalletChanged_Throws()
    {
        var walletUpdatedAt = DateTime.UtcNow.AddMinutes(-5);
        var request = AffiliatePayoutRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            150m,
            200m,
            "EGP",
            "01012345678",
            PayoutWalletTypes.VodafoneCash,
            walletUpdatedAt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            request.EnsurePayoutWalletSnapshotMatches(
                "01098765432",
                PayoutWalletTypes.VodafoneCash,
                DateTime.UtcNow));

        Assert.Contains("AFFILIATE_PAYOUT_WALLET_CHANGED_AFTER_REQUEST", ex.Message);
    }

    [Fact]
    public void Create_WithoutPayoutWallet_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AffiliatePayoutRequest.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                150m,
                200m,
                "EGP",
                string.Empty,
                PayoutWalletTypes.VodafoneCash,
                null));

        Assert.Contains("AFFILIATE_PAYOUT_WALLET_REQUIRED", ex.Message);
    }

    [Fact]
    public void MarkPaid_ClosesOnlyApprovedRequestWithExternalReference()
    {
        var request = AffiliatePayoutRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            150m,
            200m,
            "EGP",
            "01012345678",
            PayoutWalletTypes.VodafoneCash,
            DateTime.UtcNow);
        request.Approve(Guid.NewGuid());
        var paidBy = Guid.NewGuid();

        request.MarkPaid(paidBy, "TRANSFER-123");

        Assert.Equal(PayoutRequestStatus.Paid, request.Status);
        Assert.Equal(0m, request.RemainingAmount);
        Assert.Equal("TRANSFER-123", request.PaymentReference);
        Assert.Equal(paidBy, request.PaidBy);
    }

    [Fact]
    public void ApproveAndReject_CaptureAdminAuditIdentity()
    {
        var approvedBy = Guid.NewGuid();
        var rejectedBy = Guid.NewGuid();
        var request = AffiliatePayoutRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            150m,
            200m,
            "EGP",
            "01012345678",
            PayoutWalletTypes.VodafoneCash,
            DateTime.UtcNow);

        request.Approve(approvedBy, "reviewed");
        request.Reject(rejectedBy, "wallet concern");

        Assert.Equal(approvedBy, request.ApprovedBy);
        Assert.Equal(rejectedBy, request.RejectedBy);
        Assert.NotNull(request.ApprovedAtUtc);
        Assert.NotNull(request.RejectedAtUtc);
    }
}
