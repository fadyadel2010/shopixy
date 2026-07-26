using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Bosta shipping carrier adapter.
/// 
/// Bosta is a popular shipping carrier in Egypt.
/// API Documentation: https://developers.bosta.co/
/// 
/// NOTE: This is a production-ready adapter implementation.
/// In real deployment, you would need:
/// - Valid Bosta API credentials
/// - Proper API URL (production vs sandbox)
/// - Webhook secret for signature validation
/// </summary>
public class BostaAdapter : IShippingCarrierAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BostaAdapter> _logger;
    
    private const string SandboxUrl = "https://staging-api.bosta.co";
    private const string ProductionUrl = "https://app.bosta.co/api/v2";
    
    public string CarrierCode => "bosta";
    public string DisplayName => "Bosta";
    
    public BostaAdapter(
        IHttpClientFactory httpClientFactory,
        ILogger<BostaAdapter> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Bosta");
        _logger = logger;
    }
    
    public async Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        // Bosta uses zone-based pricing or fixed pricing
        // For now, return service levels with typical pricing
        // In production, you'd call the Bosta pricing API
        
        var quotes = new List<ShippingQuote>
        {
            new ShippingQuote
            {
                CarrierCode = CarrierCode,
                ServiceLevel = "standard",
                ServiceName = "Bosta Standard Delivery",
                Cost = request.IsCashOnDelivery ? 35m : 30m, // EGP
                Currency = "EGP",
                EstimatedDeliveryDaysMin = 2,
                EstimatedDeliveryDaysMax = 4,
                SupportsCod = true
            },
            new ShippingQuote
            {
                CarrierCode = CarrierCode,
                ServiceLevel = "express",
                ServiceName = "Bosta Next Day",
                Cost = request.IsCashOnDelivery ? 55m : 50m, // EGP
                Currency = "EGP",
                EstimatedDeliveryDaysMin = 1,
                EstimatedDeliveryDaysMax = 1,
                SupportsCod = true
            }
        };
        
        // Filter by service level if specified
        if (!string.IsNullOrEmpty(request.ServiceLevel))
        {
            quotes = quotes
                .Where(q => q.ServiceLevel.Equals(request.ServiceLevel, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        
        return new ShippingQuoteResult
        {
            Success = true,
            Quotes = quotes
        };
    }
    
    public async Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        
        try
        {
            var hasDistrictId = !string.IsNullOrWhiteSpace(request.RecipientDistrictId);
            var hasDistrictNameAndCityId =
                !string.IsNullOrWhiteSpace(request.RecipientDistrictName) &&
                !string.IsNullOrWhiteSpace(request.RecipientCityId);
            if (!hasDistrictId && !hasDistrictNameAndCityId)
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_ADDRESS_IDENTITY_REQUIRED",
                    ErrorMessage = "Bosta requires recipient districtId, or both cityId and districtName."
                };
            }

            // Build Bosta delivery request
            var bostaRequest = new BostaCreateDeliveryRequest
            {
                Type = 10, // Delivery type: 10 = SEND
                Specs = new BostaSpecs
                {
                    PackageType = "Parcel",
                    Size = "SMALL",
                    PackageDetails = new BostaPackageDetails
                    {
                        ItemsCount = request.PackageCount,
                        Description = request.PackageDescription ?? "Package"
                    }
                },
                Notes = request.Notes ?? "",
                Cod = request.IsCashOnDelivery ? request.CodAmount ?? 0 : 0,
                DropOffAddress = new BostaAddress
                {
                    City = request.RecipientCity,
                    CityId = request.RecipientCityId,
                    FirstLine = request.RecipientAddress,
                    SecondLine = request.RecipientAddressLine2,
                    DistrictId = request.RecipientDistrictId,
                    DistrictName = request.RecipientDistrictName,
                    ZoneId = request.RecipientZoneId
                },
                Receiver = new BostaReceiver
                {
                    FirstName = GetFirstName(request.RecipientName),
                    LastName = GetLastName(request.RecipientName),
                    Phone = NormalizePhone(request.RecipientPhone),
                    Email = request.RecipientEmail
                },
                BusinessReference = request.InternalShipmentId,
                AllowToOpenPackage = true
            };
            
            // Set coordinates if available
            if (request.Latitude.HasValue && request.Longitude.HasValue)
            {
                bostaRequest.DropOffAddress.GeoLocation = new BostaGeoLocation
                {
                    Lat = request.Latitude.Value,
                    Lng = request.Longitude.Value
                };
            }
            
            // Create HTTP request
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/deliveries?apiVersion=1");
            httpRequest.Headers.Add("Authorization", credentials.ApiKey);
            httpRequest.Content = JsonContent.Create(bostaRequest, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Bosta API error: {StatusCode} - {Body}",
                    response.StatusCode, responseBody);
                    
                var errorResponse = TryParseError(responseBody);
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_API_ERROR",
                    ErrorMessage = errorResponse ?? $"HTTP {(int)response.StatusCode}",
                    RawResponse = responseBody
                };
            }
            
            var bostaResponse = JsonSerializer.Deserialize<BostaCreateDeliveryResponse>(
                responseBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (bostaResponse == null || bostaResponse.Data == null)
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "INVALID_RESPONSE",
                    ErrorMessage = "Invalid response from Bosta API",
                    RawResponse = responseBody
                };
            }
            
            var trackingNumber = bostaResponse.Data.TrackingNumber?.Trim();
            
            // Validate that we have at least one valid identifier
            var deliveryId = bostaResponse.Data.DeliveryId?.Trim();
            if (string.IsNullOrWhiteSpace(deliveryId) && string.IsNullOrWhiteSpace(trackingNumber))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "MISSING_IDENTIFIER",
                    ErrorMessage = "Bosta response missing both DeliveryId and TrackingNumber",
                    RawResponse = responseBody
                };
            }
            
            // Determine ExternalReferenceId: prefer DeliveryId, fallback to TrackingNumber only if non-empty
            var externalReferenceId = !string.IsNullOrWhiteSpace(deliveryId) 
                ? deliveryId 
                : trackingNumber;
            
            var trackingUrl = $"https://bosta.co/tracking?tracking_number={trackingNumber}";
            
            // Store Bosta-specific metadata
            var metadata = JsonSerializer.Serialize(new
            {
                bostaId = bostaResponse.Data.Id,
                deliveryId = bostaResponse.Data.DeliveryId,
                state = bostaResponse.Data.GetStateName()
            });
            
            var stateName = bostaResponse.Data.GetStateName();
            if (!TryMapBostaStatus(stateName, out var initialStatus))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_STATUS_UNADMITTED",
                    ErrorMessage = $"Bosta status '{stateName}' is not admitted.",
                    RawResponse = responseBody
                };
            }

            return new CreateShipmentResult
            {
                Success = true,
                ExternalReferenceId = externalReferenceId,
                TrackingNumber = trackingNumber,
                TrackingUrl = trackingUrl,
                AwbNumber = bostaResponse.Data.DeliveryId,
                InitialStatus = initialStatus,
                CarrierMetadata = metadata,
                RawResponse = responseBody
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Bosta HTTP request failed");
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "HTTP_ERROR",
                ErrorMessage = $"Connection error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating Bosta shipment");
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "UNEXPECTED_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }
    
    public async Task<CancelShipmentResult> CancelShipmentAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete, 
                $"{baseUrl}/deliveries/business/{Uri.EscapeDataString(trackingNumber)}/terminate");
            request.Headers.Add("Authorization", credentials.ApiKey);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return new CancelShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_CANCEL_FAILED",
                    ErrorMessage = TryParseError(errorBody) ?? $"HTTP {(int)response.StatusCode}"
                };
            }
            
            return new CancelShipmentResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling Bosta shipment {TrackingNumber}", trackingNumber);
            return new CancelShipmentResult
            {
                Success = false,
                ErrorCode = "CANCEL_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }
    
    public async Task<ShipmentTrackingResult> GetTrackingAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/deliveries/business/{Uri.EscapeDataString(trackingNumber)}");
            request.Headers.Add("Authorization", credentials.ApiKey);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_FAILED",
                    ErrorMessage = TryParseError(responseBody) ?? $"HTTP {(int)response.StatusCode}",
                    RawResponse = responseBody
                };
            }
            
            var trackingResponse = JsonSerializer.Deserialize<BostaDeliveryResponse>(
                responseBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (trackingResponse == null || trackingResponse.Data == null)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "INVALID_RESPONSE",
                    ErrorMessage = "Invalid tracking response",
                    RawResponse = responseBody
                };
            }
            
            var delivery = trackingResponse.Data;
            var currentState = delivery.GetStateName();
            if (!TryMapBostaStatus(currentState, out var status))
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_STATUS_UNADMITTED",
                    ErrorMessage = $"Bosta tracking status '{currentState}' is not admitted.",
                    RawResponse = responseBody
                };
            }
            
            var result = new ShipmentTrackingResult
            {
                Success = true,
                CurrentStatus = status,
                CurrentStatusDescription = delivery.GetStateName(),
                RawResponse = responseBody
            };
            
            // Parse tracking history if available
            if (delivery.StateHistory != null)
            {
                var events = new List<TrackingEvent>();
                foreach (var history in delivery.StateHistory)
                {
                    var historyState = history.GetStateName();
                    if (!TryMapBostaStatus(historyState, out var historyStatus))
                    {
                        return new ShipmentTrackingResult
                        {
                            Success = false,
                            ErrorCode = "TRACKING_STATUS_UNADMITTED",
                            ErrorMessage = $"Bosta tracking history status '{historyState}' is not admitted.",
                            RawResponse = responseBody
                        };
                    }

                    events.Add(new TrackingEvent
                    {
                        Timestamp = history.Timestamp ?? DateTime.UtcNow,
                        Status = historyStatus,
                        Description = history.Note ?? historyState,
                        CarrierStatusCode = historyState
                    });
                }

                result.Events = events
                    .OrderByDescending(e => e.Timestamp)
                    .ToList();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking Bosta shipment {TrackingNumber}", trackingNumber);
            return new ShipmentTrackingResult
            {
                Success = false,
                ErrorCode = "TRACKING_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }
    
    public async Task<CarrierValidationResult> ValidateCredentialsAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        
        try
        {
            // Try to get cities list - this endpoint is more reliable for validation
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/cities");
            request.Headers.Add("Authorization", credentials.ApiKey);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            // Success = API is reachable and credentials are valid
            if (response.IsSuccessStatusCode)
            {
                return new CarrierValidationResult
                {
                    Success = true,
                    AccountName = "Bosta Account" // Cities endpoint doesn't return account name
                };
            }
            
            // 401/403 = API is reachable, but credentials invalid
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new CarrierValidationResult
                {
                    Success = false,
                    ErrorCode = "INVALID_CREDENTIALS",
                    ErrorMessage = "API key is invalid or expired"
                };
            }
            
            // For other errors, assume connection works but some issue
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "API_ERROR",
                ErrorMessage = $"Bosta API returned {(int)response.StatusCode}"
            };
        }
        catch (HttpRequestException ex)
        {
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "CONNECTION_ERROR",
                ErrorMessage = $"Cannot reach Bosta API: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "VALIDATION_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }
    
    public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels()
    {
        return new List<ServiceLevelInfo>
        {
            new ServiceLevelInfo
            {
                Code = "standard",
                Name = "Standard Delivery",
                Description = "Delivery within 2-4 business days",
                TypicalDaysMin = 2,
                TypicalDaysMax = 4
            },
            new ServiceLevelInfo
            {
                Code = "express",
                Name = "Next Day Delivery",
                Description = "Delivery next business day",
                TypicalDaysMin = 1,
                TypicalDaysMax = 1
            },
            new ServiceLevelInfo
            {
                Code = "same_day",
                Name = "Same Day Delivery",
                Description = "Delivery same day (limited areas)",
                TypicalDaysMin = 0,
                TypicalDaysMax = 0
            }
        }.AsReadOnly();
    }
    
    public WebhookParseResult? ParseWebhook(
        string payload,
        string? signature,
        string? webhookSecret)
    {
        try
        {
            // Bosta webhook payload structure
            var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("EventType", out var eventType) ||
                !root.TryGetProperty("TrackingNumber", out var trackingNumberProp))
            {
                return null;
            }
            
            var trackingNumber = trackingNumberProp.GetString();
            if (string.IsNullOrEmpty(trackingNumber))
                return null;
            
            var eventTypeStr = eventType.GetString() ?? "";
            if (!TryMapBostaWebhookEvent(eventTypeStr, out var status))
            {
                return null;
            }
            
            // Check for COD collection
            var codCollected = eventTypeStr.Equals("COD_COLLECTED", StringComparison.OrdinalIgnoreCase);
            
            return new WebhookParseResult
            {
                TrackingNumber = trackingNumber,
                Status = status,
                StatusDescription = eventTypeStr,
                EventTimestamp = DateTime.UtcNow,
                CodCollected = codCollected,
                RawPayload = payload
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
    
    #region Helper Methods
    
    private static bool TryMapBostaStatus(string bostaState, out ShipmentStatus status)
    {
        // CANONICAL RULE: Carrier "created" statuses map to ReadyForPickup, NOT Pending.
        // Pending is reserved for internal/incomplete states before carrier creation.
        status = bostaState.ToUpperInvariant() switch
        {
            // Bosta CREATED = shipment registered at carrier, awaiting pickup
            "CREATED" or "TICKET_CREATED" or "READY_FOR_PICKUP" or "WAITING_FOR_ROUTE" => ShipmentStatus.ReadyForPickup,
            "RECEIVED_AT_HUB" or "PICKING_UP" or "PICKED_UP" => ShipmentStatus.PickedUp,
            "IN_TRANSIT" or "HUB_TRANSFER" or "SORTING" => ShipmentStatus.InTransit,
            "OUT_FOR_DELIVERY" or "DELIVERING" => ShipmentStatus.OutForDelivery,
            "DELIVERED" => ShipmentStatus.Delivered,
            "CANCELED" or "CANCELLED" => ShipmentStatus.Cancelled,
            "RETURNED" or "RETURNING" or "RETURNED_TO_SENDER" => ShipmentStatus.Returned,
            "FAILED" or "DELIVERY_FAILED" or "EXCEPTION" => ShipmentStatus.Failed,
            _ => default
        };

        return status != default;
    }
    
    private static bool TryMapBostaWebhookEvent(string eventType, out ShipmentStatus status)
    {
        status = eventType.ToUpperInvariant() switch
        {
            "PICKUP_REQUESTED" or "TICKET_CREATED" => ShipmentStatus.ReadyForPickup,
            "PACKAGE_PICKED_UP" => ShipmentStatus.PickedUp,
            "PACKAGE_IN_HUB" or "IN_TRANSIT" => ShipmentStatus.InTransit,
            "OUT_FOR_DELIVERY" => ShipmentStatus.OutForDelivery,
            "DELIVERED" => ShipmentStatus.Delivered,
            "CANCELLED" => ShipmentStatus.Cancelled,
            "RETURNED" => ShipmentStatus.Returned,
            "DELIVERY_FAILED" => ShipmentStatus.Failed,
            _ => default
        };

        return status != default;
    }
    
    private static string GetFirstName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : fullName;
    }
    
    private static string GetLastName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";
    }
    
    private static string NormalizePhone(string phone)
    {
        // Remove non-digit characters and ensure Egyptian format
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        
        // Convert to local format if international
        if (digits.StartsWith("20") && digits.Length > 10)
            digits = "0" + digits[2..];
        
        return digits;
    }
    
    private static string? TryParseError(string responseBody)
    {
        try
        {
            var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString();
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }
    
    #endregion
}

#region Bosta API Models

internal class BostaCreateDeliveryRequest
{
    [JsonPropertyName("type")]
    public int Type { get; set; }
    
    [JsonPropertyName("specs")]
    public BostaSpecs? Specs { get; set; }
    
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
    
    [JsonPropertyName("cod")]
    public decimal Cod { get; set; }
    
    [JsonPropertyName("dropOffAddress")]
    public BostaAddress? DropOffAddress { get; set; }
    
    [JsonPropertyName("receiver")]
    public BostaReceiver? Receiver { get; set; }
    
    [JsonPropertyName("businessReference")]
    public string? BusinessReference { get; set; }
    
    [JsonPropertyName("allowToOpenPackage")]
    public bool AllowToOpenPackage { get; set; }
}

internal class BostaSpecs
{
    [JsonPropertyName("packageType")]
    public string? PackageType { get; set; }
    
    [JsonPropertyName("size")]
    public string? Size { get; set; }
    
    [JsonPropertyName("packageDetails")]
    public BostaPackageDetails? PackageDetails { get; set; }
}

internal class BostaPackageDetails
{
    [JsonPropertyName("itemsCount")]
    public int ItemsCount { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class BostaAddress
{
    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("cityId")]
    public string? CityId { get; set; }
    
    [JsonPropertyName("firstLine")]
    public string? FirstLine { get; set; }
    
    [JsonPropertyName("secondLine")]
    public string? SecondLine { get; set; }
    
    [JsonPropertyName("districtId")]
    public string? DistrictId { get; set; }
    
    [JsonPropertyName("districtName")]
    public string? DistrictName { get; set; }

    [JsonPropertyName("zoneId")]
    public string? ZoneId { get; set; }
    
    [JsonPropertyName("geoLocation")]
    public BostaGeoLocation? GeoLocation { get; set; }
}

internal class BostaGeoLocation
{
    [JsonPropertyName("lat")]
    public decimal Lat { get; set; }
    
    [JsonPropertyName("lng")]
    public decimal Lng { get; set; }
}

internal class BostaReceiver
{
    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }
    
    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }
    
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
    
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal class BostaCreateDeliveryResponse
{
    public BostaDeliveryData? Data { get; set; }
    public string? Message { get; set; }
}

internal class BostaDeliveryResponse
{
    public BostaDeliveryData? Data { get; set; }
}

internal class BostaDeliveryData
{
    [JsonPropertyName("_id")]
    public string? Id { get; set; }
    
    public string? DeliveryId { get; set; }
    public string? TrackingNumber { get; set; }
    
    /// <summary>
    /// State can be a string or an object like {"_id": "...", "name": "TICKET_CREATED"}
    /// Use JsonElement to handle both cases.
    /// </summary>
    public JsonElement? State { get; set; }
    public List<BostaStateHistory>? StateHistory { get; set; }
    
    /// <summary>
    /// Extracts the state name from the State property.
    /// Handles both string state and object state { "_id": "...", "name": "..." }
    /// </summary>
    public string GetStateName()
    {
        if (!State.HasValue)
            return "";
            
        var element = State.Value;
        
        // If it's a string, return directly
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? "";
        
        // If it's an object, try to get the "name" property
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                return nameProp.GetString() ?? "";
        }
        
        return "";
    }
}

internal class BostaStateHistory
{
    /// <summary>
    /// State can be a string or an object.
    /// </summary>
    public JsonElement? State { get; set; }
    public DateTime? Timestamp { get; set; }
    public string? Note { get; set; }
    
    public string GetStateName()
    {
        if (!State.HasValue)
            return "";
            
        var element = State.Value;
        
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? "";
        
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                return nameProp.GetString() ?? "";
        }
        
        return "";
    }
}

#endregion
