using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using PointOfSale.Infrastructure.WhatsApp.Transport;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PointOfSale.Tests.WhatsApp;

/// <summary>
/// Unit Tests for BaileysWhatsAppTransport
/// 
/// Tests specific scenarios and edge cases for the Baileys HTTP transport:
/// - Successful operations
/// - Error handling
/// - Timeout scenarios
/// - Cancellation token propagation
/// - Response parsing
/// </summary>
public class BaileysTransportTests
{
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly Mock<ILogger<BaileysWhatsAppTransport>> _mockLogger;

    public BaileysTransportTests()
    {
        _mockHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHandler.Object);
        
        var configData = new Dictionary<string, string?>
        {
            { "BaileysService:BaseUrl", "http://localhost:3000" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        _mockLogger = new Mock<ILogger<BaileysWhatsAppTransport>>();
    }

    #region SendMessageAsync Tests

    [Fact]
    public async Task SendMessageAsync_SuccessfulResponse_ReturnsSuccessWithMessageId()
    {
        // Arrange
        var tenantId = "tenant-123";
        var messageId = "msg-456";
        var response = new { success = true, messageId };

        SetupHttpResponse(HttpStatusCode.OK, response);

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.SendMessageAsync(
            tenantId,
            "+5511999999999",
            "Test message",
            WhatsAppMessageType.Text);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(messageId, result.MessageId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task SendMessageAsync_ErrorResponse_ReturnsFailureWithError()
    {
        // Arrange
        var tenantId = "tenant-123";
        var errorMessage = "Session not found";
        var response = new { success = false, error = errorMessage };

        SetupHttpResponse(HttpStatusCode.BadRequest, response);

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.SendMessageAsync(
            tenantId,
            "+5511999999999",
            "Test message",
            WhatsAppMessageType.Text);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.MessageId);
        Assert.Contains(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task SendMessageAsync_Timeout_ReturnsTimeoutError()
    {
        // Arrange
        var tenantId = "tenant-123";

        _mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timeout"));

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.SendMessageAsync(
            tenantId,
            "+5511999999999",
            "Test message",
            WhatsAppMessageType.Text);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessageAsync_HttpRequestException_ReturnsHttpError()
    {
        // Arrange
        var tenantId = "tenant-123";

        _mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.SendMessageAsync(
            tenantId,
            "+5511999999999",
            "Test message",
            WhatsAppMessageType.Text);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("HTTP request failed", result.ErrorMessage);
    }

    [Fact]
    public async Task SendMessageAsync_CancellationToken_PropagatesCancellation()
    {
        // Arrange
        var tenantId = "tenant-123";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.SendMessageAsync(
            tenantId,
            "+5511999999999",
            "Test message",
            WhatsAppMessageType.Text,
            cts.Token);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion

    #region GetQrCodeAsync Tests

    [Fact]
    public async Task GetQrCodeAsync_SuccessfulResponse_ReturnsSuccessWithQrCode()
    {
        // Arrange
        var tenantId = "tenant-123";
        var qrCode = "data:image/png;base64,iVBORw0KGgoAAAANS...";
        var response = new { success = true, qrCode };

        SetupHttpResponse(HttpStatusCode.OK, response);

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.GetQrCodeAsync(tenantId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(qrCode, result.QrCodeBase64);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task GetQrCodeAsync_ErrorResponse_ReturnsFailureWithError()
    {
        // Arrange
        var tenantId = "tenant-123";
        var errorMessage = "QR code not available";
        var response = new { success = false, error = errorMessage };

        SetupHttpResponse(HttpStatusCode.NotFound, response);

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.GetQrCodeAsync(tenantId);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.QrCodeBase64);
        Assert.Contains(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task GetQrCodeAsync_Timeout_ReturnsTimeoutError()
    {
        // Arrange
        var tenantId = "tenant-123";

        _mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.GetQrCodeAsync(tenantId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region GetStatusAsync Tests

    [Theory]
    [InlineData("connected", WhatsAppConnectionStatus.Connected)]
    [InlineData("disconnected", WhatsAppConnectionStatus.Disconnected)]
    [InlineData("qr_pending", WhatsAppConnectionStatus.QrPending)]
    [InlineData("connecting", WhatsAppConnectionStatus.QrPending)]
    [InlineData("unknown", WhatsAppConnectionStatus.Unknown)]
    public async Task GetStatusAsync_VariousStatuses_MapsCorrectly(string status, WhatsAppConnectionStatus expected)
    {
        // Arrange
        var tenantId = "tenant-123";
        var response = new { success = true, status };

        SetupHttpResponse(HttpStatusCode.OK, response);

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.GetStatusAsync(tenantId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task GetStatusAsync_ErrorResponse_ReturnsUnknownStatus()
    {
        // Arrange
        var tenantId = "tenant-123";
        var errorMessage = "Session not found";
        var response = new { success = false, error = errorMessage };

        SetupHttpResponse(HttpStatusCode.NotFound, response);

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.GetStatusAsync(tenantId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(WhatsAppConnectionStatus.Unknown, result.Status);
        Assert.Contains(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task GetStatusAsync_Timeout_ReturnsUnknownStatusWithError()
    {
        // Arrange
        var tenantId = "tenant-123";

        _mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.GetStatusAsync(tenantId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(WhatsAppConnectionStatus.Unknown, result.Status);
        Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region DisconnectAsync Tests

    [Fact]
    public async Task DisconnectAsync_SuccessfulResponse_ReturnsSuccess()
    {
        // Arrange
        var tenantId = "tenant-123";
        var response = new { success = true };

        SetupHttpResponse(HttpStatusCode.OK, response);

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.DisconnectAsync(tenantId);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task DisconnectAsync_ErrorResponse_ReturnsFailureWithError()
    {
        // Arrange
        var tenantId = "tenant-123";
        var errorMessage = "Session not found";
        var response = new { success = false, error = errorMessage };

        SetupHttpResponse(HttpStatusCode.NotFound, response);

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.DisconnectAsync(tenantId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task DisconnectAsync_Timeout_ReturnsTimeoutError()
    {
        // Arrange
        var tenantId = "tenant-123";

        _mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Act
        var result = await transport.DisconnectAsync(tenantId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Constructor_MissingBaseUrl_ThrowsException()
    {
        // Arrange
        var emptyConfig = new ConfigurationBuilder().Build();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new BaileysWhatsAppTransport(_httpClient, emptyConfig, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ValidConfiguration_InitializesSuccessfully()
    {
        // Act
        var transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);

        // Assert
        Assert.NotNull(transport);
    }

    #endregion

    #region Helper Methods

    private void SetupHttpResponse<T>(HttpStatusCode statusCode, T responseObject)
    {
        var json = JsonSerializer.Serialize(responseObject);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = content
            });
    }

    #endregion
}
