using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateRecoveryNullTenantSourceTests
{
    [Fact]
    public void AffiliateService_MustUseNullableSafeAffiliateProjections_InRecoveryAndCommissionPaths()
    {
        var source = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Services",
            "Affiliate",
            "AffiliateService.cs");

        Assert.Contains("TenantId = (Guid?)a.TenantId", source, StringComparison.Ordinal);
        Assert.Contains(".Select(a => (Guid?)a.Id)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".FirstOrDefaultAsync(a => a.Id == affiliateAccountId, ct)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".FirstOrDefaultAsync(a => a.Id == attribution.AffiliateAccountId, ct)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".FirstOrDefaultAsync(a => a.ReferralCode == codeNormalized && !a.IsDeleted, ct)", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var pathParts = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."
        }.Concat(segments).ToArray();

        return File.ReadAllText(Path.GetFullPath(Path.Combine(pathParts)));
    }
}
