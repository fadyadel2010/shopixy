namespace PointOfSale.Tests.Architecture;

public sealed class AbandonedCheckoutAnalyticsArchitectureTests
{
    [Fact]
    public void AdminAnalytics_MustUseTenantScopedSqlAuthorityAndDetectProjectionDrift()
    {
        var source = File.ReadAllText(ApiPath("Controllers", "AdminAbandonedCheckoutsController.cs"));

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", source, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CheckoutSessions", source, StringComparison.Ordinal);
        Assert.Contains("session.TenantId == tenantId", source, StringComparison.Ordinal);
        Assert.Contains("RequiresSqlFallbackAsync", source, StringComparison.Ordinal);
        Assert.Contains("return await GetSqlListAsync", source, StringComparison.Ordinal);
        Assert.Contains("var abandonmentRate = totalCount > 0", source, StringComparison.Ordinal);
        Assert.Contains("var conversionRate = totalCount > 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderConversionBridge_MustRejectNonEcommerceOrdersBeforeMutation()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "CheckoutSessionOrderConversionConsumer.cs"));
        var guardIndex = source.IndexOf("@event.Source != OrderSource.Ecommerce", StringComparison.Ordinal);
        var mutationIndex = source.IndexOf("await _sessionService.MarkAsConvertedAsync", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0, "The order conversion bridge must enforce the e-commerce source boundary.");
        Assert.True(mutationIndex > guardIndex, "The source boundary must be checked before checkout-session mutation.");
    }

    [Fact]
    public void AdminDetailContract_MustNotExposeRawRecoveryToken()
    {
        var source = File.ReadAllText(ApiPath("DTOs", "Checkout", "CheckoutSessionDtos.cs"));
        var start = source.IndexOf("public class AbandonedCheckoutDetail", StringComparison.Ordinal);
        var end = source.IndexOf("public class AbandonedCheckoutItemDto", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "The abandoned-checkout detail contract must be present.");
        Assert.DoesNotContain("RecoveryToken", source[start..end], StringComparison.Ordinal);
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine(RepositoryRoot(), "PointOfSale.API", Path.Combine(parts));

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine(RepositoryRoot(), "PointOfSale.Infrastructure", Path.Combine(parts));

    private static string RepositoryRoot() =>
        Environment.GetEnvironmentVariable("POINT_OF_SALE_SOLUTION_ROOT")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
