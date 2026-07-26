namespace PointOfSale.Tests.Architecture;

public sealed class AdminOrderPaymentAuthorityCertificationTests
{
    [Fact]
    public void AdminOrderPaymentPresentation_MustRemainBackendOwnedReadOnlyAndNoStore()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminOrdersController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var webPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var webService = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus = o.PaymentStatus", service, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus = subOrder.PaymentStatus", service, StringComparison.Ordinal);
        Assert.Contains("_context.Set<PaymentIntent>()", service, StringComparison.Ordinal);
        Assert.Contains("pi.OrderId == orderId && pi.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("PaymentReference = pi.PaymentReference", service, StringComparison.Ordinal);
        Assert.Contains("Amount = pi.Amount", service, StringComparison.Ordinal);
        Assert.Contains("Currency = pi.Currency", service, StringComparison.Ordinal);
        Assert.Contains("CapturedAt = pi.CapturedAt", service, StringComparison.Ordinal);
        Assert.Contains("public List<AdminPaymentIntentDto> Payments", dto, StringComparison.Ordinal);
        Assert.Contains("public PaymentStatus PaymentStatus", dto, StringComparison.Ordinal);
        Assert.Contains("StatusBadge value={order.paymentStatus}", webPage, StringComparison.Ordinal);
        Assert.Contains("order.payments?.length", webPage, StringComparison.Ordinal);
        Assert.Contains("payment.paymentReference", webPage, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(payment.amount, payment.currency)", webPage, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", webService, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminOrderPaymentRuntime_MustNotExposeFrontendPaymentVerificationOrAcceptedPaymentMutation()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminOrdersController.cs");
        var webPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var webService = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");

        Assert.DoesNotContain("verifyPayment", webPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verifyPayment", webService, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentVerification", webPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentVerification", webService, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/payments/", webService, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/payment", webService, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[HttpPost(\"{orderId:guid}/payment", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[HttpPut(\"{orderId:guid}/payment", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("setPaymentStatus", webPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", webPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", webPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm33AdminOrderPaymentAuthorityTruth()
    {
        var findings = ReadRepoFile("docs/architecture/admin-order-payment-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-ADM3.3 Admin Order Payment Authority Certification", findings, StringComparison.Ordinal);
        Assert.Contains("No runtime code change was required", findings, StringComparison.Ordinal);
        Assert.Contains("Is Payment Authority production-certifiable? Yes", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.3 Admin Order Payment Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.3 Admin Order Payment Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.3 admin order payment runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.3 Admin Order Payment Authority Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.3 Admin Order Payment Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
