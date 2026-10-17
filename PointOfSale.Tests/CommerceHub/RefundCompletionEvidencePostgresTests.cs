using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.CommerceHub;

public sealed class RefundCompletionEvidencePostgresTests : IDisposable
{
    private readonly TestDatabaseScope scope = TestDatabaseScope.Open();

    [Theory]
    [InlineData("valid", true)]
    [InlineData("bare-status", false)]
    [InlineData("amount", false)]
    [InlineData("currency", false)]
    [InlineData("actor", false)]
    [InlineData("reference", false)]
    [InlineData("deleted-execution", false)]
    [InlineData("failed-execution", false)]
    [InlineData("wrong-tenant", false)]
    [InlineData("wrong-item", false)]
    [InlineData("request-not-paid", false)]
    [InlineData("missing-audit", false)]
    [InlineData("simulation", false)]
    [InlineData("no-money-attestation", false)]
    [InlineData("wrong-audit-refund", false)]
    [InlineData("malformed-audit", false)]
    [InlineData("stale-audit", false)]
    [InlineData("provider-completed", true)]
    [InlineData("provider-paid", false)]
    public async Task Reconciliation_QualifiesActualRefundAuthority_WithoutFinancialMutation(string defect, bool admitted)
    {
        await using var db = scope.CreateDbContext();
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds()).UtcDateTime;
        var actor = Guid.NewGuid(); var tenant = Guid.NewGuid();
        var customer = CommerceHubCustomer.Create($"{Guid.NewGuid():N}@example.invalid", "Synthetic refund customer", "01000000000", "synthetic");
        var order = new CommerceHubMarketplaceOrder { Id = Guid.NewGuid(), CustomerId = customer.Id, OrderNumber = $"QA-{Guid.NewGuid():N}",
            FullName = "Synthetic", PhoneNumber = "synthetic", AddressLine = "Fixture", City = "Fixture", Currency = "EGP", Subtotal = 100, TotalAmount = 100,
            PaymentMethodCode = "ONLINE", Status = OrderStatus.Delivered, PaymentStatus = PaymentStatus.Refunded };
        var sub = new CommerceHubSubOrder { Id = Guid.NewGuid(), MarketplaceOrder = order, MarketplaceOrderId = order.Id, StoreId = tenant,
            StoreName = "Synthetic vendor", OrderNumber = $"SUB-{Guid.NewGuid():N}", Currency = "EGP", Subtotal = 100, Total = 100, VendorEarningAmount = 100,
            PaymentMethod = CommerceHubPaymentMethod.Online, PaymentMethodCode = "ONLINE", Status = OrderStatus.Delivered, PaymentStatus = PaymentStatus.Refunded };
        var item = new CommerceHubSubOrderItem { Id = Guid.NewGuid(), SubOrder = sub, SubOrderId = sub.Id, ListingId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(), ProductName = "Fixture", Quantity = 1, UnitPrice = 100, Total = 100, Status = CommerceHubOrderItemStatus.Delivered };
        var payment = new CommerceHubPayment { Id = Guid.NewGuid(), Order = order, OrderId = order.Id, Provider = PaymentProvider.Paymob,
            ProviderReference = $"fixture-{Guid.NewGuid()}", ProviderTransactionId = $"fixture-{Guid.NewGuid()}", Status = CommerceHubPaymentRecordStatus.Paid,
            Amount = 100, RefundedAmount = 100, Currency = "EGP", IsProcessed = true, PaidAtUtc = now.AddDays(-1) };
        var request = new CommerceHubRefundRequest { Id = Guid.NewGuid(), Order = order, OrderId = order.Id, OrderItem = item, OrderItemId = item.Id,
            Customer = customer, CustomerId = customer.Id, VendorId = tenant, Quantity = 1, UnitPriceSnapshot = 100, TotalAmount = 100, Reason = "Synthetic",
            Status = CommerceHubRefundRequestStatus.Paid, PayoutMethodSnapshot = RefundPayoutMethod.VodafoneCash, PayoutAccountSnapshot = "synthetic-account", CompletedAtUtc = now };
        var refund = new CommerceHubRefund { Id = Guid.NewGuid(), TenantId = tenant, SubOrder = sub, SubOrderId = sub.Id, OrderItemId = item.Id,
            Quantity = 1, RefundRequestId = request.Id, Payment = payment, PaymentId = payment.Id, Amount = 100, VendorEarningReversalAmount = 100,
            Currency = "EGP", Status = CommerceHubRefundStatus.Completed, Reference = "synthetic-transfer", GatewayStatus = "manual-confirmed", ProcessedAtUtc = now,
            IdempotencyKey = $"fixture-{Guid.NewGuid()}", IsFullRefund = true };
        var execution = new CommerceHubRefundExecution { Id = Guid.NewGuid(), Refund = request, RefundId = request.Id, Amount = 100, Currency = "EGP",
            Method = RefundPayoutMethod.VodafoneCash, AccountSnapshot = "synthetic-account", TransactionReference = refund.Reference, ExecutedBy = actor, ExecutedAt = now, IsSuccessful = true };
        object Details(bool completion) => completion
            ? new { RefundId = request.Id, FinancialRefundId = defect == "wrong-audit-refund" ? Guid.NewGuid() : refund.Id, ExecutionId = execution.Id,
                ActorId = actor, Amount = 100m, Currency = defect == "currency" ? "USD" : "EGP", Method = 0, TransactionReference = "synthetic-transfer", IsDevelopmentSimulation = defect == "simulation", ExternalMoneyMovementConfirmed = true }
            : new { RefundId = request.Id, ActorId = actor, Amount = 100m, Currency = "EGP", Method = 0, TransactionReference = "synthetic-transfer",
                IsDevelopmentSimulation = defect == "simulation", ExternalMoneyMovementConfirmed = true, ConfirmMoneySent = defect != "no-money-attestation" };
        var audits = new[] { false, true }.Select(completion => {
            var action = completion ? "REFUND_PAYOUT_CONFIRMED" : "REFUND_PAYOUT_INITIATED";
            return new AuditLog { ActionType = action, EntityType = "CommerceHubRefundRequest", EntityId = request.Id.ToString(), UserId = actor,
                Source = "SuperAdminAPI", SuccessFlag = true, TimestampUtc = defect == "stale-audit" ? now.AddDays(-1) : now,
                AfterDataJson = defect == "malformed-audit" ? "{}" : JsonSerializer.Serialize(new { ActorId = actor, EntityId = request.Id.ToString(), Action = action, Details = Details(completion) }) };
        }).ToArray();
        if (defect == "amount") execution.Amount = 99;
        if (defect == "actor") execution.ExecutedBy = Guid.NewGuid();
        if (defect == "reference") execution.TransactionReference = "changed";
        if (defect == "deleted-execution") execution.IsDeleted = true;
        if (defect == "failed-execution") execution.IsSuccessful = false;
        if (defect == "wrong-tenant") request.VendorId = Guid.NewGuid();
        if (defect == "request-not-paid") request.Status = CommerceHubRefundRequestStatus.Approved;
        if (defect == "provider-completed") refund.GatewayStatus = "completed";
        if (defect == "provider-paid") refund.GatewayStatus = "paid";
        order.SubOrders.Add(sub); sub.Items.Add(item);
        db.CommerceHubCustomers.Add(customer); db.CommerceHubMarketplaceOrders.Add(order); db.CommerceHubPayments.Add(payment);
        db.CommerceHubRefundRequests.Add(request); db.CommerceHubRefunds.Add(refund);
        if (defect != "bare-status") db.CommerceHubRefundExecutions.Add(execution);
        if (defect is not ("missing-audit" or "bare-status")) db.AuditLogs.AddRange(audits);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()) await db.SaveChangesAsync();
        if (defect == "wrong-item") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CommerceHubRefunds\" SET \"OrderItemId\"=NULL WHERE \"Id\"={refund.Id}");
        db.ChangeTracker.Clear();
        var before = await Signature(db);
        var aggregate = new CommerceHubDailyFinancialAggregateService(db, NullLogger<CommerceHubDailyFinancialAggregateService>.Instance);
        db.CommerceHubDailyFinancialAggregates.AddRange(await aggregate.RecalculateWindowAsync(now.Date, DateTime.UtcNow.Date));
        await db.SaveChangesAsync();
        var debt = new CommerceHubVendorDebtService(db, Options.Create(new CommerceHubVendorDebtOptions()));
        var service = new FinancialReconciliationService(db, new CommerceHubFinanceReconciliationReadService(db, debt),
            aggregate,
            new CommerceHubFinancialAuditService(db), new NoOpFinancialReconciliationAlertHook(), NullLogger<FinancialReconciliationService>.Instance);
        var result = await service.RunAsync();
        Assert.True(result.Report.Status == (admitted ? "Healthy" : "Critical"), result.Report.DetailsJson);
        Assert.Equal(admitted ? 0 : 1, result.Report.MismatchCount);
        // Exercise the operation-time refund gate independently of its other
        // ledger/outbox readiness prerequisites. A stale green hourly report
        // must not authorize an unproved Completed refund.
        var gate = new CommerceHubFinancialReadinessGate(db,
            Moq.Mock.Of<IOptionsMonitor<CommerceHubEmergencyStopOptions>>(o => o.CurrentValue == new CommerceHubEmergencyStopOptions()),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);
        var method = typeof(CommerceHubFinancialReadinessGate).GetMethod("EnsureExternalRefundsReconciledAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Task CheckGate() => (Task)method.Invoke(gate, [tenant, "isolated-refund-evidence", DateTime.UtcNow, CancellationToken.None])!;
        if (admitted) await CheckGate();
        else Assert.Equal("EXTERNAL_REFUND_RECONCILIATION_REQUIRED", (await Assert.ThrowsAsync<CommerceHubCheckoutException>(CheckGate)).Code);
        Assert.Equal(before, await Signature(db));
        Assert.Empty(await db.JournalEntries.ToListAsync());
        Assert.Empty(await db.LedgerEntries.ToListAsync());
        Assert.Empty(await db.CarrierRemittances.ToListAsync());
    }

    private static async Task<string> Signature(ApplicationDbContext db)
    {
        var connection = db.Database.GetDbConnection(); if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT md5(string_agg(v, ',' ORDER BY v)) FROM (SELECT to_jsonb(r)::text AS v FROM \"CommerceHubRefunds\" r UNION ALL SELECT to_jsonb(r)::text FROM \"CommerceHubRefundExecutions\" r UNION ALL SELECT to_jsonb(r)::text FROM \"CommerceHubRefundRequests\" r UNION ALL SELECT to_jsonb(r)::text FROM \"CommerceHubPayments\" r) s";
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
    public void Dispose() => scope.Dispose();
}
