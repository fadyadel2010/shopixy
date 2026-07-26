using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Transport;

/// <summary>
/// Baileys-based transport implementation.
/// Communicates with the Baileys Node.js microservice via HTTP.
/// Provides lightweight WhatsApp messaging with 20-40 MB RAM per session (vs 300-600 MB for Playwright).
/// </summary>
public class BaileysWhatsAppTransport : IWhatsAppTransport
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BaileysWhatsAppTransport> _logger;
    private readonly string _baseUrl;

    public BaileysWhatsAppTransport(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<BaileysWhatsAppTransport> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // Get base URL from configuration
        _baseUrl = configuration["BaileysService:BaseUrl"] 
            ?? throw new InvalidOperationException("BaileysService:BaseUrl configuration is required");

        // SECURITY: Get API key from configuration
        var apiKey = configuration["BaileysService:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[BaileysTransport] API key not configured - requests may fail in production");
        }

        // Configure HttpClient
        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        // SECURITY: Add API key header for authentication
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
        
        _logger.LogInformation(
            "[BaileysTransport] Initialized with base URL: {BaseUrl} | API Key: {HasApiKey}",
            _baseUrl, !string.IsNullOrEmpty(apiKey) ? "configured" : "NOT CONFIGURED");
    }

    public async Task<WhatsAppSendResult> SendMessageAsync(
        string tenantId,
        string phoneE164,
        string message,
        WhatsAppMessageType messageType,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        try
        {
            _logger.LogDebug(
                "[BaileysTransport] Sending message | Tenant: {TenantId} | Phone: {Phone} | Type: {Type}",
                tenantId, MaskPhone(phoneE164), messageType);

            // Prepare request payload
            var request = new
            {
                tenantId,
                phone = phoneE164,
                text = message,
                type = MapMessageType(messageType)
            };

            // Make HTTP POST to /messages/send
            var response = await _httpClient.PostAsJsonAsync(
                "/messages/send",
                request,
                cancellationToken);

            // Read response
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[BaileysTransport] Message send failed | Tenant: {TenantId} | Status: {StatusCode} | Response: {Response}",
                    tenantId, response.StatusCode, responseContent);

                // Try to parse error from response
                var errorResult = TryParseErrorResponse(responseContent);
                
                return new WhatsAppSendResult
                {
                    Success = false,
                    ErrorMessage = errorResult ?? $"HTTP {response.StatusCode}: {responseContent}"
                };
            }

            // Parse success response
            var result = JsonSerializer.Deserialize<BaileysSendResponse>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Success == true)
            {
                _logger.LogInformation(
                    "[BaileysTransport] Message sent successfully | Tenant: {TenantId} | MessageId: {MessageId}",
                    tenantId, result.MessageId);

                return new WhatsAppSendResult
                {
                    Success = true,
                    MessageId = result.MessageId
                };
            }

            _logger.LogWarning(
                "[BaileysTransport] Message send returned failure | Tenant: {TenantId} | Error: {Error}",
                tenantId, result?.Error);

            return new WhatsAppSendResult
            {
                Success = false,
                ErrorMessage = result?.Error ?? "Unknown error"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] HTTP request exception sending message | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppSendResult
            {
                Success = false,
                ErrorMessage = $"HTTP request failed: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] Request timeout sending message | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppSendResult
            {
                Success = false,
                ErrorMessage = "Request timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] Exception sending message | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppSendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppQrResult> GetQrCodeAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "[BaileysTransport] Getting QR code | Tenant: {TenantId}",
                tenantId);

            // STEP 1: First, ensure session exists by calling /connect
            // This creates the session if it doesn't exist
            _logger.LogDebug("[BaileysTransport] Initializing session | Tenant: {TenantId}", tenantId);
            
            var connectRequest = new { mode = "DEDICATED" };
            var connectResponse = await _httpClient.PostAsJsonAsync(
                $"/sessions/{tenantId}/connect",
                connectRequest,
                cancellationToken);

            if (!connectResponse.IsSuccessStatusCode)
            {
                var connectError = await connectResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "[BaileysTransport] Session connect failed | Tenant: {TenantId} | Status: {StatusCode} | Error: {Error}",
                    tenantId, connectResponse.StatusCode, connectError);
                
                // Non-fatal - try to get QR anyway (session may already exist)
            }
            else
            {
                _logger.LogInformation(
                    "[BaileysTransport] Session initialized | Tenant: {TenantId}",
                    tenantId);
            }

            // STEP 2: Get QR code from session
            // The Baileys endpoint now handles polling internally (up to 8 seconds)
            // so we need a longer timeout for this request
            using var qrCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            qrCts.CancelAfter(TimeSpan.FromSeconds(15)); // Allow up to 15 seconds for QR
            
            var response = await _httpClient.GetAsync(
                $"/sessions/{tenantId}/qr",
                qrCts.Token);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[BaileysTransport] QR code request failed | Tenant: {TenantId} | Status: {StatusCode}",
                    tenantId, response.StatusCode);

                var errorResult = TryParseErrorResponse(responseContent);
                
                return new WhatsAppQrResult
                {
                    Success = false,
                    ErrorMessage = errorResult ?? $"HTTP {response.StatusCode}"
                };
            }

            // Parse response
            var result = JsonSerializer.Deserialize<BaileysQrResponse>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // AUDIT FIX: Handle the "alreadyConnected" case when session auto-reconnects from stored auth
            if (result?.Success == true && result.AlreadyConnected)
            {
                _logger.LogInformation(
                    "[BaileysTransport] Session already connected, no QR needed | Tenant: {TenantId}",
                    tenantId);

                return new WhatsAppQrResult
                {
                    Success = true,
                    AlreadyConnected = true,
                    QrCodeBase64 = null
                };
            }

            if (result?.Success == true && !string.IsNullOrEmpty(result.QrCode))
            {
                _logger.LogInformation(
                    "[BaileysTransport] QR code retrieved | Tenant: {TenantId}",
                    tenantId);

                return new WhatsAppQrResult
                {
                    Success = true,
                    QrCodeBase64 = result.QrCode
                };
            }

            return new WhatsAppQrResult
            {
                Success = false,
                ErrorMessage = result?.Error ?? "QR code not available"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] HTTP request exception getting QR code | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppQrResult
            {
                Success = false,
                ErrorMessage = $"HTTP request failed: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] Request timeout getting QR code | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppQrResult
            {
                Success = false,
                ErrorMessage = "Request timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] Exception getting QR code | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppQrResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppStatusResult> GetStatusAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "[BaileysTransport] Getting status | Tenant: {TenantId}",
                tenantId);

            // Make HTTP GET to /sessions/{tenantId}/status
            var response = await _httpClient.GetAsync(
                $"/sessions/{tenantId}/status",
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[BaileysTransport] Status request failed | Tenant: {TenantId} | Status: {StatusCode}",
                    tenantId, response.StatusCode);

                var errorResult = TryParseErrorResponse(responseContent);
                
                return new WhatsAppStatusResult
                {
                    Success = false,
                    Status = WhatsAppConnectionStatus.Unknown,
                    ErrorMessage = errorResult ?? $"HTTP {response.StatusCode}"
                };
            }

            // Parse response
            var result = JsonSerializer.Deserialize<BaileysStatusResponse>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Success == true)
            {
                var status = MapConnectionStatus(result.Status);
                
                _logger.LogDebug(
                    "[BaileysTransport] Status retrieved | Tenant: {TenantId} | Status: {Status}",
                    tenantId, status);

                return new WhatsAppStatusResult
                {
                    Success = true,
                    Status = status,
                    ConnectedPhone = result.ConnectedPhone
                };
            }

            return new WhatsAppStatusResult
            {
                Success = false,
                Status = WhatsAppConnectionStatus.Unknown,
                ErrorMessage = result?.Error ?? "Status unavailable"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] HTTP request exception getting status | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppStatusResult
            {
                Success = false,
                Status = WhatsAppConnectionStatus.Unknown,
                ErrorMessage = $"HTTP request failed: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] Request timeout getting status | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppStatusResult
            {
                Success = false,
                Status = WhatsAppConnectionStatus.Unknown,
                ErrorMessage = "Request timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] Exception getting status | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppStatusResult
            {
                Success = false,
                Status = WhatsAppConnectionStatus.Unknown,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppDisconnectResult> DisconnectAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "[BaileysTransport] Disconnecting | Tenant: {TenantId}",
                tenantId);

            // Make HTTP POST to /sessions/{tenantId}/disconnect
            var response = await _httpClient.PostAsync(
                $"/sessions/{tenantId}/disconnect",
                null,
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[BaileysTransport] Disconnect failed | Tenant: {TenantId} | Status: {StatusCode}",
                    tenantId, response.StatusCode);

                var errorResult = TryParseErrorResponse(responseContent);
                
                return new WhatsAppDisconnectResult
                {
                    Success = false,
                    ErrorMessage = errorResult ?? $"HTTP {response.StatusCode}"
                };
            }

            // Parse response
            var result = JsonSerializer.Deserialize<BaileysDisconnectResponse>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Success == true)
            {
                _logger.LogInformation(
                    "[BaileysTransport] Disconnected successfully | Tenant: {TenantId}",
                    tenantId);

                return new WhatsAppDisconnectResult
                {
                    Success = true
                };
            }

            return new WhatsAppDisconnectResult
            {
                Success = false,
                ErrorMessage = result?.Error ?? "Disconnect failed"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] HTTP request exception disconnecting | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppDisconnectResult
            {
                Success = false,
                ErrorMessage = $"HTTP request failed: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] Request timeout disconnecting | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppDisconnectResult
            {
                Success = false,
                ErrorMessage = "Request timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[BaileysTransport] Exception disconnecting | Tenant: {TenantId}",
                tenantId);
            
            return new WhatsAppDisconnectResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Maps WhatsAppMessageType to Baileys message type string.
    /// </summary>
    private static string MapMessageType(WhatsAppMessageType messageType)
    {
        // Baileys expects lowercase message types: 'text', 'media', 'template'
        return messageType switch
        {
            WhatsAppMessageType.Text => "text",
            WhatsAppMessageType.Media => "media",
            WhatsAppMessageType.Template => "template",
            _ => "text"
        };
    }

    /// <summary>
    /// Maps Baileys connection status string to WhatsAppConnectionStatus enum.
    /// </summary>
    private static WhatsAppConnectionStatus MapConnectionStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "connected" => WhatsAppConnectionStatus.Connected,
            "disconnected" => WhatsAppConnectionStatus.Disconnected,
            "qr_pending" => WhatsAppConnectionStatus.QrPending,
            "connecting" => WhatsAppConnectionStatus.QrPending, // Treat connecting as QR pending
            _ => WhatsAppConnectionStatus.Unknown
        };
    }

    /// <summary>
    /// Tries to parse error message from JSON response.
    /// </summary>
    private string? TryParseErrorResponse(string responseContent)
    {
        try
        {
            var errorResponse = JsonSerializer.Deserialize<BaileysErrorResponse>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            return errorResponse?.Error;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Masks phone number for logging (privacy).
    /// </summary>
    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 6)
            return "****";

        var prefix = phone.Substring(0, 4);
        var suffix = phone.Substring(phone.Length - 3);
        var masked = new string('*', Math.Max(0, phone.Length - 7));
        return $"{prefix}{masked}{suffix}";
    }

    #region Response DTOs

    private class BaileysSendResponse
    {
        public bool Success { get; set; }
        public string? MessageId { get; set; }
        public string? Error { get; set; }
    }

    private class BaileysQrResponse
    {
        public bool Success { get; set; }
        public string? QrCode { get; set; }
        public string? Error { get; set; }
        public bool AlreadyConnected { get; set; }
    }

    private class BaileysStatusResponse
    {
        public bool Success { get; set; }
        public string? Status { get; set; }
        public string? ConnectedPhone { get; set; }
        public string? Error { get; set; }
    }

    private class BaileysDisconnectResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    private class BaileysErrorResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    #endregion
}
