using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using PointOfSale.Infrastructure.WhatsApp.Services;
using Xunit;

namespace PointOfSale.Tests.WhatsApp;

/// <summary>
/// Unit tests for WhatsAppMessageDispatcher after refactoring to use IWhatsAppTransport.
/// Validates: Requirements 1.3, 8.1, 8.4, 8.7 - Dispatcher uses transport interface and preserves business logic
/// </summary>
public class WhatsAppMessageDispatcherTests
{
    private readonly Mock<IWhatsAppMessageLogService> _mockLogService;
    private readonly Mock<IWhatsAppTransport> _mockTransport;
    private readonly Mock<IWhatsAppRateLimiter> _mockRateLimiter;
    private readonly Mock<IWhatsAppFeatureGateService> _mockFeatureGate;
    private readonly Mock<IEventPublisher> _mockEventPublisher;
    private readonly Mock<IWhatsAppCircuitBreaker> _mockCircuitBreaker;
    private readonly Mock<IWhatsAppKillSwitch> _mockKillSwitch;
    private readonly Mock<IPhoneNumberNormalizer> _mockPhoneNormalizer;
    private readonly Mock<ILogger<WhatsAppMessageDispatcher>> _mockLogger;
    private readonly WhatsAppMessageDispatcher _dispatcher;

    public WhatsAppMessageDispatcherTests()
    {
        _mockLogService = new Mock<IWhatsAppMessageLogService>();
        _mockTransport = new Mock<IWhatsAppTransport>();
        _mockRateLimiter = new Mock<IWhatsAppRateLimiter>();
        _mockFeatureGate = new Mock<IWhatsAppFeatureGateService>();
        _mockEventPublisher = new Mock<IEventPublisher>();
        _mockCircuitBreaker = new Mock<IWhatsAppCircuitBreaker>();
        _mockKillSwitch = new Mock<IWhatsAppKillSwitch>();
        _mockPhoneNormalizer = new Mock<IPhoneNumberNormalizer>();
        _mockLogger = new Mock<ILogger<WhatsAppMessageDispatcher>>();

        _dispatcher = new WhatsAppMessageDispatcher(
            _mockLogService.Object,
            _mockTransport.Object,
            _mockRateLimiter.Object,
            _mockFeatureGate.Object,
            _mockEventPublisher.Object,
            _mockCircuitBreaker.Object,
            _mockKillSwitch.Object,
            _mockPhoneNormalizer.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Test: Dispatcher uses mocked transport for message sending
    /// Validates: Requirement 1.3 - Dispatcher depends only on IWhatsAppTransport interface
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ShouldUseTransportInterface()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var messageLogId = "test_log_123";
        var normalizedPhone = "+1234567890";

        var messageLog = CreateMessageLog(messageLogId, tenantId, accountId, "+1234567890");

        _mockLogService
            .Setup(x => x.GetByIdAsync(messageLogId))
            .ReturnsAsync(messageLog);

        _mockKillSwitch
            .Setup(x => x.IsKilledAsync(tenantId))
            .ReturnsAsync(false);

        _mockCircuitBreaker
            .Setup(x => x.IsOpenAsync(accountId))
            .ReturnsAsync(false);

        _mockPhoneNormalizer
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(normalizedPhone);

        _mockRateLimiter
            .Setup(x => x.CanSendMessageAsync(tenantId, accountId, normalizedPhone, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockRateLimiter
            .Setup(x => x.GetRandomDelay())
            .Returns(TimeSpan.Zero);

        _mockTransport
            .Setup(x => x.SendMessageAsync(
                tenantId.ToString(),
                normalizedPhone,
                It.IsAny<string>(),
                WhatsAppMessageType.Text,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult { Success = true, MessageId = "msg_123" });

        // Act
        await _dispatcher.DispatchAsync(messageLogId);

        // Assert - Verify transport was called with correct parameters
        _mockTransport.Verify(
            x => x.SendMessageAsync(
                tenantId.ToString(),
                normalizedPhone,
                It.IsAny<string>(),
                WhatsAppMessageType.Text,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Test: OTP logic is preserved after refactoring
    /// Validates: Requirement 8.1 - OTP logic unchanged
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WithOtpOverride_ShouldPreserveOtpLogic()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var messageLogId = "otp_log_123";
        var otpMessage = "Your OTP is 123456";
        var normalizedPhone = "+1234567890";

        var messageLog = CreateMessageLog(messageLogId, tenantId, accountId, "+1234567890");

        _mockLogService
            .Setup(x => x.GetByIdAsync(messageLogId))
            .ReturnsAsync(messageLog);

        _mockKillSwitch
            .Setup(x => x.IsKilledAsync(tenantId))
            .ReturnsAsync(false);

        _mockCircuitBreaker
            .Setup(x => x.IsOpenAsync(accountId))
            .ReturnsAsync(false);

        _mockPhoneNormalizer
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(normalizedPhone);

        _mockRateLimiter
            .Setup(x => x.CanSendMessageAsync(tenantId, accountId, normalizedPhone, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockRateLimiter
            .Setup(x => x.GetRandomDelay())
            .Returns(TimeSpan.Zero);

        _mockTransport
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                otpMessage, // Should receive the OTP override message
                WhatsAppMessageType.Text,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult { Success = true, MessageId = "msg_123" });

        // Act - Pass OTP message as override
        await _dispatcher.DispatchAsync(messageLogId, otpMessage);

        // Assert - Verify OTP message was sent (not from payload)
        _mockTransport.Verify(
            x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                otpMessage,
                WhatsAppMessageType.Text,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Test: Circuit breaker behavior is preserved
    /// Validates: Requirement 8.7 - Circuit breaker semantics unchanged
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WhenCircuitBreakerOpen_ShouldNotSendMessage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var messageLogId = "test_log_123";

        var messageLog = CreateMessageLog(messageLogId, tenantId, accountId, "+1234567890");

        _mockLogService
            .Setup(x => x.GetByIdAsync(messageLogId))
            .ReturnsAsync(messageLog);

        _mockKillSwitch
            .Setup(x => x.IsKilledAsync(tenantId))
            .ReturnsAsync(false);

        // Circuit breaker is OPEN
        _mockCircuitBreaker
            .Setup(x => x.IsOpenAsync(accountId))
            .ReturnsAsync(true);

        // Act
        await _dispatcher.DispatchAsync(messageLogId);

        // Assert - Transport should NOT be called when circuit breaker is open
        _mockTransport.Verify(
            x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<WhatsAppMessageType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify message was marked as FAILED
        _mockLogService.Verify(
            x => x.UpdateStatusAsync(messageLogId, "FAILED", "Circuit breaker open"),
            Times.Once);
    }

    /// <summary>
    /// Test: Kill switch behavior is preserved
    /// Validates: Requirement 8.4 - Feature gate evaluation unchanged
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WhenKillSwitchActive_ShouldSkipMessage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var messageLogId = "test_log_123";

        var messageLog = CreateMessageLog(messageLogId, tenantId, accountId, "+1234567890");

        _mockLogService
            .Setup(x => x.GetByIdAsync(messageLogId))
            .ReturnsAsync(messageLog);

        // Kill switch is ACTIVE
        _mockKillSwitch
            .Setup(x => x.IsKilledAsync(tenantId))
            .ReturnsAsync(true);

        // Act
        await _dispatcher.DispatchAsync(messageLogId);

        // Assert - Transport should NOT be called when kill switch is active
        _mockTransport.Verify(
            x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<WhatsAppMessageType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify message was marked as SKIPPED
        _mockLogService.Verify(
            x => x.UpdateStatusAsync(messageLogId, "SKIPPED", "Kill switch active"),
            Times.Once);
    }

    /// <summary>
    /// Test: Rate limiting behavior is preserved
    /// Validates: Requirement 8.4 - Feature gate evaluation unchanged
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WhenRateLimitExceeded_ShouldFailMessage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var messageLogId = "test_log_123";
        var normalizedPhone = "+1234567890";

        var messageLog = CreateMessageLog(messageLogId, tenantId, accountId, "+1234567890");

        _mockLogService
            .Setup(x => x.GetByIdAsync(messageLogId))
            .ReturnsAsync(messageLog);

        _mockKillSwitch
            .Setup(x => x.IsKilledAsync(tenantId))
            .ReturnsAsync(false);

        _mockCircuitBreaker
            .Setup(x => x.IsOpenAsync(accountId))
            .ReturnsAsync(false);

        _mockPhoneNormalizer
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(normalizedPhone);

        // Rate limit EXCEEDED
        _mockRateLimiter
            .Setup(x => x.CanSendMessageAsync(tenantId, accountId, normalizedPhone, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _dispatcher.DispatchAsync(messageLogId);

        // Assert - Transport should NOT be called when rate limited
        _mockTransport.Verify(
            x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<WhatsAppMessageType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify message was marked as FAILED with rate limit reason
        _mockLogService.Verify(
            x => x.UpdateStatusAsync(messageLogId, "FAILED", "Rate Limited"),
            Times.Once);

        // Verify failure event was published
        _mockEventPublisher.Verify(
            x => x.PublishAsync(
                It.Is<WhatsAppMessageFailedEvent>(e => e.FailureReason == "Rate Limited"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Test: Successful message dispatch records success in circuit breaker
    /// Validates: Requirement 8.7 - Circuit breaker semantics unchanged
    /// </summary>
    [Fact]
    public async Task DispatchAsync_OnSuccess_ShouldRecordCircuitBreakerSuccess()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var messageLogId = "test_log_123";
        var normalizedPhone = "+1234567890";

        var messageLog = CreateMessageLog(messageLogId, tenantId, accountId, "+1234567890");

        _mockLogService
            .Setup(x => x.GetByIdAsync(messageLogId))
            .ReturnsAsync(messageLog);

        _mockKillSwitch
            .Setup(x => x.IsKilledAsync(tenantId))
            .ReturnsAsync(false);

        _mockCircuitBreaker
            .Setup(x => x.IsOpenAsync(accountId))
            .ReturnsAsync(false);

        _mockPhoneNormalizer
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(normalizedPhone);

        _mockRateLimiter
            .Setup(x => x.CanSendMessageAsync(tenantId, accountId, normalizedPhone, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockRateLimiter
            .Setup(x => x.GetRandomDelay())
            .Returns(TimeSpan.Zero);

        _mockTransport
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                WhatsAppMessageType.Text,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult { Success = true, MessageId = "msg_123" });

        // Act
        await _dispatcher.DispatchAsync(messageLogId);

        // Assert - Circuit breaker should record success
        _mockCircuitBreaker.Verify(
            x => x.RecordSuccessAsync(accountId),
            Times.Once);

        // Verify success event was published
        _mockEventPublisher.Verify(
            x => x.PublishAsync(
                It.Is<WhatsAppMessageSentEvent>(e => e.MessageLogId == messageLogId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Test: Failed message dispatch records failure in circuit breaker
    /// Validates: Requirement 8.7 - Circuit breaker semantics unchanged
    /// </summary>
    [Fact]
    public async Task DispatchAsync_OnFailure_ShouldRecordCircuitBreakerFailure()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var messageLogId = "test_log_123";
        var normalizedPhone = "+1234567890";

        var messageLog = CreateMessageLog(messageLogId, tenantId, accountId, "+1234567890");

        _mockLogService
            .Setup(x => x.GetByIdAsync(messageLogId))
            .ReturnsAsync(messageLog);

        _mockKillSwitch
            .Setup(x => x.IsKilledAsync(tenantId))
            .ReturnsAsync(false);

        _mockCircuitBreaker
            .Setup(x => x.IsOpenAsync(accountId))
            .ReturnsAsync(false);

        _mockPhoneNormalizer
            .Setup(x => x.NormalizeAsync(It.IsAny<string>(), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(normalizedPhone);

        _mockRateLimiter
            .Setup(x => x.CanSendMessageAsync(tenantId, accountId, normalizedPhone, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockRateLimiter
            .Setup(x => x.GetRandomDelay())
            .Returns(TimeSpan.Zero);

        // Transport returns failure
        _mockTransport
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                WhatsAppMessageType.Text,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult { Success = false, ErrorMessage = "Connection timeout" });

        // Act
        await _dispatcher.DispatchAsync(messageLogId);

        // Assert - Circuit breaker should record failure
        _mockCircuitBreaker.Verify(
            x => x.RecordFailureAsync(accountId),
            Times.Once);

        // Verify failure event was published
        _mockEventPublisher.Verify(
            x => x.PublishAsync(
                It.Is<WhatsAppMessageFailedEvent>(e => e.FailureReason == "Connection timeout"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Helper method to create a test message log document
    /// </summary>
    private WhatsAppMessageLogDocument CreateMessageLog(
        string id,
        Guid tenantId,
        Guid accountId,
        string targetPhone)
    {
        var payload = new BsonDocument
        {
            { "text", "Test message content" }
        };

        return new WhatsAppMessageLogDocument
        {
            Id = id,
            TenantId = tenantId.ToString(),
            AccountId = accountId.ToString(),
            TargetPhone = targetPhone,
            MessageType = "OrderConfirmation",
            Status = "QUEUED",
            PayloadSnapshot = payload,
            SendAttempts = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
