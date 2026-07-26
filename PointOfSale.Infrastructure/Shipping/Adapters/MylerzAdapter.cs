using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Mylerz shipping carrier adapter.
/// 
/// Mylerz is a leading Egyptian last-mile delivery company.
/// API Documentation: https://developers.mylerz.com/
/// 
/// CARRIER-AGNOSTIC ARCHITECTURE:
/// ❌ NO Mylerz enums outside this file
/// ❌ NO Mylerz field names in core entities
/// ✅ Translates Mylerz → Canonical POS models
/// ✅ Follows canonical ShipmentStatus lifecycle
/// ✅ ReadyForPickup used after successful creation (NOT Pending)
/// 
/// STATUS MAPPING (MANDATORY):
/// Mylerz Status       → POS ShipmentStatus
/// ------------------------------------------
/// created             → ReadyForPickup
/// pickup_scheduled    → ReadyForPickup
/// picked_up           → PickedUp
/// in_transit          → InTransit
/// out_for_delivery    → OutForDelivery
/// delivered           → Delivered
/// cancelled           → Cancelled
/// returned            → Returned
/// failed              → Failed
/// </summary>
public class MylerzAdapter : IShippingCarrierAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MylerzAdapter> _logger;
    
    private const string ProductionUrl = "https://api.mylerz.com/v2";
    private const string SandboxUrl = "https://sandbox-api.mylerz.com/v2";
    private const string ContractUnavailableErrorCode = "MYLERZ_OFFICIAL_CONTRACT_UNAVAILABLE";
    private const string ContractUnavailableErrorMessage =
        "Official public Mylerz API contract is unavailable; operation is disabled until a vendor-issued contract is supplied and frozen.";
    
    public string CarrierCode => "mylerz";
    public string DisplayName => "Mylerz";
    
    public MylerzAdapter(
        IHttpClientFactory httpClientFactory,
        ILogger<MylerzAdapter> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Mylerz");
        _logger = logger;
    }
    
    public async Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (OfficialPublicContractUnavailable())
        {
            _logger.LogWarning("Mylerz quote request blocked because no official public API contract is available.");
            return await Task.FromResult(new ShippingQuoteResult
            {
                Success = false,
                ErrorCode = ContractUnavailableErrorCode,
                ErrorMessage = ContractUnavailableErrorMessage
            });
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            // Build Mylerz rate calculation request
            var mylerzRequest = new MylerzRateRequest
            {
                OriginCity = request.OriginCity,
                DestinationCity = request.DestinationCity,
                Weight = request.WeightKg,
                Pieces = request.PackageCount,
                IsCod = request.IsCashOnDelivery,
                CodAmount = request.CodAmount ?? 0,
                ServiceType = MapServiceLevel(request.ServiceLevel)
            };
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/shipping/rates");
            AddAuthHeaders(httpRequest, credentials);
            httpRequest.Content = JsonContent.Create(mylerzRequest, options: GetJsonOptions());
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mylerz rate calculation failed: {StatusCode} - {Body}", 
                    response.StatusCode, responseBody);
                
                // Fallback to default rates on error
                return new ShippingQuoteResult
                {
                    Success = true,
                    Quotes = GetDefaultQuotes(request)
                };
            }
            
            var rateResponse = JsonSerializer.Deserialize<MylerzRateResponse>(responseBody, GetJsonOptions());
            
            if (rateResponse?.Success != true || rateResponse.Data == null)
            {
                return new ShippingQuoteResult
                {
                    Success = true,
                    Quotes = GetDefaultQuotes(request)
                };
            }
            
            var quotes = new List<ShippingQuote>();
            
            foreach (var rate in rateResponse.Data.Rates ?? new List<MylerzRate>())
            {
                quotes.Add(new ShippingQuote
                {
                    CarrierCode = CarrierCode,
                    ServiceLevel = MapMylerzServiceToCanonical(rate.ServiceType),
                    ServiceName = rate.ServiceName ?? GetServiceName(rate.ServiceType),
                    Cost = rate.TotalCost,
                    Currency = "EGP",
                    EstimatedDeliveryDaysMin = rate.MinDays,
                    EstimatedDeliveryDaysMax = rate.MaxDays,
                    SupportsCod = true,
                    CarrierQuoteId = rate.QuoteId
                });
            }
            
            // Fallback if no rates returned
            if (!quotes.Any())
            {
                quotes = GetDefaultQuotes(request);
            }
            
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
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Mylerz rate calculation HTTP error");
            return new ShippingQuoteResult
            {
                Success = true,
                Quotes = GetDefaultQuotes(request)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mylerz rate calculation unexpected error");
            return new ShippingQuoteResult
            {
                Success = true,
                Quotes = GetDefaultQuotes(request)
            };
        }
    }
    
    public async Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (OfficialPublicContractUnavailable())
        {
            _logger.LogWarning(
                "Mylerz shipment creation blocked for order {OrderNumber} because no official public API contract is available.",
                request.OrderNumber);
            return await Task.FromResult(new CreateShipmentResult
            {
                Success = false,
                ErrorCode = ContractUnavailableErrorCode,
                ErrorMessage = ContractUnavailableErrorMessage
            });
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            // Build Mylerz shipment creation request
            var mylerzRequest = new MylerzCreateShipmentRequest
            {
                Reference = request.OrderNumber,
                InternalReference = request.InternalShipmentId,
                ServiceType = MapServiceLevel(request.ServiceLevel),
                Pickup = new MylerzAddress
                {
                    Name = request.OriginName,
                    Address = request.OriginAddress,
                    City = request.OriginCity,
                    Country = request.OriginCountry,
                    PostalCode = request.OriginZipCode,
                    Phone = request.OriginPhone
                },
                Delivery = new MylerzAddress
                {
                    Name = request.RecipientName,
                    Address = request.RecipientAddress,
                    AddressLine2 = request.RecipientAddressLine2,
                    City = request.RecipientCity,
                    Country = request.RecipientCountry,
                    PostalCode = request.RecipientZipCode,
                    Phone = request.RecipientPhone,
                    Email = request.RecipientEmail
                },
                Package = new MylerzPackage
                {
                    Weight = request.WeightKg,
                    Pieces = request.PackageCount,
                    Description = request.PackageDescription ?? "Package"
                },
                Cod = request.IsCashOnDelivery ? new MylerzCod
                {
                    Amount = request.CodAmount ?? 0,
                    Currency = request.Currency
                } : null,
                Notes = request.Notes
            };
            
            _logger.LogInformation(
                "Creating Mylerz shipment for order {OrderNumber}",
                request.OrderNumber);
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/shipments");
            AddAuthHeaders(httpRequest, credentials);
            httpRequest.Content = JsonContent.Create(mylerzRequest, options: GetJsonOptions());
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Mylerz API error: {StatusCode} - {Body}",
                    response.StatusCode, responseBody);
                    
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "MYLERZ_API_ERROR",
                    ErrorMessage = TryParseError(responseBody) ?? $"HTTP {(int)response.StatusCode}",
                    RawResponse = responseBody
                };
            }
            
            var mylerzResponse = JsonSerializer.Deserialize<MylerzCreateShipmentResponse>(responseBody, GetJsonOptions());
            
            if (mylerzResponse?.Success != true || mylerzResponse.Data == null)
            {
                var errorMessage = mylerzResponse?.Message ?? "Shipment creation failed";
                
                _logger.LogError("Mylerz shipment creation failed: {Error}", errorMessage);
                
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "MYLERZ_CREATE_ERROR",
                    ErrorMessage = errorMessage,
                    RawResponse = responseBody
                };
            }
            
            var shipment = mylerzResponse.Data;
            
            // Determine tracking number - only use if we have a real value
            var trackingNumber = !string.IsNullOrWhiteSpace(shipment.TrackingNumber) ? shipment.TrackingNumber :
                                 !string.IsNullOrWhiteSpace(shipment.ShipmentId) ? shipment.ShipmentId :
                                 null;
            
            // Determine external reference - only persist if we have a real carrier reference
            var externalReferenceId = !string.IsNullOrWhiteSpace(shipment.ShipmentId) ? shipment.ShipmentId :
                                      !string.IsNullOrWhiteSpace(shipment.Awb) ? shipment.Awb :
                                      !string.IsNullOrWhiteSpace(trackingNumber) ? trackingNumber :
                                      null;
            
            // Build tracking URL only if we have a tracking number
            var trackingUrl = !string.IsNullOrWhiteSpace(trackingNumber) 
                ? $"https://track.mylerz.com/{trackingNumber}" 
                : null;
            
            // Store Mylerz-specific metadata
            var metadata = JsonSerializer.Serialize(new
            {
                mylerzId = shipment.ShipmentId,
                awb = shipment.Awb,
                zoneCode = shipment.ZoneCode,
                hubCode = shipment.HubCode
            });
            
            _logger.LogInformation(
                "Created Mylerz shipment. TrackingNumber: {TrackingNumber}, ExternalReferenceId: {ExternalReferenceId}, OrderNumber: {OrderNumber}",
                trackingNumber, externalReferenceId, request.OrderNumber);
            
            return new CreateShipmentResult
            {
                Success = true,
                ExternalReferenceId = externalReferenceId,
                TrackingNumber = trackingNumber,
                TrackingUrl = trackingUrl,
                AwbNumber = !string.IsNullOrWhiteSpace(shipment.Awb) ? shipment.Awb : trackingNumber,
                // CANONICAL RULE: Successful carrier creation → ReadyForPickup (NEVER Pending)
                InitialStatus = ShipmentStatus.ReadyForPickup,
                LabelUrl = shipment.LabelUrl,
                LabelData = shipment.LabelBase64,
                LabelFormat = "PDF",
                CarrierMetadata = metadata,
                RawResponse = responseBody
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Mylerz HTTP request failed");
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "HTTP_ERROR",
                ErrorMessage = $"Connection error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating Mylerz shipment");
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
        if (OfficialPublicContractUnavailable())
        {
            _logger.LogWarning(
                "Mylerz cancellation blocked for {TrackingNumber} because no official public API contract is available.",
                trackingNumber);
            return await Task.FromResult(new CancelShipmentResult
            {
                Success = false,
                ErrorCode = ContractUnavailableErrorCode,
                ErrorMessage = ContractUnavailableErrorMessage
            });
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            _logger.LogInformation("Cancelling Mylerz shipment {TrackingNumber}", trackingNumber);
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"{baseUrl}/shipments/{trackingNumber}");
            AddAuthHeaders(httpRequest, credentials);
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Mylerz cancel failed for {TrackingNumber}: {StatusCode} - {Body}",
                    trackingNumber, response.StatusCode, responseBody);
                    
                return new CancelShipmentResult
                {
                    Success = false,
                    ErrorCode = "MYLERZ_CANCEL_FAILED",
                    ErrorMessage = TryParseError(responseBody) ?? $"HTTP {(int)response.StatusCode}"
                };
            }
            
            _logger.LogInformation("Successfully cancelled Mylerz shipment {TrackingNumber}", trackingNumber);
            
            return new CancelShipmentResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling Mylerz shipment {TrackingNumber}", trackingNumber);
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
        if (OfficialPublicContractUnavailable())
        {
            _logger.LogWarning(
                "Mylerz tracking blocked for {TrackingNumber} because no official public API contract is available.",
                trackingNumber);
            return await Task.FromResult(new ShipmentTrackingResult
            {
                Success = false,
                ErrorCode = ContractUnavailableErrorCode,
                ErrorMessage = ContractUnavailableErrorMessage
            });
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/shipments/{trackingNumber}/tracking");
            AddAuthHeaders(httpRequest, credentials);
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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
            
            var trackResponse = JsonSerializer.Deserialize<MylerzTrackingResponse>(responseBody, GetJsonOptions());
            
            if (trackResponse?.Success != true || trackResponse.Data == null)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_ERROR",
                    ErrorMessage = trackResponse?.Message ?? "Tracking failed",
                    RawResponse = responseBody
                };
            }
            
            var trackingData = trackResponse.Data;
            var currentStatus = MapMylerzStatus(trackingData.CurrentStatus ?? "");
            
            var result = new ShipmentTrackingResult
            {
                Success = true,
                CurrentStatus = currentStatus,
                CurrentStatusDescription = trackingData.CurrentStatusDescription,
                CurrentLocation = trackingData.CurrentLocation,
                EstimatedDeliveryDate = trackingData.EstimatedDelivery,
                ActualDeliveryDate = trackingData.ActualDelivery,
                RawResponse = responseBody,
                Events = trackingData.Events?.Select(e => new TrackingEvent
                {
                    Timestamp = e.Timestamp ?? DateTime.UtcNow,
                    Status = MapMylerzStatus(e.Status ?? ""),
                    Description = e.Description ?? "",
                    Location = e.Location,
                    CarrierStatusCode = e.Status
                }).OrderByDescending(e => e.Timestamp).ToList() ?? new List<TrackingEvent>()
            };
            
            // Check for COD collection
            if (currentStatus == ShipmentStatus.Delivered && trackingData.CodCollected == true)
            {
                result.CodCollected = true;
                result.CodCollectedAt = trackingData.CodCollectedAt ?? trackingData.ActualDelivery;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking Mylerz shipment {TrackingNumber}", trackingNumber);
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
        if (OfficialPublicContractUnavailable())
        {
            _logger.LogWarning("Mylerz credential validation blocked because no official public API contract is available.");
            return await Task.FromResult(new CarrierValidationResult
            {
                Success = false,
                ErrorCode = ContractUnavailableErrorCode,
                ErrorMessage = ContractUnavailableErrorMessage
            });
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            // Test credentials by fetching account info
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/account");
            AddAuthHeaders(httpRequest, credentials);
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var accountResponse = JsonSerializer.Deserialize<MylerzAccountResponse>(body, GetJsonOptions());
                
                return new CarrierValidationResult
                {
                    Success = true,
                    AccountName = accountResponse?.Data?.AccountName ?? $"Mylerz Account"
                };
            }
            
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "INVALID_CREDENTIALS",
                ErrorMessage = "Mylerz API credentials validation failed"
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
        if (OfficialPublicContractUnavailable())
        {
            return Array.Empty<ServiceLevelInfo>();
        }

        return new List<ServiceLevelInfo>
        {
            new ServiceLevelInfo
            {
                Code = "standard",
                Name = "Standard Delivery",
                Description = "Standard delivery within 2-4 business days",
                TypicalDaysMin = 2,
                TypicalDaysMax = 4
            },
            new ServiceLevelInfo
            {
                Code = "express",
                Name = "Express Delivery",
                Description = "Express delivery within 1-2 business days",
                TypicalDaysMin = 1,
                TypicalDaysMax = 2
            },
            new ServiceLevelInfo
            {
                Code = "same_day",
                Name = "Same Day Delivery",
                Description = "Same day delivery (order by 12 PM)",
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
        if (OfficialPublicContractUnavailable())
        {
            _logger.LogWarning("Mylerz webhook payload rejected because no official public webhook contract is available.");
            return null;
        }

        try
        {
            // Validate signature if secret is configured
            if (!string.IsNullOrEmpty(webhookSecret))
            {
                if (string.IsNullOrWhiteSpace(signature))
                {
                    _logger.LogWarning("Mylerz webhook signature missing but webhookSecret is configured");
                    return null;
                }

                if (!ValidateWebhookSignature(payload, signature, webhookSecret))
                {
                    _logger.LogWarning("Mylerz webhook signature validation failed");
                    return null;
                }
            }
            
            var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            
            // Extract tracking number
            if (!root.TryGetProperty("tracking_number", out var trackingProp) &&
                !root.TryGetProperty("shipment_id", out trackingProp) &&
                !root.TryGetProperty("awb", out trackingProp))
            {
                _logger.LogWarning("Mylerz webhook missing tracking number");
                return null;
            }
            
            var trackingNumber = trackingProp.GetString();
            if (string.IsNullOrEmpty(trackingNumber))
                return null;
            
            // Extract status
            string? statusCode = null;
            string? statusDescription = null;
            
            if (root.TryGetProperty("status", out var statusProp))
                statusCode = statusProp.GetString();
            if (root.TryGetProperty("status_description", out var descProp))
                statusDescription = descProp.GetString();
            
            if (string.IsNullOrEmpty(statusCode))
            {
                _logger.LogWarning("Mylerz webhook missing status for {TrackingNumber}", trackingNumber);
                return null;
            }
            
            var status = MapMylerzStatus(statusCode);
            
            // Extract timestamp
            DateTime? eventTime = null;
            if (root.TryGetProperty("timestamp", out var timeProp) ||
                root.TryGetProperty("event_time", out timeProp))
            {
                if (DateTime.TryParse(timeProp.GetString(), out var parsed))
                    eventTime = parsed;
            }
            
            // Check for COD collection
            var codCollected = false;
            DateTime? codCollectedAt = null;
            
            if (root.TryGetProperty("cod_collected", out var codProp))
            {
                codCollected = codProp.ValueKind == JsonValueKind.True;
                if (codCollected && root.TryGetProperty("cod_collected_at", out var codTimeProp))
                {
                    if (DateTime.TryParse(codTimeProp.GetString(), out var codTime))
                        codCollectedAt = codTime;
                }
            }
            
            // Extract location
            string? location = null;
            if (root.TryGetProperty("location", out var locProp))
                location = locProp.GetString();
            
            _logger.LogInformation(
                "Parsed Mylerz webhook: TrackingNumber={TrackingNumber}, Status={Status}, Code={Code}",
                trackingNumber, status, statusCode);
            
            return new WebhookParseResult
            {
                TrackingNumber = trackingNumber,
                Status = status,
                StatusDescription = statusDescription,
                EventTimestamp = eventTime ?? DateTime.UtcNow,
                Location = location,
                CodCollected = codCollected,
                CodCollectedAt = codCollectedAt,
                RawPayload = payload
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Mylerz webhook payload");
            return null;
        }
    }
    
    #region Helper Methods

    private static bool OfficialPublicContractUnavailable() => true;
    
    /// <summary>
    /// Maps Mylerz status codes to canonical POS ShipmentStatus.
    /// 
    /// CANONICAL RULES (ENFORCED):
    /// - Carrier "created" → ReadyForPickup (NEVER Pending)
    /// - Unknown status → log + return InTransit (safe default, no exception)
    /// </summary>
    private ShipmentStatus MapMylerzStatus(string mylerzStatus)
    {
        // CRITICAL: Map all Mylerz statuses to canonical POS statuses ONLY
        // No Mylerz enums leak outside this adapter
        return mylerzStatus.ToUpperInvariant().Replace("-", "_").Replace(" ", "_") switch
        {
            // Shipment created/accepted - CANONICAL RULE: Use ReadyForPickup, NOT Pending
            "CREATED" or "ORDER_CREATED" or "PENDING" or "NEW" or "CONFIRMED" 
                => ShipmentStatus.ReadyForPickup,
            
            // Pickup scheduled/waiting
            "PICKUP_SCHEDULED" or "WAITING_PICKUP" or "AWAITING_PICKUP" or "SCHEDULED" 
                => ShipmentStatus.ReadyForPickup,
            
            // Picked up by carrier
            "PICKED_UP" or "PICKEDUP" or "COLLECTED" or "IN_WAREHOUSE" 
                => ShipmentStatus.PickedUp,
            
            // In transit through network
            "IN_TRANSIT" or "INTRANSIT" or "AT_HUB" or "SORTING" or "TRANSFERRED" or "ON_ROUTE"
                => ShipmentStatus.InTransit,
            
            // Out for final delivery
            "OUT_FOR_DELIVERY" or "OUTFORDELIVERY" or "WITH_COURIER" or "ON_DELIVERY" 
                => ShipmentStatus.OutForDelivery,
            
            // Successfully delivered
            "DELIVERED" or "COMPLETED" or "DELIVERY_SUCCESS" 
                => ShipmentStatus.Delivered,
            
            // Cancelled
            "CANCELLED" or "CANCELED" or "VOIDED" 
                => ShipmentStatus.Cancelled,
            
            // Returned to sender
            "RETURNED" or "RETURN_TO_SENDER" or "RTS" or "RETURNING" 
                => ShipmentStatus.Returned,
            
            // Failed delivery
            "FAILED" or "DELIVERY_FAILED" or "EXCEPTION" or "UNDELIVERABLE" or "REJECTED"
                => ShipmentStatus.Failed,
            
            // Unknown status - log warning, return safe default
            _ => LogUnknownStatusAndReturnDefault(mylerzStatus)
        };
    }
    
    private ShipmentStatus LogUnknownStatusAndReturnDefault(string mylerzStatus)
    {
        _logger.LogWarning(
            "Unknown Mylerz status code: {Code}. Defaulting to InTransit. " +
            "Consider adding this code to the mapping table.",
            mylerzStatus);
        return ShipmentStatus.InTransit;
    }
    
    private static string GetBaseUrl(CarrierCredentials credentials)
    {
        return credentials.UseSandbox ? SandboxUrl : ProductionUrl;
    }
    
    private static void AddAuthHeaders(HttpRequestMessage request, CarrierCredentials credentials)
    {
        request.Headers.Add("Authorization", $"Bearer {credentials.ApiKey}");
        if (!string.IsNullOrEmpty(credentials.ApiSecret))
        {
            request.Headers.Add("X-Api-Secret", credentials.ApiSecret);
        }
        if (!string.IsNullOrEmpty(credentials.AccountId))
        {
            request.Headers.Add("X-Account-Id", credentials.AccountId);
        }
    }
    
    private static string MapServiceLevel(string? serviceLevel)
    {
        return (serviceLevel?.ToLowerInvariant()) switch
        {
            "express" => "EXPRESS",
            "same_day" => "SAME_DAY",
            _ => "STANDARD"
        };
    }
    
    private static string MapMylerzServiceToCanonical(string? mylerzService)
    {
        return (mylerzService?.ToUpperInvariant()) switch
        {
            "EXPRESS" => "express",
            "SAME_DAY" => "same_day",
            _ => "standard"
        };
    }
    
    private static string GetServiceName(string? serviceType)
    {
        return (serviceType?.ToUpperInvariant()) switch
        {
            "EXPRESS" => "Mylerz Express Delivery",
            "SAME_DAY" => "Mylerz Same Day",
            _ => "Mylerz Standard Delivery"
        };
    }
    
    private List<ShippingQuote> GetDefaultQuotes(ShippingQuoteRequest request)
    {
        return new List<ShippingQuote>
        {
            new ShippingQuote
            {
                CarrierCode = CarrierCode,
                ServiceLevel = "standard",
                ServiceName = "Mylerz Standard Delivery",
                Cost = request.IsCashOnDelivery ? 35m : 30m,
                Currency = "EGP",
                EstimatedDeliveryDaysMin = 2,
                EstimatedDeliveryDaysMax = 4,
                SupportsCod = true
            },
            new ShippingQuote
            {
                CarrierCode = CarrierCode,
                ServiceLevel = "express",
                ServiceName = "Mylerz Express Delivery",
                Cost = request.IsCashOnDelivery ? 55m : 50m,
                Currency = "EGP",
                EstimatedDeliveryDaysMin = 1,
                EstimatedDeliveryDaysMax = 2,
                SupportsCod = true
            },
            new ShippingQuote
            {
                CarrierCode = CarrierCode,
                ServiceLevel = "same_day",
                ServiceName = "Mylerz Same Day",
                Cost = request.IsCashOnDelivery ? 80m : 75m,
                Currency = "EGP",
                EstimatedDeliveryDaysMin = 0,
                EstimatedDeliveryDaysMax = 0,
                SupportsCod = true
            }
        };
    }
    
    private static bool ValidateWebhookSignature(string payload, string signature, string secret)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        try
        {
            var signatureBytes = Convert.FromHexString(signature.Trim());
            return hash.Length == signatureBytes.Length &&
                   System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash, signatureBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
    
    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
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
            if (doc.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                var firstError = errors.EnumerateArray().FirstOrDefault();
                if (firstError.TryGetProperty("message", out var errMsg))
                    return errMsg.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
    
    #endregion
}

#region Mylerz API Models

/// <summary>
/// Mylerz API models - INTERNAL to adapter only.
/// No Mylerz concepts leak to core domain.
/// </summary>

internal class MylerzAddress
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("address")]
    public string? Address { get; set; }
    
    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }
    
    [JsonPropertyName("city")]
    public string? City { get; set; }
    
    [JsonPropertyName("country")]
    public string? Country { get; set; }
    
    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }
    
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
    
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal class MylerzPackage
{
    [JsonPropertyName("weight")]
    public decimal Weight { get; set; }
    
    [JsonPropertyName("pieces")]
    public int Pieces { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class MylerzCod
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
    
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

// Request models
internal class MylerzRateRequest
{
    [JsonPropertyName("origin_city")]
    public string? OriginCity { get; set; }
    
    [JsonPropertyName("destination_city")]
    public string? DestinationCity { get; set; }
    
    [JsonPropertyName("weight")]
    public decimal Weight { get; set; }
    
    [JsonPropertyName("pieces")]
    public int Pieces { get; set; }
    
    [JsonPropertyName("is_cod")]
    public bool IsCod { get; set; }
    
    [JsonPropertyName("cod_amount")]
    public decimal CodAmount { get; set; }
    
    [JsonPropertyName("service_type")]
    public string? ServiceType { get; set; }
}

internal class MylerzCreateShipmentRequest
{
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
    
    [JsonPropertyName("internal_reference")]
    public string? InternalReference { get; set; }
    
    [JsonPropertyName("service_type")]
    public string? ServiceType { get; set; }
    
    [JsonPropertyName("pickup")]
    public MylerzAddress? Pickup { get; set; }
    
    [JsonPropertyName("delivery")]
    public MylerzAddress? Delivery { get; set; }
    
    [JsonPropertyName("package")]
    public MylerzPackage? Package { get; set; }
    
    [JsonPropertyName("cod")]
    public MylerzCod? Cod { get; set; }
    
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

// Response models
internal class MylerzApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

internal class MylerzRate
{
    [JsonPropertyName("quote_id")]
    public string? QuoteId { get; set; }
    
    [JsonPropertyName("service_type")]
    public string? ServiceType { get; set; }
    
    [JsonPropertyName("service_name")]
    public string? ServiceName { get; set; }
    
    [JsonPropertyName("total_cost")]
    public decimal TotalCost { get; set; }
    
    [JsonPropertyName("min_days")]
    public int MinDays { get; set; }
    
    [JsonPropertyName("max_days")]
    public int MaxDays { get; set; }
}

internal class MylerzRateData
{
    [JsonPropertyName("rates")]
    public List<MylerzRate>? Rates { get; set; }
}

internal class MylerzRateResponse : MylerzApiResponse<MylerzRateData> { }

internal class MylerzShipmentData
{
    [JsonPropertyName("shipment_id")]
    public string? ShipmentId { get; set; }
    
    [JsonPropertyName("tracking_number")]
    public string? TrackingNumber { get; set; }
    
    [JsonPropertyName("awb")]
    public string? Awb { get; set; }
    
    [JsonPropertyName("zone_code")]
    public string? ZoneCode { get; set; }
    
    [JsonPropertyName("hub_code")]
    public string? HubCode { get; set; }
    
    [JsonPropertyName("label_url")]
    public string? LabelUrl { get; set; }
    
    [JsonPropertyName("label_base64")]
    public string? LabelBase64 { get; set; }
}

internal class MylerzCreateShipmentResponse : MylerzApiResponse<MylerzShipmentData> { }

internal class MylerzTrackingEvent
{
    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("location")]
    public string? Location { get; set; }
}

internal class MylerzTrackingData
{
    [JsonPropertyName("current_status")]
    public string? CurrentStatus { get; set; }
    
    [JsonPropertyName("current_status_description")]
    public string? CurrentStatusDescription { get; set; }
    
    [JsonPropertyName("current_location")]
    public string? CurrentLocation { get; set; }
    
    [JsonPropertyName("estimated_delivery")]
    public DateTime? EstimatedDelivery { get; set; }
    
    [JsonPropertyName("actual_delivery")]
    public DateTime? ActualDelivery { get; set; }
    
    [JsonPropertyName("cod_collected")]
    public bool? CodCollected { get; set; }
    
    [JsonPropertyName("cod_collected_at")]
    public DateTime? CodCollectedAt { get; set; }
    
    [JsonPropertyName("events")]
    public List<MylerzTrackingEvent>? Events { get; set; }
}

internal class MylerzTrackingResponse : MylerzApiResponse<MylerzTrackingData> { }

internal class MylerzAccountData
{
    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }
    
    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }
}

internal class MylerzAccountResponse : MylerzApiResponse<MylerzAccountData> { }

#endregion
