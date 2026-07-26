using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using PointOfSale.Infrastructure.WhatsApp.Transport;
using Xunit;

namespace PointOfSale.Tests.WhatsApp;

/// <summary>
/// Tests for PlaywrightTransport.
/// Validates: Requirements 1.4, 1.5 - Transport abstraction and behavioral equivalence
/// </summary>
public class PlaywrightTransportTests
{
    /// <summary>
    /// Property 1: Playwright Transport Behavioral Equivalence
    /// 
    /// For any valid message send request, when sent through both the legacy Playwright engine
    /// and the new PlaywrightTransport wrapper, both should produce equivalent results.
    /// 
    /// Validates: Requirements 1.5
    /// </summary>
    [Theory]
    [InlineData(true, "msg_123", null)]
    [InlineData(false, null, "Engine error")]
    [InlineData(false, null, "Connection timeout")]
    public async Task SendMessage_ShouldPreserveEngineBehavior(bool success, string? messageId, string? error)
    {
        // Arrange
        var mockEngine = new Mock<IWhatsAppWebEngine>();
        var mockAccountService = new Mock<IWhatsAppAccountService>();
        var mockLogger = new Mock<ILogger<PlaywrightTransport>>();

        var tenantId = Guid.NewGuid();
        var account = new WhatsAppAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Status = WhatsAppAccountStatus.Connected,
            Enabled = true,
            UseForOrderConfirmation = true
        };

        mockAccountService
            .Setup(x => x.GetActiveAccountForUseCaseAsync(
                tenantId,
                WhatsAppUseCase.OrderConfirmation,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var engineResult = new SendResult(success, messageId, error);
        mockEngine
            .Setup(x => x.SendMessageAsync(
                account.Id,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(engineResult);

        var transport = new PlaywrightTransport(
            mockEngine.Object,
            mockAccountService.Object,
            mockLogger.Object);

        // Act
        var transportResult = await transport.SendMessageAsync(
            tenantId.ToString(),
            "+1234567890",
            "Test message",
            WhatsAppMessageType.Text,
            CancellationToken.None);

        // Assert - Transport result should match engine result exactly
        Assert.Equal(engineResult.Success, transportResult.Success);
        Assert.Equal(engineResult.MessageId, transportResult.MessageId);
        Assert.Equal(engineResult.Error, transportResult.ErrorMessage);
    }

    /// <summary>
    /// Property 2: Transport Async Non-Blocking
    /// 
    /// For any transport operation, the operation should return a Task immediately without blocking.
    /// 
    /// Validates: Requirements 1.4
    /// </summary>
    [Fact]
    public async Task TransportOperations_ShouldBeAsync()
    {
        // Arrange
        var mockEngine = new Mock<IWhatsAppWebEngine>();
        var mockAccountService = new Mock<IWhatsAppAccountService>();
        var mockLogger = new Mock<ILogger<PlaywrightTransport>>();

        var tenantId = Guid.NewGuid();
        var account = new WhatsAppAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Status = WhatsAppAccountStatus.Connected,
            Enabled = true,
            UseForOrderConfirmation = true
        };

        mockAccountService
            .Setup(x => x.GetActiveAccountForUseCaseAsync(
                It.IsAny<Guid>(),
                It.IsAny<WhatsAppUseCase>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Setup engine with async delay to simulate real async work
        mockEngine
            .Setup(x => x.SendMessageAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(10);
                return new SendResult(true, "msg_123", null);
            });

        mockEngine
            .Setup(x => x.GenerateQrCodeAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(10);
                return new QrCodeResult(true, "qr_base64", null, 60);
            });

        mockEngine
            .Setup(x => x.CheckConnectionAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(10);
                return new ConnectionStatus(true, WhatsAppAccountStatus.Connected, DateTime.UtcNow);
            });

        mockEngine
            .Setup(x => x.DisconnectAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () => await Task.Delay(10));

        var transport = new PlaywrightTransport(
            mockEngine.Object,
            mockAccountService.Object,
            mockLogger.Object);

        // Act - All operations should return Task immediately (not blocking)
        var sendTask = transport.SendMessageAsync(tenantId.ToString(), "+1234567890", "test", WhatsAppMessageType.Text);
        var qrTask = transport.GetQrCodeAsync(tenantId.ToString());
        var statusTask = transport.GetStatusAsync(tenantId.ToString());
        var disconnectTask = transport.DisconnectAsync(tenantId.ToString());

        // Assert - Verify tasks are created (not blocking)
        Assert.NotNull(sendTask);
        Assert.NotNull(qrTask);
        Assert.NotNull(statusTask);
        Assert.NotNull(disconnectTask);

        // Await all tasks
        await Task.WhenAll(sendTask, qrTask, statusTask, disconnectTask);

        // Verify all completed successfully
        Assert.True(sendTask.IsCompletedSuccessfully);
        Assert.True(qrTask.IsCompletedSuccessfully);
        Assert.True(statusTask.IsCompletedSuccessfully);
        Assert.True(disconnectTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task GetQrCode_ShouldPreserveEngineBehavior()
    {
        // Arrange
        var mockEngine = new Mock<IWhatsAppWebEngine>();
        var mockAccountService = new Mock<IWhatsAppAccountService>();
        var mockLogger = new Mock<ILogger<PlaywrightTransport>>();

        var tenantId = Guid.NewGuid();
        var account = new WhatsAppAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Status = WhatsAppAccountStatus.PendingQR,
            Enabled = true,
            UseForOrderConfirmation = true
        };

        mockAccountService
            .Setup(x => x.GetActiveAccountForUseCaseAsync(
                tenantId,
                WhatsAppUseCase.OrderConfirmation,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var engineResult = new QrCodeResult(true, "base64_qr_code", null, 60);
        mockEngine
            .Setup(x => x.GenerateQrCodeAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(engineResult);

        var transport = new PlaywrightTransport(
            mockEngine.Object,
            mockAccountService.Object,
            mockLogger.Object);

        // Act
        var transportResult = await transport.GetQrCodeAsync(tenantId.ToString());

        // Assert
        Assert.Equal(engineResult.Success, transportResult.Success);
        Assert.Equal(engineResult.QrImageBase64, transportResult.QrCodeBase64);
        Assert.Equal(engineResult.Error, transportResult.ErrorMessage);
    }

    [Fact]
    public async Task GetStatus_ShouldMapConnectionStatusCorrectly()
    {
        // Arrange
        var mockEngine = new Mock<IWhatsAppWebEngine>();
        var mockAccountService = new Mock<IWhatsAppAccountService>();
        var mockLogger = new Mock<ILogger<PlaywrightTransport>>();

        var tenantId = Guid.NewGuid();
        var account = new WhatsAppAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Status = WhatsAppAccountStatus.Connected,
            Enabled = true,
            UseForOrderConfirmation = true
        };

        mockAccountService
            .Setup(x => x.GetActiveAccountForUseCaseAsync(
                tenantId,
                WhatsAppUseCase.OrderConfirmation,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var engineResult = new ConnectionStatus(true, WhatsAppAccountStatus.Connected, DateTime.UtcNow);
        mockEngine
            .Setup(x => x.CheckConnectionAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(engineResult);

        var transport = new PlaywrightTransport(
            mockEngine.Object,
            mockAccountService.Object,
            mockLogger.Object);

        // Act
        var transportResult = await transport.GetStatusAsync(tenantId.ToString());

        // Assert
        Assert.True(transportResult.Success);
        Assert.Equal(WhatsAppConnectionStatus.Connected, transportResult.Status);
    }
}
