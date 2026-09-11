using PointOfSale.Infrastructure.Consumers;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubProjectionEndpointIsolationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEndpointName_WithoutDeploymentPrefix_PreservesExistingQueue(string? prefix)
    {
        Assert.Equal(
            "commercehub-projection",
            CommerceHubProjectionConsumerDefinition.ResolveEndpointName(prefix));
    }

    [Theory]
    [InlineData("Fadya Windows Development", "fadya-windows-development-commercehub-projection")]
    [InlineData("qa/blue", "qa-blue-commercehub-projection")]
    [InlineData("  local--dev  ", "local-dev-commercehub-projection")]
    public void ResolveEndpointName_WithDeploymentPrefix_IsolatesProjectionQueue(
        string prefix,
        string expected)
    {
        Assert.Equal(
            expected,
            CommerceHubProjectionConsumerDefinition.ResolveEndpointName(prefix));
    }

    [Fact]
    public void ResolveEndpointName_WithOnlySeparators_FailsClosed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CommerceHubProjectionConsumerDefinition.ResolveEndpointName("---___///"));
    }
}
