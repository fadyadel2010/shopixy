using PointOfSale.Infrastructure.Services.Affiliate;

namespace PointOfSale.Tests.Affiliate;

public sealed class AffiliateSettlementProductionGuardTests
{
    [Fact]
    public void IsProductionSafe_DefaultSettlementOptions_AllowsProductionStartup()
    {
        var options = new AffiliateSettlementOptions
        {
            ExternalEvidenceHmacSecret = new string('x', 32)
        };

        Assert.True(AffiliateSettlementOptionsProductionGuard.IsProductionSafe(options));
        Assert.Empty(AffiliateSettlementOptionsProductionGuard.GetViolations(options));
    }

    [Fact]
    public void IsProductionSafe_BreakGlassOrUnverifiedEvidence_BlocksProductionStartup()
    {
        var options = new AffiliateSettlementOptions
        {
            RequireVerifiedExternalPayoutEvidence = false,
            AllowManualPayoutEvidenceOverride = true,
            RequireVerifiedManualDeliveryEvidence = false,
            AllowManualDeliveryEvidenceOverride = true,
            ExternalEvidenceHmacSecret = "short"
        };

        var violations = AffiliateSettlementOptionsProductionGuard.GetViolations(options);

        Assert.False(AffiliateSettlementOptionsProductionGuard.IsProductionSafe(options));
        Assert.Contains(violations, violation => violation.Contains(nameof(AffiliateSettlementOptions.RequireVerifiedExternalPayoutEvidence), StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains(nameof(AffiliateSettlementOptions.AllowManualPayoutEvidenceOverride), StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains(nameof(AffiliateSettlementOptions.RequireVerifiedManualDeliveryEvidence), StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains(nameof(AffiliateSettlementOptions.AllowManualDeliveryEvidenceOverride), StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains(nameof(AffiliateSettlementOptions.ExternalEvidenceHmacSecret), StringComparison.Ordinal));
    }
}
