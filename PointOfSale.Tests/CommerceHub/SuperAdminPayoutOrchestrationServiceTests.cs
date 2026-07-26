using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class SuperAdminPayoutOrchestrationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_InvokesCanonicalPayoutLifecycleInOrder()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();
        var payoutMethodId = Guid.NewGuid();
        var earningId = Guid.NewGuid();
        SeedExecutionMapping(dbContext, vendorId, payoutMethodId, earningId, amount: 50m);

        var actorId = Guid.NewGuid();
        var request = new SuperAdminExecuteVendorPayoutRequestDto
        {
            Amount = 50m,
            PayoutMethodId = payoutMethodId,
            TransferReference = "BANK-TRF-001",
            ExternalReferenceId = "EXT-BANK-TRF-001",
            BankTransactionId = "BANK-TXN-001",
            IdempotencyKey = "super-admin-payout-canonical"
        };
        var payoutId = Guid.NewGuid();
        var externalTransferIdempotencyKey = "CH-PAYOUT-INTENT";
        var handoffToken = "HANDOFF-TOKEN";
        var sequence = new MockSequence();
        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        canonicalPayoutService
            .InSequence(sequence)
            .Setup(service => service.CreatePayoutAsync(
                vendorId,
                It.Is<CommerceHubCreatePayoutRequestDto>(create =>
                    create.Method == CommerceHubPayoutMethod.BankTransfer &&
                    create.IdempotencyKey == request.IdempotencyKey &&
                    create.Reference == request.TransferReference &&
                    create.EarningIds.SequenceEqual(new[] { earningId })),
                It.Is<string>(correlationId => correlationId.Contains(request.IdempotencyKey, StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubPayoutDto
            {
                PayoutId = payoutId,
                TenantId = vendorId,
                Amount = request.Amount,
                Status = "Pending",
                ExternalTransferIdempotencyKey = externalTransferIdempotencyKey
            });
        canonicalPayoutService
            .InSequence(sequence)
            .Setup(service => service.MarkProcessingAsync(
                vendorId,
                payoutId,
                It.Is<CommerceHubUpdatePayoutRequestDto>(update =>
                    update.Reference == request.TransferReference &&
                    update.ExternalReferenceId == request.ExternalReferenceId &&
                    update.BankTransactionId == request.BankTransactionId &&
                    update.ExternalTransferIdempotencyKey == externalTransferIdempotencyKey &&
                    !update.ExternalTransferConfirmedExecuted),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                "ops@commercehub.test"))
            .ReturnsAsync(new CommerceHubPayoutDto
            {
                PayoutId = payoutId,
                TenantId = vendorId,
                Amount = request.Amount,
                Status = "Processing",
                ExternalTransferIdempotencyKey = externalTransferIdempotencyKey,
                ExternalTransferHandoffToken = handoffToken,
                ExternalTransferHandoffCommittedAtUtc = DateTime.UtcNow
            });
        canonicalPayoutService
            .InSequence(sequence)
            .Setup(service => service.MarkCompletedAsync(
                vendorId,
                payoutId,
                It.Is<CommerceHubUpdatePayoutRequestDto>(update =>
                    update.Reference == request.TransferReference &&
                    update.ExternalReferenceId == request.ExternalReferenceId &&
                    update.BankTransactionId == request.BankTransactionId &&
                    update.ExternalTransferIdempotencyKey == externalTransferIdempotencyKey &&
                    update.ExternalTransferHandoffToken == handoffToken &&
                    update.ExternalTransferConfirmedExecuted),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                "ops@commercehub.test"))
            .ReturnsAsync(new CommerceHubPayoutDto
            {
                PayoutId = payoutId,
                TenantId = vendorId,
                Amount = request.Amount,
                Status = "Completed"
            });

        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var actual = await orchestration.ExecuteAsync(
            vendorId,
            request,
            actorId,
            "ops@commercehub.test");

        Assert.Equal("Completed", actual.Status);
        canonicalPayoutService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotTranslateCanonicalBusinessExceptions()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();
        var payoutMethodId = Guid.NewGuid();
        SeedExecutionMapping(dbContext, vendorId, payoutMethodId, Guid.NewGuid(), amount: 50m);
        var request = new SuperAdminExecuteVendorPayoutRequestDto
        {
            Amount = 50m,
            PayoutMethodId = payoutMethodId,
            IdempotencyKey = "super-admin-payout-business-exception"
        };
        var expected = new CommerceHubCheckoutException(
            "PAYOUT_AMOUNT_EXCEEDS_PAYABLE",
            "Payout amount cannot exceed the vendor payable balance.",
            StatusCodes.Status409Conflict);

        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        canonicalPayoutService
            .Setup(service => service.CreatePayoutAsync(
                vendorId,
                It.IsAny<CommerceHubCreatePayoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var actual = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            orchestration.ExecuteAsync(vendorId, request, Guid.NewGuid(), null));

        Assert.Same(expected, actual);
        canonicalPayoutService.VerifyAll();
        canonicalPayoutService.Verify(
            service => service.MarkProcessingAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CommerceHubUpdatePayoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WrapsUnexpectedInfrastructureFailures()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();
        var payoutMethodId = Guid.NewGuid();
        SeedExecutionMapping(dbContext, vendorId, payoutMethodId, Guid.NewGuid(), amount: 50m);
        var request = new SuperAdminExecuteVendorPayoutRequestDto
        {
            Amount = 50m,
            PayoutMethodId = payoutMethodId,
            IdempotencyKey = "super-admin-payout-unexpected-exception"
        };
        var expected = new TimeoutException("database timeout");

        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        canonicalPayoutService
            .Setup(service => service.CreatePayoutAsync(
                vendorId,
                It.IsAny<CommerceHubCreatePayoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var actual = await Assert.ThrowsAsync<SuperAdminPayoutOrchestrationException>(() =>
            orchestration.ExecuteAsync(vendorId, request, Guid.NewGuid(), null));

        Assert.Same(expected, actual.InnerException);
        canonicalPayoutService.VerifyAll();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void SeedExecutionMapping(
        ApplicationDbContext dbContext,
        Guid vendorId,
        Guid payoutMethodId,
        Guid earningId,
        decimal amount)
    {
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            StoreId = vendorId,
            Currency = "EGP",
            OrderNumber = "CH-PAYOUT-ORCH",
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubVendorPayoutMethods.Add(new CommerceHubVendorPayoutMethod
        {
            Id = payoutMethodId,
            VendorId = vendorId,
            MethodType = CommerceHubPayoutMethod.BankTransfer,
            DestinationValue = "EG-ORCH-001",
            IsDefault = true,
            IsVerified = true
        });
        dbContext.CommerceHubVendorEarnings.Add(new CommerceHubVendorEarning
        {
            Id = earningId,
            TenantId = vendorId,
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            Amount = amount,
            Status = CommerceHubVendorEarningStatus.Available,
            AvailableAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        });
        dbContext.SaveChanges();
    }
}
