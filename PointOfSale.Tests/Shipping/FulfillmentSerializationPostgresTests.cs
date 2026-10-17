using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Fulfillment;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed partial class DhlParcelEvidencePostgresTests
{
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(3, false, false)]
    [InlineData(1, true, false)]
    [InlineData(1, false, true)]
    public async Task Fulfillment_LockSerializationFailure_IsBoundedAndNeverDiscardsCallerWork(int failures, bool callerOwned, bool dirty)
    {
        var shipment = await Seed(carrier: "manual", cod: true);
        var interceptor = new RejectOrderLock(failures);
        await using var baseline = scope.CreateDbContext(tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(baseline.Database.GetConnectionString())
            .AddInterceptors(interceptor).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        await using var transaction = callerOwned ? await db.Database.BeginTransactionAsync() : null;
        var user = dirty ? await db.Users.SingleAsync() : null;
        if (user != null) user.FirstName = "Uncommitted caller-owned change";
        var service = new OrderFulfillmentService(db, events.Object, NullLogger<OrderFulfillmentService>.Instance);
        if (callerOwned || dirty || failures == 3)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => service.PackAsync(shipment.OrderId, FulfillmentRequestSource.Manual));
            Assert.Contains("synthetic lock serialization", error.ToString());
            Assert.Equal(callerOwned || dirty ? 1 : 3, interceptor.Attempts);
            if (user != null)
            {
                Assert.Equal(EntityState.Modified, db.Entry(user).State);
                Assert.Equal("Uncommitted caller-owned change", user.FirstName);
            }
            if (callerOwned)
            {
                Assert.Same(transaction, db.Database.CurrentTransaction);
                await transaction!.RollbackAsync();
            }
        }
        else
        {
            var packed = await service.PackAsync(shipment.OrderId, FulfillmentRequestSource.Manual);
            Assert.True(packed.Success, packed.ErrorMessage);
            Assert.Equal(2, interceptor.Attempts);
            var duplicate = await service.PackAsync(shipment.OrderId, FulfillmentRequestSource.Manual);
            Assert.True(duplicate.Success);
            events.Verify(e => e.PublishAsync(Moq.It.IsAny<OrderPackedEvent>(), Moq.It.IsAny<CancellationToken>()), Moq.Times.Once);
        }
        await using var verify = scope.CreateDbContext(tenant);
        var order = await verify.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(!callerOwned && !dirty && failures == 1 ? FulfillmentStatus.Packed : FulfillmentStatus.Confirmed, order.FulfillmentStatus);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Empty(await verify.JournalEntries.ToListAsync()); Assert.Empty(await verify.AccountingOutboxMessages.ToListAsync());
    }

    private sealed class RejectOrderLock(int failures) : DbCommandInterceptor
    {
        public int Attempts;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Orders\"", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Attempts++;
                if (Attempts <= failures) throw new PostgresException("synthetic lock serialization", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
            }
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    public async Task Fulfillment_TransactionStartSerializationFailure_IsBoundedAndPreservesDirtyWork(int failures, bool dirty)
    {
        var shipment = await Seed(carrier: "manual", cod: true);
        var interceptor = new RejectTransactionStart(failures);
        await using var baseline = scope.CreateDbContext(tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(baseline.Database.GetConnectionString())
            .AddInterceptors(interceptor).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        var user = dirty ? await db.Users.SingleAsync() : null;
        if (user != null) user.FirstName = "Uncommitted caller-owned change";
        var service = new OrderFulfillmentService(db, events.Object, NullLogger<OrderFulfillmentService>.Instance);
        if (dirty || failures == 3)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PackAsync(shipment.OrderId, FulfillmentRequestSource.Manual));
            var providerError = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.SerializationFailure, providerError.SqlState);
            Assert.Equal("synthetic transaction-start serialization", providerError.MessageText);
            Assert.Equal(dirty ? 1 : 3, interceptor.Attempts);
            if (user != null)
            {
                Assert.Equal(EntityState.Modified, db.Entry(user).State);
                Assert.Equal("Uncommitted caller-owned change", user.FirstName);
            }
            events.Verify(e => e.PublishAsync(Moq.It.IsAny<OrderPackedEvent>(), Moq.It.IsAny<CancellationToken>()), Moq.Times.Never);
        }
        else
        {
            var result = await service.PackAsync(shipment.OrderId, FulfillmentRequestSource.Manual);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, interceptor.Attempts);
            events.Verify(e => e.PublishAsync(Moq.It.IsAny<OrderPackedEvent>(), Moq.It.IsAny<CancellationToken>()), Moq.Times.Once);
        }
        Assert.Null(db.Database.CurrentTransaction);
        await using var verify = scope.CreateDbContext(tenant);
        var order = await verify.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(!dirty && failures == 1 ? FulfillmentStatus.Packed : FulfillmentStatus.Confirmed, order.FulfillmentStatus);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Empty(await verify.JournalEntries.ToListAsync()); Assert.Empty(await verify.AccountingOutboxMessages.ToListAsync());
    }

    private sealed class RejectTransactionStart(int failures) : DbTransactionInterceptor
    {
        public int Attempts;
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts <= failures)
                throw new PostgresException("synthetic transaction-start serialization", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
            return ValueTask.FromResult(result);
        }
    }
}
