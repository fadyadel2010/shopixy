using System.Collections.Generic;
using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class S16BPaymentAuthorityExternalIdempotencyTests
{
    [Fact]
    public void PaymentLifecycle_MustRequireProviderAuthority_ForOnlineGatewaySuccess()
    {
        var lifecycleSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");
        var contractSource = ReadRepoFile(
            "PointOfSale.Core",
            "Interfaces",
            "Payments",
            "IPaymentLifecycleService.cs");

        Assert.Contains("ProviderAmount", contractSource, StringComparison.Ordinal);
        Assert.Contains("ProviderCurrency", contractSource, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.GatewayWebhook", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("request.ProviderAmount.HasValue", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("request.ProviderCurrency", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.AmountMismatch", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("RecordPaymentAmountMismatch", lifecycleSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentRegistration_MustPersistCarrierRequestId_AndBlockDuplicateCreateOnRetry()
    {
        var serviceSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Shipping",
            "CarrierShipmentService.cs");
        var adapterSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Shipping",
            "IShippingCarrierAdapter.cs");

        Assert.Contains("CarrierRequestId", adapterSource, StringComparison.Ordinal);
        Assert.Contains("GetShipmentByCarrierRequestIdAsync", adapterSource, StringComparison.Ordinal);
        Assert.Contains("EnsureCarrierRequestIdPersistedAsync", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ReconcilePendingCarrierRequestAsync", serviceSource, StringComparison.Ordinal);
        Assert.Contains("RecordShipmentExternalDuplicatePrevented", serviceSource, StringComparison.Ordinal);
        Assert.Contains("Prior carrier create attempt could not be reconciled; duplicate create was blocked.", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentSafetyBaseline_MustUseUniqueFilteredIndexes()
    {
        var migrationSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260723214642_Baseline_Recovery3.cs");
        var telemetrySource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Instrumentation",
            "CommerceReliabilityTelemetry.cs");

        Assert.Contains("IX_Shipments_TenantId_CarrierCode_CarrierRequestId", migrationSource, StringComparison.Ordinal);
        Assert.Contains("columns: new[] { \"TenantId\", \"CarrierCode\", \"CarrierRequestId\" }", migrationSource, StringComparison.Ordinal);
        Assert.Contains("unique: true", migrationSource, StringComparison.Ordinal);
        Assert.Contains("filter: \"\\\"IsDeleted\\\" = false AND \\\"CarrierRequestId\\\" IS NOT NULL\"", migrationSource, StringComparison.Ordinal);
        Assert.Contains("migration_concurrent_index_applied_total", telemetrySource, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var pathParts = new List<string>
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."
        };

        pathParts.AddRange(segments);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(pathParts.ToArray())));
    }
}
