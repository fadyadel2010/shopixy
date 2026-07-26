namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS AF-PA6.1 - Withdrawal Request creation and admission authority freeze.
/// </summary>
public sealed class AffiliateWithdrawalRequestAuthorityCertificationTests
{
    [Fact]
    public void WithdrawalAdmission_MustBeBackendBalanceAuthorityOnly()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePortalController.cs");
        var requestMethod = Slice(service, "private async Task<AffiliatePayoutRequest> RequestPayoutCoreAsync", "private async Task<AffiliatePayoutRequest?> FindActiveDuplicatePayoutRequestAsync");

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", requestMethod, StringComparison.Ordinal);
        Assert.Contains("AcquireAffiliatePayoutBalanceLockAsync", requestMethod, StringComparison.Ordinal);
        Assert.Contains("GetAvailableBalanceAsync(affiliateAccountId, storeTenantId, ct)", requestMethod, StringComparison.Ordinal);
        Assert.Contains("availableForWithdrawal: balance.RawAvailable", requestMethod, StringComparison.Ordinal);
        Assert.Contains("AffiliatePayoutRequest.Create", requestMethod, StringComparison.Ordinal);

        Assert.DoesNotContain("body.Amount > balance.RawAvailable", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAvailableBalanceAsync(affiliateAccountId.Value", Slice(controller, "public async Task<ActionResult<AffiliatePayoutRequestDto>> RequestPayout", "catch (AffiliatePayoutAuthorityException ex)"), StringComparison.Ordinal);
        Assert.DoesNotContain("AvailableForWithdrawal = body", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateReplay_MustNotCreateSecondActiveReservation()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");
        var duplicateMethod = Slice(service, "private async Task<AffiliatePayoutRequest?> FindActiveDuplicatePayoutRequestAsync", "private async Task<Guid> ResolvePayoutStoreTenantIdAsync");

        Assert.Contains("FindActiveDuplicatePayoutRequestAsync", service, StringComparison.Ordinal);
        Assert.Contains("return activeDuplicate;", service, StringComparison.Ordinal);
        Assert.Contains("request.AffiliateAccountId == affiliateAccountId", duplicateMethod, StringComparison.Ordinal);
        Assert.Contains("request.StoreTenantId == storeTenantId", duplicateMethod, StringComparison.Ordinal);
        Assert.Contains("request.RequestedAmount == amount", duplicateMethod, StringComparison.Ordinal);
        Assert.Contains("request.Status == PayoutRequestStatus.Pending", duplicateMethod, StringComparison.Ordinal);
        Assert.Contains("request.Status == PayoutRequestStatus.Approved", duplicateMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PayoutRequestStatus.Rejected", duplicateMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PayoutRequestStatus.Paid", duplicateMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void WalletAdmission_MustValidateAndSnapshotBackendWallet()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs");
        var account = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateAccount.cs");
        var walletMethod = Slice(service, "private static CurrentPayoutWalletSnapshot RequireCurrentPayoutWallet", "private sealed record CurrentPayoutWalletSnapshot");

        Assert.Contains("PayoutWallet.Create(account.PayoutWalletNumber, account.PayoutWalletType)", walletMethod, StringComparison.Ordinal);
        Assert.Contains("payoutWalletNumber: payoutWallet.Number", service, StringComparison.Ordinal);
        Assert.Contains("payoutWalletType: payoutWallet.Type", service, StringComparison.Ordinal);
        Assert.Contains("PayoutWalletNumberSnapshot", entity, StringComparison.Ordinal);
        Assert.Contains("PayoutWalletTypeSnapshot", entity, StringComparison.Ordinal);
        Assert.Contains("EnsurePayoutWalletSnapshotMatches", entity, StringComparison.Ordinal);
        Assert.Contains("var wallet = PayoutWallet.Create(walletNumber, walletType);", account, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendWithdrawalRequest_MustSubmitAmountIntentOnly()
    {
        var api = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/affiliateApi.js");
        var payoutApi = Slice(api, "getPayouts: (page = 1, pageSize = 20) =>", "export default affiliateApi");
        var tab = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/portal/PayoutsTab.jsx");
        var apiTest = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/affiliateApi.test.ts");

        Assert.Contains("affiliateAxios.post('/affiliate/payouts/request', { amount }, affiliateAuthorityReadConfig)", payoutApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/payouts', { ...affiliateAuthorityReadConfig", payoutApi, StringComparison.Ordinal);
        Assert.Contains("await affiliateApi.requestPayout(numAmount)", tab, StringComparison.Ordinal);
        Assert.Contains("{ amount: 125 }", apiTest, StringComparison.Ordinal);
        Assert.DoesNotContain("availableForWithdrawal:", payoutApi, StringComparison.Ordinal);
        Assert.DoesNotContain("affiliateAccountId:", payoutApi, StringComparison.Ordinal);
        Assert.DoesNotContain("tenantId:", payoutApi, StringComparison.Ordinal);
        Assert.DoesNotContain("walletNumber:", payoutApi, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPayoutApproval_MustBeBackendSnapshotAuthorityAndAuditable()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAffiliateController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs");
        var api = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminAffiliateApi.js");

        Assert.Contains("TryGetCurrentAdminUserId(out var adminUserId)", controller, StringComparison.Ordinal);
        Assert.Contains("id, TenantId, adminUserId, body?.AdminNotes, ct", controller, StringComparison.Ordinal);
        Assert.Contains("Guid approvedByUserId", service, StringComparison.Ordinal);
        Assert.Contains("Guid rejectedByUserId", service, StringComparison.Ordinal);
        Assert.Contains("ExecuteWithRowLockAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsurePayoutWalletSnapshotMatches", service, StringComparison.Ordinal);
        Assert.Contains("r.Approve(approvedByUserId, notes)", service, StringComparison.Ordinal);
        Assert.Contains("r.Reject(rejectedByUserId, notes)", service, StringComparison.Ordinal);
        Assert.Contains("public Guid? ApprovedBy", entity, StringComparison.Ordinal);
        Assert.Contains("public Guid? RejectedBy", entity, StringComparison.Ordinal);
        Assert.Contains("ApprovedAmount = RequestedAmount", entity, StringComparison.Ordinal);
        Assert.Contains("Status != PayoutRequestStatus.Pending", entity, StringComparison.Ordinal);

        Assert.Contains("api.post(`/admin/affiliates/payouts/${requestId}/approve`, { adminNotes })", api, StringComparison.Ordinal);
        Assert.Contains("api.post(`/admin/affiliates/payouts/${requestId}/reject`, { adminNotes })", api, StringComparison.Ordinal);
        Assert.DoesNotContain("approvedAmount", Slice(api, "approvePayoutRequest:", "rejectPayoutRequest:"), StringComparison.Ordinal);
        Assert.DoesNotContain("wallet", Slice(api, "approvePayoutRequest:", "rejectPayoutRequest:"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("affiliateAccountId", Slice(api, "approvePayoutRequest:", "rejectPayoutRequest:"), StringComparison.Ordinal);
        Assert.DoesNotContain("storeTenantId", Slice(api, "approvePayoutRequest:", "rejectPayoutRequest:"), StringComparison.Ordinal);
        Assert.DoesNotContain("currency", Slice(api, "approvePayoutRequest:", "rejectPayoutRequest:"), StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordPassAfPa61()
    {
        Assert.Contains("PASS AF-PA6.1", CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA6.1", CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA6.1", CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA6.1", CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA6.1", CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA6.1", CertificationSource.ReadRepoFile("docs/architecture/affiliate-withdrawal-request-authority-certification-findings.md"), StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
