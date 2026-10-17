namespace PointOfSale.Tests.Architecture;

// Historical findings remain historical evidence, not a statement that the
// current working tree or the merchant runtime has passed production E2E.
internal static class HistoricalShippingCertificationBoundary
{
    public static void AssertPriorClosureConditional()
    {
        var report = CertificationSource.ReadRepoFile(".ai/reports/SHIPPING_CARRIERS_FINAL_CLOSURE_CERTIFICATION.md");
        Assert.Contains("CONDITIONAL", report, StringComparison.Ordinal);
        Assert.Contains("18 historical architecture/source/document assertions remain failing", report, StringComparison.Ordinal);
        Assert.Contains("Mylerz remains unavailable and FedEx remains NotCertified", report, StringComparison.Ordinal);
        Assert.Contains("authenticated storefront checkout E2E", report, StringComparison.Ordinal);
    }
}
