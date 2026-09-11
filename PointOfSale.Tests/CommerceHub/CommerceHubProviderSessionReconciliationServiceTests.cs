using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubProviderSessionReconciliationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IDistributedLockService> _lockService = new();
    private readonly Mock<IPaymobSensitiveArtifactProtector> _sensitiveArtifactProtector = new();
    private readonly CommerceHubProviderSessionReconciliationService _service;

    public CommerceHubProviderSessionReconciliationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _lockService
            .Setup(service => service.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.Acquired());
        _lockService
            .Setup(service => service.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _sensitiveArtifactProtector
            .Setup(protector => protector.ProtectWebhookPayload(It.IsAny<string>()))
            .Returns("paymob-webhook:v1:protected-reconciliation-evidence");

        _service = new CommerceHubProviderSessionReconciliationService(
            _dbContext,
            _lockService.Object,
            _sensitiveArtifactProtector.Object,
            NullLogger<CommerceHubProviderSessionReconciliationService>.Instance);
    }

    [Fact]
    public async Task ResolveNotCreatedAsync_WithExactEvidenceFreeOutcomeUnknownPayment_ShouldFailPaymentAndRetainAuditLog()
    {
        var payment = await SeedOutcomeUnknownPaymentAsync();

        var result = await _service.ResolveNotCreatedAsync(
            CommerceHubPaymentMethods.Paymob,
            ValidRequest(payment),
            "trace-reconcile-success");

        var stored = await _dbContext.CommerceHubPayments
            .IgnoreQueryFilters()
            .Include(item => item.Attempts)
            .Include(item => item.Logs)
            .SingleAsync(item => item.Id == payment.Id);

        Assert.Equal(CommerceHubPaymentRecordStatus.Failed, stored.Status);
        Assert.StartsWith("PROVIDER_SESSION_RECONCILED_NOT_CREATED:", stored.FailureReason, StringComparison.Ordinal);
        Assert.All(stored.Attempts, attempt => Assert.Equal(CommerceHubPaymentRecordStatus.Failed, attempt.Status));
        var auditLog = Assert.Single(stored.Logs.Where(log =>
            log.EventType == "initiate.paymob.outcome_reconciled_not_created"));
        Assert.Null(auditLog.ProviderEventId);
        Assert.Equal("paymob-webhook:v1:protected-reconciliation-evidence", auditLog.RawResponse);
        _sensitiveArtifactProtector.Verify(protector => protector.ProtectWebhookPayload(
            It.Is<string>(payload => payload.Contains("PAYMOB_DASHBOARD_NO_ORDER", StringComparison.Ordinal))), Times.Once);
        Assert.Equal("Failed", result.Status);
        Assert.True(result.RetryAllowed);
        _lockService.Verify(service => service.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ResolveNotCreatedAsync_WhenProviderReferenceDoesNotMatch_ShouldRejectWithoutMutation()
    {
        var payment = await SeedOutcomeUnknownPaymentAsync();
        var request = ValidRequest(payment, providerReference: "different-reference");

        var exception = await Assert.ThrowsAsync<CommerceHubProviderSessionReconciliationException>(() =>
            _service.ResolveNotCreatedAsync(CommerceHubPaymentMethods.Paymob, request, "trace-reference-mismatch"));

        Assert.Equal("PROVIDER_REFERENCE_MISMATCH", exception.Code);
        Assert.Equal(CommerceHubPaymentRecordStatus.Pending, payment.Status);
        Assert.DoesNotContain(payment.Logs, log => log.EventType == "initiate.paymob.outcome_reconciled_not_created");
    }

    [Fact]
    public async Task ResolveNotCreatedAsync_WhenProviderEventExists_ShouldFailClosed()
    {
        var payment = await SeedOutcomeUnknownPaymentAsync(providerEventId: "paymob-event-123");

        var exception = await Assert.ThrowsAsync<CommerceHubProviderSessionReconciliationException>(() =>
            _service.ResolveNotCreatedAsync(
                CommerceHubPaymentMethods.Paymob,
                ValidRequest(payment),
                "trace-provider-event"));

        Assert.Equal("PROVIDER_SESSION_NOT_ELIGIBLE", exception.Code);
        Assert.Equal(CommerceHubPaymentRecordStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task ResolveNotCreatedAsync_WithoutAdministratorConfirmation_ShouldRejectBeforeLock()
    {
        var payment = await SeedOutcomeUnknownPaymentAsync();
        var request = ValidRequest(payment, administratorConfirmation: false);

        var exception = await Assert.ThrowsAsync<CommerceHubProviderSessionReconciliationException>(() =>
            _service.ResolveNotCreatedAsync(CommerceHubPaymentMethods.Paymob, request, "trace-unconfirmed"));

        Assert.Equal("PROVIDER_RECONCILIATION_CONFIRMATION_REQUIRED", exception.Code);
        _lockService.Verify(service => service.TryAcquireLockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    private async Task<CommerceHubPayment> SeedOutcomeUnknownPaymentAsync(string? providerEventId = null)
    {
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = $"CH-REC-{Guid.NewGuid():N}"[..15],
            FullName = "Provider Reconciliation Test",
            PhoneNumber = "01000000000",
            AddressLine = "123 Reconciliation Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = CommerceHubPaymentMethods.Paymob,
            PaymentMethodName = "Paymob",
            Subtotal = 750m,
            ShippingTotal = 0m,
            TotalAmount = 750m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };
        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = order.TotalAmount,
            Currency = order.Currency,
            Provider = PaymentProvider.Paymob,
            ProviderReference = Guid.NewGuid().ToString("N"),
            ProviderIntentId = $"{CommerceHubPaymentSessionRecoveryAuthority.ProviderSessionReinitFencePrefix}{Guid.NewGuid():N}",
            Status = CommerceHubPaymentRecordStatus.Pending,
            FailureReason = "PROVIDER_SESSION_OUTCOME_UNKNOWN: Paymob intention creation has an unknown outcome.",
            IsProcessed = false
        };
        payment.Attempts.Add(new CommerceHubPaymentAttempt
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            Provider = PaymentProvider.Paymob,
            AttemptNumber = 1,
            ProviderReference = payment.ProviderReference,
            ProviderIntentId = $"{CommerceHubPaymentSessionRecoveryAuthority.ProviderSessionReinitFencePrefix}{Guid.NewGuid():N}",
            Status = CommerceHubPaymentRecordStatus.Pending,
            FailureReason = payment.FailureReason,
            CompletedAtUtc = DateTime.UtcNow
        });
        payment.Logs.Add(new CommerceHubPaymentLog
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            EventType = "initiate.paymob.outcome_unknown",
            ProviderEventId = providerEventId,
            RawResponse = "{}"
        });
        order.Payments.Add(payment);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubMarketplaceOrders.Add(order);
            await _dbContext.SaveChangesAsync();
        }

        return payment;
    }

    private static PaymentProviderSessionReconciliationRequest ValidRequest(
        CommerceHubPayment payment,
        string? providerReference = null,
        bool administratorConfirmation = true) => new()
    {
        PaymentId = payment.Id,
        ProviderReference = providerReference ?? payment.ProviderReference,
        EvidenceCode = CommerceHubProviderSessionReconciliationService.DashboardNoOrderEvidenceCode,
        EvidenceNote = "MID 645875 Test mode: no matching order or transaction appeared in Paymob.",
        AdministratorConfirmation = administratorConfirmation
    };

    public void Dispose() => _dbContext.Dispose();
}
