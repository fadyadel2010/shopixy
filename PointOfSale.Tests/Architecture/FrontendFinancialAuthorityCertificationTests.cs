namespace PointOfSale.Tests.Architecture;

public sealed class FrontendFinancialAuthorityCertificationTests
{
    [Fact]
    public void SuperAdminCommerceHubFinancialActions_MustRemainEgpAuthoritative()
    {
        var workspace = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/super-admin/finance/VendorFinanceWorkspace.tsx");
        var authorityService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");

        Assert.Equal(2, CountOccurrences(workspace, "currency: 'EGP'"));
        Assert.DoesNotContain("currency: adjustmentDraft.currency", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("currency: settlementDraft.currency", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("currency: current.currency || selectedVendor.currency", workspace, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(workspace, "value=\"EGP\" readOnly"));

        Assert.Contains(
            "if (!string.Equals(normalizedCurrency, DefaultCurrency, StringComparison.Ordinal))",
            authorityService,
            StringComparison.Ordinal);
        Assert.Contains("CURRENCY_NOT_SUPPORTED", authorityService, StringComparison.Ordinal);
        Assert.Contains("CommerceHub financial actions support EGP only.", authorityService, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateFinancialSurfaces_MustNotCollapseOrMislabelLedgerCurrencies()
    {
        var portal = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx");
        var dashboard = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/portal/DashboardTab.jsx");
        var earnings = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/portal/EarningsTab.jsx");
        var payouts = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/portal/PayoutsTab.jsx");
        var orders = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/portal/OrdersTab.jsx");
        var landingPages = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/portal/LandingPagesTab.jsx");
        var adminDetail = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/AdminAffiliateDetail.jsx");
        var adminList = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/AdminAffiliatesList.jsx");
        var adminController = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/AdminAffiliateController.cs");

        Assert.Contains("const formatCurrency = (val, currency)", portal, StringComparison.Ordinal);
        Assert.DoesNotContain("style: 'currency', currency: 'EGP'", portal, StringComparison.Ordinal);
        Assert.Contains("data?.earningsByCurrency", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("data?.totalEarned", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("data?.totalApproved", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("data?.totalPaid", dashboard, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(item.value, summary.currency)", earnings, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(summary.totalReversed, summary.currency)", earnings, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(card.value, bal.currency)", payouts, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(req.requestedAmount, req.currency)", payouts, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(order.totalCommission, order.currency)", orders, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(lp.estimatedCommission, lp.currency)", landingPages, StringComparison.Ordinal);

        Assert.Contains("EarningsByCurrency = dash?.EarningsByCurrency", adminController, StringComparison.Ordinal);
        Assert.Contains("public List<AffiliateEarningsSummaryDto> EarningsByCurrency", adminController, StringComparison.Ordinal);
        Assert.Contains("formatEarningsByCurrency(affiliate, 'totalEarned', formatCurrency)", adminList, StringComparison.Ordinal);
        Assert.Contains("formatEarningsByCurrency(detail, 'totalEarned', formatCurrency)", adminDetail, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(order.totalCommission, order.currency)", adminDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("formatCurrency(order.totalCommission)", adminDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("formatCurrency(affiliate.totalEarned)", adminList, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCustomerFinancialCaches_MustFollowCurrentSessionAuthority()
    {
        var bootstrap = CertificationSource.ReadRepoFile(
            "PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx");
        var header = CertificationSource.ReadRepoFile(
            "PointOfSale.CommerceHub/src/components/layout/Header.tsx");
        var sessionAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.CommerceHub/src/auth/customer-session-authority.ts");

        Assert.Contains("CUSTOMER_SESSION_REVALIDATION_INTERVAL_MS = 30_000", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.setInterval(() => void revalidateSession()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"focus\", revalidateSession)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener(\"visibilitychange\", handleVisibility)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", bootstrap, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_SESSION_LOGOUT_STORAGE_KEY", bootstrap, StringComparison.Ordinal);
        Assert.Contains("broadcastCustomerLogout();", header, StringComparison.Ordinal);
        Assert.Contains("commercehub_customer_logout_at", sessionAuthority, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
