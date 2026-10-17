using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class CarrierOperationSupportTests
{
    [Theory]
    [InlineData("manual")][InlineData("bosta")][InlineData("aramex")][InlineData("dhl")][InlineData("fedex")][InlineData("mylerz")]
    public void Operations_AreIndependentOfGlobalReadiness_AndNeverAuthorizeBillableLabelOrRepost(string carrier)
    {
        var operations = CarrierOperationSupports.Describe(carrier);
        Assert.Equal(6, operations.Count); Assert.Equal(6, operations.Select(o => o.Operation).Distinct().Count());
        Assert.Equal("StoredOnly", operations.Single(o => o.Operation == "label").Implementation);
        if (carrier != "manual") Assert.Contains("ReconciliationOnly", operations.Where(o => o.Operation == "recovery").Select(o => o.Implementation));
        if (carrier is "bosta" or "fedex" or "mylerz") Assert.Equal("Unavailable", operations.Single(o => o.Operation == "quote").Implementation);
        if (carrier == "mylerz") Assert.DoesNotContain(operations, o => o.Implementation == "Implemented");
        if (carrier is "aramex" or "dhl" or "fedex" or "mylerz") Assert.Equal("Unavailable", operations.Single(o => o.Operation == "cancellation").Implementation);
    }
}
