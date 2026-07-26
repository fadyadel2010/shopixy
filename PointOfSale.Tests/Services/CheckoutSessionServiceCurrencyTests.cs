using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services;

namespace PointOfSale.Tests.Services;

public sealed class CheckoutSessionServiceCurrencyTests : IDisposable, IAsyncDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IEventPublisher> _eventPublisher;
    private readonly CheckoutSessionService _service;

    public CheckoutSessionServiceCurrencyTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"CheckoutSessionCurrency_{Guid.NewGuid():N}")
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _dbContext.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Checkout Currency Tenant",
            Slug = $"checkout-currency-{_tenantId:N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        _dbContext.SaveChanges();

        _eventPublisher = new Mock<IEventPublisher>();
        _eventPublisher
            .Setup(publisher => publisher.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eventPublisher
            .Setup(publisher => publisher.PublishSensitiveAsync(It.IsAny<CheckoutSessionProofEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new CheckoutSessionService(
            _dbContext,
            _eventPublisher.Object,
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionService>.Instance);
    }

    [Fact]
    public async Task CreateOrResumeSessionAsync_WhenCurrencyIsMissing_Throws()
    {
        var exception = await Assert.ThrowsAsync<FinancialCurrencyMissingException>(() =>
            _service.CreateOrResumeSessionAsync(
                _tenantId,
                Guid.NewGuid(),
                "session-missing-currency",
                onlineCustomerId: null,
                itemsSnapshotJson: "[]",
                cartTotal: 100m,
                currency: string.Empty,
                deviceType: "web",
                source: "storefront",
                CancellationToken.None));

        Assert.Equal("checkout.session.currency", exception.Component);
        _eventPublisher.Verify(
            publisher => publisher.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrResumeSessionAsync_WhenCurrencyDoesNotMatchTenant_Throws()
    {
        var exception = await Assert.ThrowsAsync<FinancialCurrencyMismatchException>(() =>
            _service.CreateOrResumeSessionAsync(
                _tenantId,
                Guid.NewGuid(),
                "session-mismatch-currency",
                onlineCustomerId: null,
                itemsSnapshotJson: "[]",
                cartTotal: 100m,
                currency: "USD",
                deviceType: "web",
                source: "storefront",
                CancellationToken.None));

        Assert.Equal("checkout.session.currency", exception.ExpectedComponent);
        Assert.Equal("USD", exception.ExpectedCurrency);
        Assert.Equal("tenant.currency", exception.ActualComponent);
        Assert.Equal("EGP", exception.ActualCurrency);
        _eventPublisher.Verify(
            publisher => publisher.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrResumeSessionAsync_WhenCurrencyMatchesTenant_CreatesSession()
    {
        var session = await _service.CreateOrResumeSessionAsync(
            _tenantId,
            Guid.NewGuid(),
            "session-valid-currency",
            onlineCustomerId: null,
            itemsSnapshotJson: "[]",
            cartTotal: 100m,
            currency: "EGP",
            deviceType: "web",
            source: "storefront",
            CancellationToken.None);

        Assert.Equal("EGP", session.Currency);
        Assert.Equal(CheckoutSessionStatus.Active, session.Status);
    }

    [Fact]
    public void TenantCurrency_ModelRequiresCurrencyWithDatabaseDefault()
    {
        var property = _dbContext.Model.FindEntityType(typeof(Tenant))?.FindProperty(nameof(Tenant.Currency));

        Assert.NotNull(property);
        Assert.False(property!.IsNullable);
        Assert.Equal("EGP", property.GetDefaultValue());
    }

    public void Dispose() => _dbContext.Dispose();

    public ValueTask DisposeAsync() => _dbContext.DisposeAsync();
}
