namespace PointOfSale.Tests.Architecture;

public sealed class ProviderDeliveryHardeningArchitectureTests
{
    [Fact]
    public void HttpSender_MustRemainProviderAgnostic()
    {
        var sender = ReadRepoFile("PointOfSale.Infrastructure/Analytics/Delivery/ProviderDeliveryHttpSender.cs");

        Assert.DoesNotContain("AnalyticsProvider.GoogleAnalytics", sender, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalyticsProvider.Meta", sender, StringComparison.Ordinal);
        Assert.DoesNotContain("switch", sender, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IProviderDeliveryHttpRequestCustomizer", sender, StringComparison.Ordinal);
        Assert.Contains("message.EndpointUrl", sender, StringComparison.Ordinal);
        Assert.Contains("message.HttpMethod", sender, StringComparison.Ordinal);
        Assert.Contains("message.HeadersJson", sender, StringComparison.Ordinal);
        Assert.Contains("message.ContentType", sender, StringComparison.Ordinal);
        Assert.Contains("message.TimeoutSeconds", sender, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderDeliveryLease_MustUseAtomicConditionalUpdateForRelationalStores()
    {
        var processor = ReadRepoFile("PointOfSale.Infrastructure/Analytics/Delivery/ProviderDeliveryProcessor.cs");

        Assert.Contains("db.Database.IsRelational()", processor, StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdateAsync", processor, StringComparison.Ordinal);
        Assert.Contains("message.ClaimedUntilUtc == null || message.ClaimedUntilUtc <= nowUtc", processor, StringComparison.Ordinal);
        Assert.Contains("message.DeliveredAtUtc == null", processor, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderDeliveryTelemetry_MustBeRegisteredForExport()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("ProviderDeliveryMetrics.MeterName", program, StringComparison.Ordinal);
        Assert.Contains("ProviderDeliveryTelemetry.ActivitySourceName", program, StringComparison.Ordinal);
        Assert.Contains("WithTracing", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderDeliveryMigrationSnapshot_MustContainOutboxAndReplayAudit()
    {
        var snapshot = ReadRepoFile("PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        Assert.Contains("ProviderDeliveryOutboxMessage", snapshot, StringComparison.Ordinal);
        Assert.Contains("ProviderDeliveryReplayAudit", snapshot, StringComparison.Ordinal);
        Assert.Contains("EndpointUrl", snapshot, StringComparison.Ordinal);
        Assert.Contains("ProviderDeliveryReplayAudits", migration, StringComparison.Ordinal);
        Assert.Contains("IX_ProviderDeliveryReplayAudits_ReplayId", snapshot, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
