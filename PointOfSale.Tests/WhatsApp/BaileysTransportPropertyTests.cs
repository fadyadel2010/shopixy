using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using PointOfSale.Infrastructure.WhatsApp.Transport;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PointOfSale.Tests.WhatsApp;

/// <summary>
/// Property-Based Tests for BaileysWhatsAppTransport
/// 
/// Tests universal properties that must hold for Baileys transport:
/// - Property 15: Error Propagation
/// 
/// Uses FsCheck for property-based testing with 100+ iterations
/// </summary>
public class BaileysTransportPropertyTests
{
    /// <summary>
    /// Property 15: Error Propagation
    /// 
    /// **Validates: Requirements 11.2**
    /// 
    /// For any HTTP error response from the Baileys service, the transport must:
    /// 1. Return Success = false
    /// 2. Include a descriptive error message
    /// 3. Not throw exceptions (graceful degradation)
    /// 4. Log the error appropriately
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ErrorPropagation_SendMessage_ReturnsFailureWithErrorMessage()
    {
        return Prop.ForAll(
            Arb.From<HttpStatusCode>().Generator.Where(code => (int)code >= 400),
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (statusCode, tenantId, errorMessage) =>
            {
                // Arrange
                var mockHandler = new Mock<HttpMessageHandler>();
                var errorResponse = new
                {
                    success = false,
                    error = errorMessage.Get
                };

                mockHandler
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = statusCode,
                        Content = new StringContent(
                            JsonSerializer.Serialize(errorResponse),
                            Encoding.UTF8,
                            "application/json")
                    });

                var httpClient = new HttpClient(mockHandler.Object);
                var config = CreateMockConfiguration();
                var logger = Mock.Of<ILogger<BaileysWhatsAppTransport>>();

                var transport = new BaileysWhatsAppTransport(httpClient, config, logger);

                // Act
                var result = await transport.SendMessageAsync(
                    tenantId.Get,
                    "+1234567890",
                    "Test message",
                    WhatsAppMessageType.Text);

                // Assert
                return (result.Success == false)
                    .Label("Result should indicate failure")
                    .And(!string.IsNullOrEmpty(result.ErrorMessage))
                    .Label("Error message should be present");
            });
    }

    [Property(MaxTest = 100)]
    public Property ErrorPropagation_GetQrCode_ReturnsFailureWithErrorMessage()
    {
        return Prop.ForAll(
            Arb.From<HttpStatusCode>().Generator.Where(code => (int)code >= 400),
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (statusCode, tenantId, errorMessage) =>
            {
                // Arrange
                var mockHandler = new Mock<HttpMessageHandler>();
                var errorResponse = new
                {
                    success = false,
                    error = errorMessage.Get
                };

                mockHandler
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = statusCode,
                        Content = new StringContent(
                            JsonSerializer.Serialize(errorResponse),
                            Encoding.UTF8,
                            "application/json")
                    });

                var httpClient = new HttpClient(mockHandler.Object);
                var config = CreateMockConfiguration();
                var logger = Mock.Of<ILogger<BaileysWhatsAppTransport>>();

                var transport = new BaileysWhatsAppTransport(httpClient, config, logger);

                // Act
                var result = await transport.GetQrCodeAsync(tenantId.Get);

                // Assert
                return (result.Success == false)
                    .Label("Result should indicate failure")
                    .And(!string.IsNullOrEmpty(result.ErrorMessage))
                    .Label("Error message should be present");
            });
    }

    [Property(MaxTest = 100)]
    public Property ErrorPropagation_GetStatus_ReturnsFailureWithErrorMessage()
    {
        return Prop.ForAll(
            Arb.From<HttpStatusCode>().Generator.Where(code => (int)code >= 400),
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (statusCode, tenantId, errorMessage) =>
            {
                // Arrange
                var mockHandler = new Mock<HttpMessageHandler>();
                var errorResponse = new
                {
                    success = false,
                    error = errorMessage.Get
                };

                mockHandler
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = statusCode,
                        Content = new StringContent(
                            JsonSerializer.Serialize(errorResponse),
                            Encoding.UTF8,
                            "application/json")
                    });

                var httpClient = new HttpClient(mockHandler.Object);
                var config = CreateMockConfiguration();
                var logger = Mock.Of<ILogger<BaileysWhatsAppTransport>>();

                var transport = new BaileysWhatsAppTransport(httpClient, config, logger);

                // Act
                var result = await transport.GetStatusAsync(tenantId.Get);

                // Assert
                return (result.Success == false)
                    .Label("Result should indicate failure")
                    .And(result.Status == WhatsAppConnectionStatus.Unknown)
                    .Label("Status should be Unknown on error")
                    .And(!string.IsNullOrEmpty(result.ErrorMessage))
                    .Label("Error message should be present");
            });
    }

    [Property(MaxTest = 100)]
    public Property ErrorPropagation_Disconnect_ReturnsFailureWithErrorMessage()
    {
        return Prop.ForAll(
            Arb.From<HttpStatusCode>().Generator.Where(code => (int)code >= 400),
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (statusCode, tenantId, errorMessage) =>
            {
                // Arrange
                var mockHandler = new Mock<HttpMessageHandler>();
                var errorResponse = new
                {
                    success = false,
                    error = errorMessage.Get
                };

                mockHandler
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = statusCode,
                        Content = new StringContent(
                            JsonSerializer.Serialize(errorResponse),
                            Encoding.UTF8,
                            "application/json")
                    });

                var httpClient = new HttpClient(mockHandler.Object);
                var config = CreateMockConfiguration();
                var logger = Mock.Of<ILogger<BaileysWhatsAppTransport>>();

                var transport = new BaileysWhatsAppTransport(httpClient, config, logger);

                // Act
                var result = await transport.DisconnectAsync(tenantId.Get);

                // Assert
                return (result.Success == false)
                    .Label("Result should indicate failure")
                    .And(!string.IsNullOrEmpty(result.ErrorMessage))
                    .Label("Error message should be present");
            });
    }

    /// <summary>
    /// Property: Timeout Handling
    /// 
    /// For any operation that times out, the transport must:
    /// 1. Return Success = false
    /// 2. Include "timeout" in error message
    /// 3. Not throw exceptions
    /// </summary>
    [Property(MaxTest = 50)]
    public Property TimeoutHandling_SendMessage_ReturnsTimeoutError()
    {
        return Prop.ForAll(
            Arb.Default.NonEmptyString().Generator,
            async (tenantId) =>
            {
                // Arrange
                var mockHandler = new Mock<HttpMessageHandler>();
                
                mockHandler
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                    .ThrowsAsync(new TaskCanceledException("Request timeout"));

                var httpClient = new HttpClient(mockHandler.Object);
                var config = CreateMockConfiguration();
                var logger = Mock.Of<ILogger<BaileysWhatsAppTransport>>();

                var transport = new BaileysWhatsAppTransport(httpClient, config, logger);

                // Act
                var result = await transport.SendMessageAsync(
                    tenantId.Get,
                    "+1234567890",
                    "Test message",
                    WhatsAppMessageType.Text);

                // Assert
                return (result.Success == false)
                    .Label("Result should indicate failure")
                    .And(!string.IsNullOrEmpty(result.ErrorMessage))
                    .Label("Error message should be present")
                    .And(result.ErrorMessage!.ToLower().Contains("timeout"))
                    .Label("Error message should mention timeout");
            });
    }

    /// <summary>
    /// Property: Success Response Parsing
    /// 
    /// For any successful HTTP response with valid JSON, the transport must:
    /// 1. Return Success = true
    /// 2. Parse response data correctly
    /// 3. Not throw exceptions
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SuccessResponseParsing_SendMessage_ReturnsSuccessWithMessageId()
    {
        return Prop.ForAll(
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, messageId) =>
            {
                // Arrange
                var mockHandler = new Mock<HttpMessageHandler>();
                var successResponse = new
                {
                    success = true,
                    messageId = messageId.Get
                };

                mockHandler
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(
                            JsonSerializer.Serialize(successResponse),
                            Encoding.UTF8,
                            "application/json")
                    });

                var httpClient = new HttpClient(mockHandler.Object);
                var config = CreateMockConfiguration();
                var logger = Mock.Of<ILogger<BaileysWhatsAppTransport>>();

                var transport = new BaileysWhatsAppTransport(httpClient, config, logger);

                // Act
                var result = await transport.SendMessageAsync(
                    tenantId.Get,
                    "+1234567890",
                    "Test message",
                    WhatsAppMessageType.Text);

                // Assert
                return (result.Success == true)
                    .Label("Result should indicate success")
                    .And(!string.IsNullOrEmpty(result.MessageId))
                    .Label("Message ID should be present")
                    .And(result.MessageId == messageId.Get)
                    .Label("Message ID should match response");
            });
    }

    [Property(MaxTest = 100)]
    public Property SuccessResponseParsing_GetQrCode_ReturnsSuccessWithQrCode()
    {
        return Prop.ForAll(
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, qrCode) =>
            {
                // Arrange
                var mockHandler = new Mock<HttpMessageHandler>();
                var successResponse = new
                {
                    success = true,
                    qrCode = qrCode.Get
                };

                mockHandler
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(
                            JsonSerializer.Serialize(successResponse),
                            Encoding.UTF8,
                            "application/json")
                    });

                var httpClient = new HttpClient(mockHandler.Object);
                var config = CreateMockConfiguration();
                var logger = Mock.Of<ILogger<BaileysWhatsAppTransport>>();

                var transport = new BaileysWhatsAppTransport(httpClient, config, logger);

                // Act
                var result = await transport.GetQrCodeAsync(tenantId.Get);

                // Assert
                return (result.Success == true)
                    .Label("Result should indicate success")
                    .And(!string.IsNullOrEmpty(result.QrCodeBase64))
                    .Label("QR code should be present")
                    .And(result.QrCodeBase64 == qrCode.Get)
                    .Label("QR code should match response");
            });
    }

    [Property(MaxTest = 100)]
    public Property SuccessResponseParsing_GetStatus_ReturnsSuccessWithStatus()
    {
        return Prop.ForAll(
            Arb.Default.NonEmptyString().Generator,
            Gen.Elements("connected", "disconnected", "qr_pending", "connecting"),
            async (tenantId, status) =>
            {
                // Arrange
                var mockHandler = new Mock<HttpMessageHandler>();
                var successResponse = new
                {
                    success = true,
                    status
                };

                mockHandler
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>())
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(
                            JsonSerializer.Serialize(successResponse),
                            Encoding.UTF8,
                            "application/json")
                    });

                var httpClient = new HttpClient(mockHandler.Object);
                var config = CreateMockConfiguration();
                var logger = Mock.Of<ILogger<BaileysWhatsAppTransport>>();

                var transport = new BaileysWhatsAppTransport(httpClient, config, logger);

                // Act
                var result = await transport.GetStatusAsync(tenantId.Get);

                // Assert
                return (result.Success == true)
                    .Label("Result should indicate success")
                    .And(result.Status != WhatsAppConnectionStatus.Unknown)
                    .Label("Status should be mapped correctly");
            });
    }

    /// <summary>
    /// Creates a mock configuration with Baileys service base URL.
    /// </summary>
    private static IConfiguration CreateMockConfiguration()
    {
        var configData = new Dictionary<string, string?>
        {
            { "BaileysService:BaseUrl", "http://localhost:3000" }
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }
}
