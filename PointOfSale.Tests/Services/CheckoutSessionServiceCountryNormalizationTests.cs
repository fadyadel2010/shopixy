using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services;
using Xunit;

namespace PointOfSale.Tests.Services;

/// <summary>
/// Tests for CheckoutSession.Country normalization behavior.
/// Ensures all country assignments go through proper normalization to maintain ISO 3166-1 alpha-2 format.
/// </summary>
public class CheckoutSessionServiceCountryNormalizationTests : IDisposable, IAsyncDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly CheckoutSessionService _service;
    private readonly Mock<IEventPublisher> _eventPublisher;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CheckoutSessionServiceCountryNormalizationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"CheckoutSessionCountryTest_{Guid.NewGuid()}")
            .Options;

        _db = new ApplicationDbContext(options);

        _eventPublisher = new Mock<IEventPublisher>();
        _eventPublisher
            .Setup(x => x.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eventPublisher
            .Setup(x => x.PublishSensitiveAsync(It.IsAny<CheckoutSessionProofEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new CheckoutSessionService(
            _db,
            _eventPublisher.Object,
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionService>.Instance);
    }

    [Theory]
    [InlineData("eg", "EG")]
    [InlineData("EG", "EG")]
    [InlineData("us", "US")]
    [InlineData("US", "US")]
    [InlineData("sa", "SA")]
    [InlineData("Egypt", "EG")]
    [InlineData("United States", "US")]
    [InlineData("Saudi Arabia", "SA")]
    [InlineData(" eg ", "EG")]
    [InlineData("  Egypt  ", "EG")]
    [InlineData(" us ", "US")]
    [InlineData("  United States  ", "US")]
    public async Task UpdateCheckoutSessionAsync_NormalizesValidCountryCodes(string inputCountry, string expectedNormalized)
    {
        // Arrange
        var cartId = Guid.NewGuid();
        var sessionId = "test-session";
        
        var session = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CartId = cartId,
            SessionId = sessionId,
            Status = CheckoutSessionStatus.Active,
            ItemsSnapshotJson = "[]",
            CartTotal = 100m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow
        };

        _db.CheckoutSessions.Add(session);
        await _db.SaveChangesAsync();

        var request = new UpdateCheckoutSessionRequest
        {
            TenantId = _tenantId,
            SessionId = session.Id,
            Country = inputCountry
        };

        // Act
        await _service.UpdateSessionAsync(request, CancellationToken.None);

        // Assert
        var updated = await _db.CheckoutSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.Equal(expectedNormalized, updated.Country);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("XYZ")]
    [InlineData("123")]
    [InlineData("Unknown Country")]
    public async Task UpdateCheckoutSessionAsync_SkipsInvalidCountryCodes(string invalidCountry)
    {
        // Arrange
        var cartId = Guid.NewGuid();
        var sessionId = "test-session";
        
        var session = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CartId = cartId,
            SessionId = sessionId,
            Status = CheckoutSessionStatus.Active,
            ItemsSnapshotJson = "[]",
            CartTotal = 100m,
            Currency = "EGP",
            Country = "EG", // Pre-existing valid country
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow
        };

        _db.CheckoutSessions.Add(session);
        await _db.SaveChangesAsync();

        var request = new UpdateCheckoutSessionRequest
        {
            TenantId = _tenantId,
            SessionId = session.Id,
            Country = invalidCountry
        };

        // Act
        await _service.UpdateSessionAsync(request, CancellationToken.None);

        // Assert - Country should remain unchanged
        var updated = await _db.CheckoutSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.Equal("EG", updated.Country); // Should preserve original value
    }

    [Fact]
    public async Task UpdateCheckoutSessionAsync_HandlesNullCountry()
    {
        // Arrange
        var cartId = Guid.NewGuid();
        var sessionId = "test-session";
        
        var session = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CartId = cartId,
            SessionId = sessionId,
            Status = CheckoutSessionStatus.Active,
            ItemsSnapshotJson = "[]",
            CartTotal = 100m,
            Currency = "EGP",
            Country = "EG",
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow
        };

        _db.CheckoutSessions.Add(session);
        await _db.SaveChangesAsync();

        var request = new UpdateCheckoutSessionRequest
        {
            TenantId = _tenantId,
            SessionId = session.Id,
            Country = null
        };

        // Act
        await _service.UpdateSessionAsync(request, CancellationToken.None);

        // Assert - Country should remain unchanged when null is provided
        var updated = await _db.CheckoutSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.Equal("EG", updated.Country);
    }

    [Fact]
    public async Task CreateOrResumeSessionAsync_DoesNotSetCountryByDefault()
    {
        // Arrange
        var cartId = Guid.NewGuid();
        var sessionId = "test-session";

        // Act
        var session = await _service.CreateOrResumeSessionAsync(
            _tenantId,
            cartId,
            sessionId,
            onlineCustomerId: null,
            itemsSnapshotJson: "[]",
            cartTotal: 100m,
            currency: "EGP",
            deviceType: "web",
            source: "storefront",
            CancellationToken.None);

        // Assert
        Assert.Null(session.Country); // Should be null by default
    }

    [Fact]
    public async Task CreateOrResumeSessionAsync_RotatesRecoveryToken_WhenOwnerContextChanges()
    {
        var cartId = Guid.NewGuid();
        var previousRecoveryToken = "previous-recovery-token";

        var existingSession = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CartId = cartId,
            SessionId = "guest-session-a",
            Status = CheckoutSessionStatus.Active,
            RecoveryToken = previousRecoveryToken,
            ItemsSnapshotJson = "[]",
            CartTotal = 100m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            LastActivityAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };

        _db.CheckoutSessions.Add(existingSession);
        await _db.SaveChangesAsync();

        var resumedSession = await _service.CreateOrResumeSessionAsync(
            _tenantId,
            cartId,
            "guest-session-b",
            onlineCustomerId: null,
            itemsSnapshotJson: "[]",
            cartTotal: 125m,
            currency: "EGP",
            deviceType: "web",
            source: "storefront",
            CancellationToken.None);

        var updatedExistingSession = await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleAsync(session => session.Id == existingSession.Id);

        Assert.NotNull(updatedExistingSession);
        Assert.Equal(CheckoutSessionStatus.Abandoned, updatedExistingSession!.Status);
        Assert.NotEqual(previousRecoveryToken, updatedExistingSession.RecoveryToken);
        Assert.Equal(
            resumedSession.Id,
            _db.CheckoutSessions
                .IgnoreQueryFilters()
                .Single(s => s.SessionId == "guest-session-b")
                .Id);
        Assert.Equal("guest-session-b", resumedSession.SessionId);
        Assert.NotEqual(existingSession.Id, resumedSession.Id);
    }

    [Fact]
    public async Task ManualWalletRecoveryFields_AreTracked_And_ProofIsStoredViaDedicatedApi()
    {
        var session = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CartId = Guid.NewGuid(),
            SessionId = "wallet-session",
            Status = CheckoutSessionStatus.Active,
            ItemsSnapshotJson = "[]",
            CartTotal = 100m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow
        };

        _db.CheckoutSessions.Add(session);
        await _db.SaveChangesAsync();

        var request = new UpdateCheckoutSessionRequest
        {
            TenantId = _tenantId,
            SessionId = session.Id,
            PaymentMethodCode = "MANUALWALLET",
            WalletType = "vodafone_cash",
            WalletPhoneNumber = "01011112222"
        };

        await _service.UpdateSessionAsync(request, CancellationToken.None);
        var proofResult = await _service.SetManualWalletProofAsync(
            session.Id,
            _tenantId,
            $"tenants/{_tenantId:D}/manual-wallet-proofs/checkout-sessions/{session.Id:D}/proof.png",
            CancellationToken.None);

        var updated = await _db.CheckoutSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.NotNull(proofResult);
        Assert.Equal("vodafone_cash", updated.WalletType);
        Assert.Equal("01011112222", updated.WalletPhoneNumber);
        Assert.Equal(
            $"tenants/{_tenantId:D}/manual-wallet-proofs/checkout-sessions/{session.Id:D}/proof.png",
            updated.ProofImageUrl);

        // Verify the main update event was published (without proof data - those fields are no longer on the event)
        _eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.Is<CheckoutSessionUpdatedEvent>(evt =>
                    evt.CheckoutSessionId == session.Id),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Verify proof data is published separately via internal event
        _eventPublisher.Verify(
            publisher => publisher.PublishSensitiveAsync(
                It.Is<CheckoutSessionProofEvent>(evt =>
                    evt.CheckoutSessionId == session.Id &&
                    evt.WalletType == "vodafone_cash" &&
                    evt.WalletPhoneNumber == "01011112222" &&
                    evt.ProofImageUrl == $"tenants/{_tenantId:D}/manual-wallet-proofs/checkout-sessions/{session.Id:D}/proof.png"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }
}
