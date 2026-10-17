using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Storefront;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using Xunit;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontPhoneVerificationReliabilityTests
{
    private const int OtpHashIterations = 100_000;

    [Fact]
    public async Task RequestVerificationAsync_WhenOtpChannelUnavailable_ReturnsFailureAndDoesNotPersistOrPublish()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryCheckoutPhoneOtpStore();
        var eventPublisher = new Mock<IEventPublisher>();
        var featureGate = new Mock<IWhatsAppFeatureGateService>();
        featureGate
            .Setup(x => x.GetActiveAccountAsync(tenantId, PointOfSale.Core.Enums.WhatsAppUseCase.OtpVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WhatsAppAccount?)null);

        var service = CreateService(
            featureGate: featureGate.Object,
            eventPublisher: eventPublisher.Object,
            otpStore: store);

        var result = await service.RequestVerificationAsync(tenantId, "session-1", "01022174803");

        Assert.False(result.Success);
        Assert.Equal("OTP_CHANNEL_UNAVAILABLE", result.Error);
        Assert.Null(await store.GetOtpAsync(tenantId, "session-1", "+201022174803"));
        eventPublisher.Verify(
            x => x.PublishSensitiveAsync(It.IsAny<WhatsAppOtpRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RequestVerificationAsync_WhenChannelAvailable_PersistsOtpAndPublishesDeliverableEvent()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryCheckoutPhoneOtpStore();
        var eventPublisher = new Mock<IEventPublisher>();
        var featureGate = new Mock<IWhatsAppFeatureGateService>();
        featureGate
            .Setup(x => x.GetActiveAccountAsync(tenantId, PointOfSale.Core.Enums.WhatsAppUseCase.OtpVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConnectedAccount(tenantId));

        WhatsAppOtpRequestedEvent? publishedEvent = null;
        eventPublisher
            .Setup(x => x.PublishSensitiveAsync(It.IsAny<WhatsAppOtpRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WhatsAppOtpRequestedEvent, CancellationToken>((evt, _) => publishedEvent = evt)
            .Returns(Task.CompletedTask);

        var service = CreateService(
            featureGate: featureGate.Object,
            eventPublisher: eventPublisher.Object,
            otpStore: store);

        var result = await service.RequestVerificationAsync(tenantId, "session-1", "01022174803");

        Assert.True(result.Success);
        Assert.NotNull(publishedEvent);

        var persistedOtp = await store.GetOtpAsync(tenantId, "session-1", "+201022174803");
        Assert.NotNull(persistedOtp);
        Assert.Equal(CheckoutPhoneOtpStates.Sent, persistedOtp!.State);
        Assert.False(string.IsNullOrWhiteSpace(persistedOtp.CodeHash));
        Assert.False(string.IsNullOrWhiteSpace(persistedOtp.CodeSalt));
        Assert.NotEqual(publishedEvent!.OtpCode, persistedOtp.CodeHash);
        Assert.Equal("+201022174803", publishedEvent.Phone);

        var verification = await service.VerifyOtpAsync(
            tenantId,
            "session-1",
            "+201022174803",
            publishedEvent.OtpCode);
        Assert.True(verification.Success);
    }

    [Fact]
    public async Task RequestVerificationAsync_WhenActiveOtpIsStillCoolingDown_ReusesExistingOtpWithoutRepublishing()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryCheckoutPhoneOtpStore();
        var existingOtp = CreateOtpRecord(
            tenantId,
            "session-1",
            "+201022174803",
            "123456",
            DateTime.UtcNow.AddSeconds(-20),
            DateTime.UtcNow.AddSeconds(-10),
            DateTime.UtcNow.AddMinutes(4));
        await store.SaveOtpAsync(tenantId, "session-1", "+201022174803", existingOtp, TimeSpan.FromMinutes(4));

        var eventPublisher = new Mock<IEventPublisher>();
        var featureGate = new Mock<IWhatsAppFeatureGateService>();
        featureGate
            .Setup(x => x.GetActiveAccountAsync(tenantId, PointOfSale.Core.Enums.WhatsAppUseCase.OtpVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConnectedAccount(tenantId));

        var service = CreateService(
            featureGate: featureGate.Object,
            eventPublisher: eventPublisher.Object,
            otpStore: store);

        var result = await service.RequestVerificationAsync(tenantId, "session-1", "01022174803");

        Assert.True(result.Success);
        Assert.True(result.RetryAfterSeconds > 0);
        eventPublisher.Verify(
            x => x.PublishSensitiveAsync(It.IsAny<WhatsAppOtpRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var persistedOtp = await store.GetOtpAsync(tenantId, "session-1", "+201022174803");
        Assert.NotNull(persistedOtp);
        Assert.Equal(existingOtp.CodeHash, persistedOtp!.CodeHash);
        Assert.Equal(1, persistedOtp.DispatchCount);
    }

    [Fact]
    public async Task RequestVerificationAsync_WhenCooldownElapsed_RotatesOtpAndPersistsReplacement()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryCheckoutPhoneOtpStore();
        var existingOtp = CreateOtpRecord(
            tenantId,
            "session-1",
            "+201022174803",
            "654321",
            DateTime.UtcNow.AddMinutes(-2),
            DateTime.UtcNow.AddSeconds(-90),
            DateTime.UtcNow.AddMinutes(3));
        await store.SaveOtpAsync(tenantId, "session-1", "+201022174803", existingOtp, TimeSpan.FromMinutes(3));

        var eventPublisher = new Mock<IEventPublisher>();
        var featureGate = new Mock<IWhatsAppFeatureGateService>();
        featureGate
            .Setup(x => x.GetActiveAccountAsync(tenantId, PointOfSale.Core.Enums.WhatsAppUseCase.OtpVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConnectedAccount(tenantId));

        WhatsAppOtpRequestedEvent? publishedEvent = null;
        eventPublisher
            .Setup(x => x.PublishSensitiveAsync(It.IsAny<WhatsAppOtpRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WhatsAppOtpRequestedEvent, CancellationToken>((evt, _) => publishedEvent = evt)
            .Returns(Task.CompletedTask);

        var service = CreateService(
            featureGate: featureGate.Object,
            eventPublisher: eventPublisher.Object,
            otpStore: store);

        var result = await service.RequestVerificationAsync(tenantId, "session-1", "01022174803");

        Assert.True(result.Success);
        Assert.NotNull(publishedEvent);
        Assert.NotEqual("654321", publishedEvent!.OtpCode);

        var persistedOtp = await store.GetOtpAsync(tenantId, "session-1", "+201022174803");
        Assert.NotNull(persistedOtp);
        Assert.NotEqual(existingOtp.CodeHash, persistedOtp!.CodeHash);
        Assert.Equal(2, persistedOtp.DispatchCount);

        var verification = await service.VerifyOtpAsync(
            tenantId,
            "session-1",
            "+201022174803",
            publishedEvent.OtpCode);
        Assert.True(verification.Success);
    }

    [Fact]
    public async Task RequestVerificationAsync_WhenSessionRateLimitExceeded_ReturnsRateLimited()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryCheckoutPhoneOtpStore();
        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher
            .Setup(x => x.PublishSensitiveAsync(It.IsAny<WhatsAppOtpRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var featureGate = new Mock<IWhatsAppFeatureGateService>();
        featureGate
            .Setup(x => x.GetActiveAccountAsync(tenantId, PointOfSale.Core.Enums.WhatsAppUseCase.OtpVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConnectedAccount(tenantId));

        var service = CreateService(
            featureGate: featureGate.Object,
            eventPublisher: eventPublisher.Object,
            otpStore: store);

        Assert.True((await service.RequestVerificationAsync(tenantId, "session-rate", "01000000001", "198.51.100.10")).Success);
        Assert.True((await service.RequestVerificationAsync(tenantId, "session-rate", "01000000002", "198.51.100.10")).Success);
        Assert.True((await service.RequestVerificationAsync(tenantId, "session-rate", "01000000003", "198.51.100.10")).Success);

        var blocked = await service.RequestVerificationAsync(
            tenantId,
            "session-rate",
            "01000000004",
            "198.51.100.10");

        Assert.False(blocked.Success);
        Assert.Equal("RATE_LIMIT_EXCEEDED", blocked.Error);
        Assert.True(blocked.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task VerifyOtpAsync_WhenOtpExpired_ReturnsExpired()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryCheckoutPhoneOtpStore();
        await store.SaveOtpAsync(
            tenantId,
            "session-1",
            "+201022174803",
            CreateOtpRecord(
                tenantId,
                "session-1",
                "+201022174803",
                "123456",
                DateTime.UtcNow.AddMinutes(-6),
                DateTime.UtcNow.AddMinutes(-6),
                DateTime.UtcNow.AddSeconds(-5)),
            TimeSpan.FromMinutes(1));

        var service = CreateService(otpStore: store);

        var result = await service.VerifyOtpAsync(tenantId, "session-1", "01022174803", "123456");

        Assert.False(result.Success);
        Assert.Equal("OTP_EXPIRED", result.Error);
        Assert.Null(await store.GetOtpAsync(tenantId, "session-1", "+201022174803"));
    }

    [Fact]
    public async Task VerifyOtpAsync_WhenOtpIncorrect_ReturnsInvalidOtp()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryCheckoutPhoneOtpStore();
        await store.SaveOtpAsync(
            tenantId,
            "session-1",
            "+201022174803",
            CreateOtpRecord(
                tenantId,
                "session-1",
                "+201022174803",
                "123456",
                DateTime.UtcNow.AddSeconds(-10),
                DateTime.UtcNow.AddSeconds(-10),
                DateTime.UtcNow.AddMinutes(4)),
            TimeSpan.FromMinutes(4));

        var service = CreateService(otpStore: store);

        var result = await service.VerifyOtpAsync(tenantId, "session-1", "01022174803", "000000");

        Assert.False(result.Success);
        Assert.Equal("INVALID_OTP", result.Error);

        var persistedOtp = await store.GetOtpAsync(tenantId, "session-1", "+201022174803");
        Assert.NotNull(persistedOtp);
        Assert.False(string.IsNullOrWhiteSpace(persistedOtp!.CodeHash));
        Assert.Equal(1, persistedOtp.VerificationAttempts);
    }

    [Fact]
    public async Task VerifyOtpAsync_WhenWrongCodeRepeatedFiveTimes_InvalidatesOtpAndBlocks()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemoryCheckoutPhoneOtpStore();
        await store.SaveOtpAsync(
            tenantId,
            "session-1",
            "+201022174803",
            CreateOtpRecord(
                tenantId,
                "session-1",
                "+201022174803",
                "123456",
                DateTime.UtcNow.AddSeconds(-10),
                DateTime.UtcNow.AddSeconds(-10),
                DateTime.UtcNow.AddMinutes(4)),
            TimeSpan.FromMinutes(4));

        var service = CreateService(otpStore: store);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var result = await service.VerifyOtpAsync(tenantId, "session-1", "01022174803", "000000");
            Assert.False(result.Success);
            Assert.Equal("INVALID_OTP", result.Error);
        }

        var blocked = await service.VerifyOtpAsync(tenantId, "session-1", "01022174803", "000000");

        Assert.False(blocked.Success);
        Assert.Equal("OTP_ATTEMPTS_EXCEEDED", blocked.Error);
        Assert.Null(await store.GetOtpAsync(tenantId, "session-1", "+201022174803"));
    }

    [Fact]
    public async Task Status_WhenAuthoritativeNormalizerRejectsPhone_DoesNotInventEgyptianVerifiedIdentity()
    {
        var tenantId = Guid.NewGuid();
        var normalizer = new Mock<IPhoneNumberNormalizer>();
        normalizer.Setup(n => n.NormalizeAsync(It.IsAny<string>(), tenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PhoneNumberNormalizationException("Controlled invalid country input"));
        var featureGate = new Mock<IWhatsAppFeatureGateService>();
        featureGate.Setup(g => g.GetActiveAccountAsync(tenantId, PointOfSale.Core.Enums.WhatsAppUseCase.OtpVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConnectedAccount(tenantId));
        var store = new InMemoryCheckoutPhoneOtpStore();
        await store.SetVerifiedAsync(tenantId, "session", "+25512345678", TimeSpan.FromHours(1));
        var service = CreateService(featureGate: featureGate.Object, otpStore: store, normalizer: normalizer.Object);
        var result = await service.CheckVerificationRequiredAsync(tenantId, "session", "5512345678", null);
        Assert.True(result.IsRequired);
        Assert.False(result.IsVerified);
    }

    private static StorefrontPhoneVerificationService CreateService(
        IWhatsAppFeatureGateService? featureGate = null,
        IEventPublisher? eventPublisher = null,
        ICheckoutPhoneOtpStore? otpStore = null,
        IPhoneNumberNormalizer? normalizer = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var dbContext = new ApplicationDbContext(options);

        var phoneNormalizer = new Mock<IPhoneNumberNormalizer>();
        phoneNormalizer
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string phone, Guid _, CancellationToken _) =>
            {
                if (phone.StartsWith("+", StringComparison.Ordinal))
                {
                    return phone;
                }

                return "+2" + new string(phone.Where(char.IsDigit).ToArray());
            });

        return new StorefrontPhoneVerificationService(
            dbContext,
            featureGate ?? Mock.Of<IWhatsAppFeatureGateService>(),
            eventPublisher ?? Mock.Of<IEventPublisher>(),
            normalizer ?? phoneNormalizer.Object,
            Mock.Of<ICartService>(),
            otpStore ?? new InMemoryCheckoutPhoneOtpStore(),
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontPhoneVerificationService>.Instance);
    }

    private static WhatsAppAccount CreateConnectedAccount(Guid tenantId)
    {
        return new WhatsAppAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Enabled = true,
            UseForOtpVerification = true,
            Status = PointOfSale.Core.Enums.WhatsAppAccountStatus.Connected,
            PhoneNumber = "+201000000000",
            DisplayName = "OTP"
        };
    }

    private static CheckoutPhoneOtpRecord CreateOtpRecord(
        Guid tenantId,
        string sessionId,
        string phone,
        string code,
        DateTime createdAtUtc,
        DateTime lastRequestedAtUtc,
        DateTime expiresAtUtc)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var saltBytes = Convert.FromBase64String(salt);
        var value = Encoding.UTF8.GetBytes($"{tenantId:N}\n{sessionId}\n{phone}\n{code}");

        return new CheckoutPhoneOtpRecord
        {
            CodeHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
                value,
                saltBytes,
                OtpHashIterations,
                HashAlgorithmName.SHA256,
                32)),
            CodeSalt = salt,
            State = CheckoutPhoneOtpStates.Sent,
            DeliveryChannel = "whatsapp",
            CreatedAtUtc = createdAtUtc,
            LastRequestedAtUtc = lastRequestedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            DispatchCount = 1,
            MaxVerificationAttempts = 5
        };
    }

    private sealed class InMemoryCheckoutPhoneOtpStore : ICheckoutPhoneOtpStore
    {
        private readonly ConcurrentDictionary<string, CheckoutPhoneOtpRecord> _otpRecords = new();
        private readonly ConcurrentDictionary<string, DateTime> _otpExpirations = new();
        private readonly ConcurrentDictionary<string, byte> _verifiedScopes = new();
        private readonly ConcurrentDictionary<string, (int Count, DateTime ExpiresAtUtc)> _rateLimits = new();
        private readonly ConcurrentDictionary<string, string> _locks = new();

        public Task<CheckoutPhoneOtpRecord?> GetOtpAsync(Guid tenantId, string sessionId, string phone, CancellationToken ct = default)
        {
            var key = BuildOtpKey(tenantId, sessionId, phone);
            if (_otpExpirations.TryGetValue(key, out var expiresAtUtc) && expiresAtUtc <= DateTime.UtcNow)
            {
                _otpRecords.TryRemove(key, out _);
                _otpExpirations.TryRemove(key, out _);
                return Task.FromResult<CheckoutPhoneOtpRecord?>(null);
            }

            return Task.FromResult(
                _otpRecords.TryGetValue(key, out var record)
                    ? Clone(record)
                    : null);
        }

        public Task<bool> SaveOtpAsync(Guid tenantId, string sessionId, string phone, CheckoutPhoneOtpRecord record, TimeSpan ttl, CancellationToken ct = default)
        {
            var key = BuildOtpKey(tenantId, sessionId, phone);
            _otpRecords[key] = Clone(record);
            _otpExpirations[key] = DateTime.UtcNow.Add(ttl);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteOtpAsync(Guid tenantId, string sessionId, string phone, CancellationToken ct = default)
        {
            var key = BuildOtpKey(tenantId, sessionId, phone);
            _otpExpirations.TryRemove(key, out _);
            return Task.FromResult(_otpRecords.TryRemove(key, out _));
        }

        public Task<bool> IsVerifiedAsync(Guid tenantId, string scopeId, string phone, CancellationToken ct = default)
        {
            return Task.FromResult(_verifiedScopes.ContainsKey(BuildVerifiedKey(tenantId, scopeId, phone)));
        }

        public Task<bool> SetVerifiedAsync(Guid tenantId, string scopeId, string phone, TimeSpan ttl, CancellationToken ct = default)
        {
            _verifiedScopes.TryAdd(BuildVerifiedKey(tenantId, scopeId, phone), 0);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteVerifiedAsync(Guid tenantId, string scopeId, string phone, CancellationToken ct = default)
        {
            return Task.FromResult(_verifiedScopes.TryRemove(BuildVerifiedKey(tenantId, scopeId, phone), out _));
        }

        public Task<int> GetRateLimitCountAsync(Guid tenantId, CheckoutOtpRateLimitKey key, CancellationToken ct = default)
        {
            var rateLimitKey = BuildRateLimitKey(tenantId, key);
            if (_rateLimits.TryGetValue(rateLimitKey, out var value))
            {
                if (value.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    _rateLimits.TryRemove(rateLimitKey, out _);
                    return Task.FromResult(0);
                }

                return Task.FromResult(value.Count);
            }

            return Task.FromResult(0);
        }

        public Task<TimeSpan?> GetRateLimitTimeToLiveAsync(Guid tenantId, CheckoutOtpRateLimitKey key, CancellationToken ct = default)
        {
            var rateLimitKey = BuildRateLimitKey(tenantId, key);
            if (!_rateLimits.TryGetValue(rateLimitKey, out var value))
            {
                return Task.FromResult<TimeSpan?>(null);
            }

            var remaining = value.ExpiresAtUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _rateLimits.TryRemove(rateLimitKey, out _);
                return Task.FromResult<TimeSpan?>(null);
            }

            return Task.FromResult<TimeSpan?>(remaining);
        }

        public Task IncrementRateLimitAsync(Guid tenantId, CheckoutOtpRateLimitKey key, TimeSpan ttl, CancellationToken ct = default)
        {
            var rateLimitKey = BuildRateLimitKey(tenantId, key);
            _rateLimits.AddOrUpdate(
                rateLimitKey,
                _ => (1, DateTime.UtcNow.Add(ttl)),
                (_, existing) =>
                {
                    var currentCount = existing.ExpiresAtUtc > DateTime.UtcNow ? existing.Count : 0;
                    var newCount = currentCount + 1;
                    var expiresAt = newCount == 1 ? DateTime.UtcNow.Add(ttl) : existing.ExpiresAtUtc;
                    return (newCount, expiresAt);
                });
            return Task.CompletedTask;
        }

        public Task<bool> ClearRateLimitAsync(Guid tenantId, CheckoutOtpRateLimitKey key, CancellationToken ct = default)
        {
            return Task.FromResult(_rateLimits.TryRemove(BuildRateLimitKey(tenantId, key), out _));
        }

        public Task<bool> TryAcquireLockAsync(Guid tenantId, string sessionId, string phone, string ownerToken, TimeSpan ttl, CancellationToken ct = default)
        {
            var key = BuildLockKey(tenantId, sessionId, phone);
            return Task.FromResult(_locks.TryAdd(key, ownerToken));
        }

        public Task ReleaseLockAsync(Guid tenantId, string sessionId, string phone, string ownerToken, CancellationToken ct = default)
        {
            var key = BuildLockKey(tenantId, sessionId, phone);
            ((ICollection<KeyValuePair<string, string>>)_locks).Remove(
                new KeyValuePair<string, string>(key, ownerToken));
            return Task.CompletedTask;
        }

        private static CheckoutPhoneOtpRecord Clone(CheckoutPhoneOtpRecord source)
        {
            return new CheckoutPhoneOtpRecord
            {
                CodeHash = source.CodeHash,
                CodeSalt = source.CodeSalt,
                State = source.State,
                DeliveryChannel = source.DeliveryChannel,
                CreatedAtUtc = source.CreatedAtUtc,
                ExpiresAtUtc = source.ExpiresAtUtc,
                LastRequestedAtUtc = source.LastRequestedAtUtc,
                VerifiedAtUtc = source.VerifiedAtUtc,
                DispatchCount = source.DispatchCount,
                VerificationAttempts = source.VerificationAttempts,
                MaxVerificationAttempts = source.MaxVerificationAttempts
            };
        }

        private static string BuildOtpKey(Guid tenantId, string sessionId, string phone) =>
            $"{tenantId:N}:{sessionId}:{phone}";

        private static string BuildVerifiedKey(Guid tenantId, string scopeId, string phone) =>
            $"{tenantId:N}:{scopeId}:{phone}";

        private static string BuildRateLimitKey(Guid tenantId, CheckoutOtpRateLimitKey key) =>
            $"{tenantId:N}:{key.Scope}:{key.Value}";

        private static string BuildLockKey(Guid tenantId, string sessionId, string phone) =>
            $"{tenantId:N}:{sessionId}:{phone}";
    }
}
