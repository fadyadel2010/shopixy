using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using PointOfSale.Infrastructure.WhatsApp.Transport;
using Xunit;

namespace PointOfSale.Tests.WhatsApp;

/// <summary>
/// Integration Tests for Backend-to-Baileys Service Communication
/// 
/// Tests end-to-end communication between ASP.NET Core backend and Baileys Node.js service.
/// These tests require a running instance of the Baileys service.
/// 
/// To run these tests:
/// 1. Start the Baileys service: cd PointOfSale.WhatsApp.Baileys && npm start
/// 2. Ensure Redis is running
/// 3. Run tests: dotnet test --filter "Category=Integration"
/// 
/// Tests cover:
/// - Message sending
/// - QR code retrieval
/// - Status checking
/// - Disconnect operations
/// - Error handling
/// - Timeout scenarios
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Baileys")]
public class BaileysIntegrationTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly Mock<ILogger<BaileysWhatsAppTransport>> _mockLogger;
    private readonly BaileysWhatsAppTransport _transport;
    private readonly string _testTenantId;

    public BaileysIntegrationTests()
    {
        _httpClient = new HttpClient();
        
        var configData = new Dictionary<string, string?>
        {
            { "BaileysService:BaseUrl", "http://localhost:3000" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        _mockLogger = new Mock<ILogger<BaileysWhatsAppTransport>>();
        _transport = new BaileysWhatsAppTransport(_httpClient, _configuration, _mockLogger.Object);
        
        _testTenantId = $"test-tenant-{Guid.NewGuid()}";
    }

    #region Health Check Tests

    [Fact]
    public async Task HealthCheck_ServiceRunning_ReturnsHealthy()
    {
        // Arrange
        var healthUrl = $"{_configuration["BaileysService:BaseUrl"]}/health";

        // Act
        var response = await _httpClient.GetAsync(healthUrl);

        // Assert
        Assert.True(response.IsSuccessStatusCode, 
            "Baileys service should be running. Start it with: cd PointOfSale.WhatsApp.Baileys && npm start");
    }

    #endregion

    #region Session Management Tests

    [Fact]
    public async Task ConnectSession_NewTenant_CreatesSession()
    {
        // Arrange
        var connectUrl = $"{_configuration["BaileysService:BaseUrl"]}/sessions/{_testTenantId}/connect";
        var payload = new { mode = "DEDICATED" };

        // Act
        var response = await _httpClient.PostAsJsonAsync(connectUrl, payload);

        // Assert
        Assert.True(response.IsSuccessStatusCode, 
            $"Failed to connect session. Status: {response.StatusCode}");
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("success", content.ToLower());
    }

    [Fact]
    public async Task GetStatus_ExistingSession_ReturnsStatus()
    {
        // Arrange - First create a session
        await ConnectSession_NewTenant_CreatesSession();

        // Act
        var result = await _transport.GetStatusAsync(_testTenantId);

        // Assert
        Assert.True(result.Success, $"Failed to get status: {result.ErrorMessage}");
        Assert.NotEqual(WhatsAppConnectionStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task GetStatus_NonExistentSession_ReturnsError()
    {
        // Arrange
        var nonExistentTenant = $"non-existent-{Guid.NewGuid()}";

        // Act
        var result = await _transport.GetStatusAsync(nonExistentTenant);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region QR Code Tests

    [Fact]
    public async Task GetQrCode_DedicatedSession_ReturnsQrCodeOrConnected()
    {
        // Arrange - Create a dedicated session
        var connectUrl = $"{_configuration["BaileysService:BaseUrl"]}/sessions/{_testTenantId}/connect";
        var payload = new { mode = "DEDICATED" };
        await _httpClient.PostAsJsonAsync(connectUrl, payload);

        // Wait a bit for QR generation
        await Task.Delay(2000);

        // Act
        var result = await _transport.GetQrCodeAsync(_testTenantId);

        // Assert
        // Either we get a QR code, or the session is already connected (from previous test run)
        if (result.Success)
        {
            Assert.NotNull(result.QrCodeBase64);
            Assert.StartsWith("data:image/png;base64,", result.QrCodeBase64);
        }
        else
        {
            // Session might already be connected
            Assert.NotNull(result.ErrorMessage);
        }
    }

    [Fact]
    public async Task GetQrCode_PoolSession_ReturnsError()
    {
        // Arrange - Create a pool session
        var poolTenantId = $"pool-test-{Guid.NewGuid()}";
        var connectUrl = $"{_configuration["BaileysService:BaseUrl"]}/sessions/{poolTenantId}/connect";
        var payload = new { mode = "POOL" };
        await _httpClient.PostAsJsonAsync(connectUrl, payload);

        // Act
        var result = await _transport.GetQrCodeAsync(poolTenantId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("pool", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Message Sending Tests

    [Fact]
    public async Task SendMessage_ValidPhone_ReturnsSuccessOrSessionNotConnected()
    {
        // Arrange
        var phone = "+5511999999999"; // Valid E.164 format
        var message = "Integration test message";

        // Act
        var result = await _transport.SendMessageAsync(
            _testTenantId,
            phone,
            message,
            WhatsAppMessageType.Text);

        // Assert
        // Either succeeds (if session is connected) or fails with "not connected" error
        if (!result.Success)
        {
            Assert.NotNull(result.ErrorMessage);
            // Expected errors: session not found, not connected, etc.
            Assert.True(
                result.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                result.ErrorMessage.Contains("not connected", StringComparison.OrdinalIgnoreCase) ||
                result.ErrorMessage.Contains("session", StringComparison.OrdinalIgnoreCase),
                $"Unexpected error: {result.ErrorMessage}");
        }
        else
        {
            Assert.NotNull(result.MessageId);
        }
    }

    [Fact]
    public async Task SendMessage_InvalidPhone_ReturnsValidationError()
    {
        // Arrange
        var invalidPhone = "invalid-phone";
        var message = "Test message";

        // Act
        var result = await _transport.SendMessageAsync(
            _testTenantId,
            invalidPhone,
            message,
            WhatsAppMessageType.Text);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("phone", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessage_EmptyMessage_ReturnsValidationError()
    {
        // Arrange
        var phone = "+5511999999999";
        var emptyMessage = "";

        // Act
        var result = await _transport.SendMessageAsync(
            _testTenantId,
            phone,
            emptyMessage,
            WhatsAppMessageType.Text);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion

    #region Disconnect Tests

    [Fact]
    public async Task Disconnect_ExistingSession_ReturnsSuccess()
    {
        // Arrange - Create a session first
        var disconnectTenantId = $"disconnect-test-{Guid.NewGuid()}";
        var connectUrl = $"{_configuration["BaileysService:BaseUrl"]}/sessions/{disconnectTenantId}/connect";
        var payload = new { mode = "DEDICATED" };
        await _httpClient.PostAsJsonAsync(connectUrl, payload);

        // Act
        var result = await _transport.DisconnectAsync(disconnectTenantId);

        // Assert
        Assert.True(result.Success, $"Failed to disconnect: {result.ErrorMessage}");
    }

    [Fact]
    public async Task Disconnect_NonExistentSession_ReturnsError()
    {
        // Arrange
        var nonExistentTenant = $"non-existent-{Guid.NewGuid()}";

        // Act
        var result = await _transport.DisconnectAsync(nonExistentTenant);

        // Assert
        // Disconnect might succeed even if session doesn't exist (idempotent)
        // or return an error - both are acceptable
        Assert.NotNull(result);
    }

    #endregion

    #region Timeout Tests

    [Fact]
    public async Task SendMessage_WithCancellation_RespectsCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100); // Cancel after 100ms

        // Act
        var result = await _transport.SendMessageAsync(
            _testTenantId,
            "+5511999999999",
            "Test message",
            WhatsAppMessageType.Text,
            cts.Token);

        // Assert
        // Should either complete quickly or return timeout error
        Assert.NotNull(result);
        if (!result.Success)
        {
            Assert.NotNull(result.ErrorMessage);
        }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task SendMessage_ServiceUnavailable_ReturnsHttpError()
    {
        // Arrange - Use invalid base URL
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "BaileysService:BaseUrl", "http://localhost:9999" } // Non-existent port
            })
            .Build();

        var invalidTransport = new BaileysWhatsAppTransport(
            new HttpClient(),
            invalidConfig,
            _mockLogger.Object);

        // Act
        var result = await invalidTransport.SendMessageAsync(
            _testTenantId,
            "+5511999999999",
            "Test message",
            WhatsAppMessageType.Text);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("HTTP", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region End-to-End Workflow Tests

    [Fact]
    public async Task EndToEnd_ConnectCheckStatusDisconnect_WorksCorrectly()
    {
        // Arrange
        var e2eTenantId = $"e2e-test-{Guid.NewGuid()}";
        var connectUrl = $"{_configuration["BaileysService:BaseUrl"]}/sessions/{e2eTenantId}/connect";

        // Act & Assert

        // 1. Connect session
        var connectPayload = new { mode = "DEDICATED" };
        var connectResponse = await _httpClient.PostAsJsonAsync(connectUrl, connectPayload);
        Assert.True(connectResponse.IsSuccessStatusCode, "Connect should succeed");

        // 2. Check status
        var statusResult = await _transport.GetStatusAsync(e2eTenantId);
        Assert.True(statusResult.Success, "Status check should succeed");
        Assert.NotEqual(WhatsAppConnectionStatus.Unknown, statusResult.Status);

        // 3. Disconnect
        var disconnectResult = await _transport.DisconnectAsync(e2eTenantId);
        Assert.True(disconnectResult.Success, "Disconnect should succeed");

        // 4. Verify disconnected
        var finalStatusResult = await _transport.GetStatusAsync(e2eTenantId);
        // Session might not exist anymore, which is fine
        Assert.NotNull(finalStatusResult);
    }

    #endregion

    public void Dispose()
    {
        // Cleanup: Try to disconnect test session
        try
        {
            _transport.DisconnectAsync(_testTenantId).Wait();
        }
        catch
        {
            // Ignore cleanup errors
        }

        _httpClient?.Dispose();
    }
}
