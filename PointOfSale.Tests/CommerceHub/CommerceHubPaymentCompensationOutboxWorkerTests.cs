using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubPaymentCompensationOutboxWorkerTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public CommerceHubPaymentCompensationOutboxWorkerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CloseDevelopmentSimulationCompensationAsync_ClosesWithoutGatewayOutcome()
    {
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var transactionId = $"DEV-SIM:ONLINE:{paymentId:N}:test";
        var payment = new CommerceHubPayment
        {
            Id = paymentId,
            OrderId = orderId,
            Amount = 2200m,
            Currency = "EGP",
            Provider = PaymentProvider.Paymob,
            ProviderReference = paymentId.ToString("N"),
            ProviderTransactionId = transactionId,
            Status = CommerceHubPaymentRecordStatus.RefundRequired,
            IsProcessed = true,
            ProcessedAtUtc = DateTime.UtcNow
        };
        payment.Attempts.Add(new CommerceHubPaymentAttempt
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            Payment = payment,
            Provider = PaymentProvider.Paymob,
            ProviderReference = payment.ProviderReference,
            ProviderTransactionId = transactionId,
            AttemptNumber = 1,
            Status = CommerceHubPaymentRecordStatus.RefundRequired
        });
        var message = new CommerceHubPaymentCompensationOutboxMessage
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            OrderId = orderId,
            Provider = PaymentProvider.Paymob,
            ProviderTransactionId = transactionId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Reason = "Captured payment requires compensation.",
            IdempotencyKey = $"test:{paymentId:N}",
            GatewayRequestStartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            RequiresOperatorReconciliation = true,
            IsFailed = true
        };

        _dbContext.AddRange(payment, message);
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await _dbContext.SaveChangesAsync();
        }

        await CommerceHubPaymentCompensationOutboxWorker.CloseDevelopmentSimulationCompensationAsync(
            _dbContext,
            message,
            payment,
            CancellationToken.None);

        _dbContext.ChangeTracker.Clear();
        var storedPayment = await _dbContext.CommerceHubPayments
            .Include(item => item.Attempts)
            .SingleAsync(item => item.Id == paymentId);
        var storedMessage = await _dbContext.CommerceHubPaymentCompensationOutboxMessages
            .SingleAsync(item => item.Id == message.Id);

        Assert.Equal(CommerceHubPaymentRecordStatus.Failed, storedPayment.Status);
        Assert.All(storedPayment.Attempts, attempt => Assert.Equal(CommerceHubPaymentRecordStatus.Failed, attempt.Status));
        Assert.NotNull(storedMessage.ProcessedAtUtc);
        Assert.Null(storedMessage.NextRetryAtUtc);
        Assert.False(storedMessage.IsFailed);
        Assert.False(storedMessage.RequiresOperatorReconciliation);
        Assert.NotNull(storedMessage.GatewayRequestStartedAtUtc);
        Assert.Contains("without a gateway refund", storedMessage.LastError, StringComparison.Ordinal);
    }

    public void Dispose() => _dbContext.Dispose();
}
