using System;
using System.IO;
using System.Text.RegularExpressions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public class CommerceHubRefundHardeningTests
{
    [Fact]
    public void CommerceHubSubOrderItem_MustExpose_ItemLevelDeliveryFields()
    {
        var itemType = typeof(CommerceHubSubOrderItem);

        Assert.Equal(typeof(CommerceHubOrderItemStatus), itemType.GetProperty(nameof(CommerceHubSubOrderItem.Status))?.PropertyType);
        Assert.Equal(typeof(DateTime?), itemType.GetProperty(nameof(CommerceHubSubOrderItem.DeliveredAt))?.PropertyType);
    }

    [Fact]
    public void CustomerRefundService_MustUseSerializableTransaction_AndItemRowLocking()
    {
        var servicePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CustomerRefundService.cs");

        var source = File.ReadAllText(Path.GetFullPath(servicePath));

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", source);
        Assert.Contains("FOR UPDATE", source);
    }

    [Fact]
    public void ApplicationDbContext_MustAllowCommerceHubSubOrderItem_LifecycleFields()
    {
        var dbContextPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Data",
            "ApplicationDbContext.cs");

        var source = File.ReadAllText(Path.GetFullPath(dbContextPath));

        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubSubOrderItem>", source);
        Assert.Contains("nameof(CommerceHubSubOrderItem.Status)", source);
        Assert.Contains("nameof(CommerceHubSubOrderItem.DeliveredAt)", source);
    }

    [Fact]
    public void RefundHardeningMigration_MustAddIdempotencyIndex()
    {
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "Sql",
            "20260323_ZZF_HardenCommerceHubRefundRequestSafety.sql");

        var migration = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("ALTER TABLE \"CommerceHubRefundRequests\"", migration);
        Assert.Contains("\"IdempotencyKey\"", migration);
        Assert.Contains("IX_CommerceHubRefundRequests_IdempotencyKey", migration);
    }

    [Fact]
    public void RefundExecutionService_MustUseSerializableExecutionAndRefundRequestLocking()
    {
        var servicePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "RefundExecutionService.cs");

        var source = File.ReadAllText(Path.GetFullPath(servicePath));

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", source);
        Assert.True(ContainsSqlFromClause(source, "CommerceHubRefundRequests"),
            "Expected SQL query with FROM \"CommerceHubRefundRequests\"");
        Assert.True(ContainsSqlFromClause(source, "CommerceHubRefunds"),
            "Expected SQL query with FROM \"CommerceHubRefunds\"");
        Assert.True(ContainsSqlFromClause(source, "CommerceHubRefundExecutions"),
            "Expected SQL query with FROM \"CommerceHubRefundExecutions\"");
        Assert.Contains("FOR UPDATE", source);
        Assert.Contains("refund-exec-", source);
        Assert.Contains("REFUND_PAYOUT_INITIATED", source);
        Assert.Contains("REFUND_PAYOUT_CONFIRMED", source);
        Assert.Contains("REFUND_PAYOUT_RECONCILIATION_REQUIRED", source);
        Assert.Contains("CommerceHubRefundStatus.ReconciliationRequired", source);
        Assert.Contains("manual_transfer_reconciliation_required", source);
        Assert.Contains("trackedRefund = new CommerceHubRefund", source);
        Assert.DoesNotContain("manual-failed", source);
        Assert.Contains("REFUND_RETURN_TRIGGER_NOT_REACHED", source);
    }

    private static bool ContainsSqlFromClause(string source, string tableName)
    {
        var normalizedSource = source.Replace("\"\"", "\"", StringComparison.Ordinal);
        return Regex.IsMatch(
            normalizedSource,
            $@"FROM\s+""{Regex.Escape(tableName)}""",
            RegexOptions.None);
    }

    [Fact]
    public void RefundControlMigration_MustAddExecutionColumns()
    {
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "Sql",
            "20260324_ZZG_AddCommerceHubSuperAdminRefundControl.sql");

        var migration = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("\"ReviewedBy\"", migration);
        Assert.Contains("\"ProcessingStartedAtUtc\"", migration);
        Assert.Contains("\"RefundRequestId\"", migration);
        Assert.Contains("\"IsPostPayoutAdjustment\"", migration);
    }

    [Fact]
    public void ManualRefundExecutionMigration_MustAddUniqueExecutionTable()
    {
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "Sql",
            "20260324_ZZJ_AddCommerceHubManualRefundExecution.sql");

        var migration = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("CREATE TABLE IF NOT EXISTS \"CommerceHubRefundExecutions\"", migration);
        Assert.Contains("\"TransactionReference\"", migration);
        Assert.Contains("\"ExecutedBy\"", migration);
        Assert.Contains("IX_CommerceHubRefundExecutions_RefundId", migration);
    }

    [Fact]
    public void ReturnManagementMigration_MustAddReturnWorkflowTable_AndRefundTriggerColumns()
    {
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "Sql",
            "20260324_ZZK_AddCommerceHubReturnManagementSystem.sql");

        var migration = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("ALTER TABLE \"CommerceHubRefundRequests\"", migration);
        Assert.Contains("\"ReturnRequestId\"", migration);
        Assert.Contains("\"RefundExecutionTrigger\"", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"CommerceHubReturnRequests\"", migration);
        Assert.Contains("IX_CommerceHubReturnRequests_RefundId", migration);
    }
}
