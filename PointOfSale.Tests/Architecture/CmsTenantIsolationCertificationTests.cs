using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Architecture;

public sealed class CmsTenantIsolationCertificationTests
{
    [Fact]
    public void CmsPage_MustBeTenantOwnedAndGloballyTenantFiltered()
    {
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.True(typeof(ITenantOwned).IsAssignableFrom(typeof(CmsPage)));
        Assert.Contains("modelBuilder.Entity<CmsPage>().HasQueryFilter", dbContext, StringComparison.Ordinal);
        Assert.Contains("!e.IsDeleted && e.TenantId == CurrentTenantId", dbContext, StringComparison.Ordinal);
    }
}
