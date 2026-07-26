using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.Core.DTOs.Customs;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// DHL Express shipping carrier adapter.
/// 
/// DHL is a global logistics leader providing express delivery services.
/// API Documentation: https://developer.dhl.com/
/// 
/// CARRIER-AGNOSTIC ARCHITECTURE:
/// ❌ NO DHL enums outside this file
/// ❌ NO DHL field names in core entities
/// ✅ Translates DHL → Canonical POS models
/// ✅ Follows canonical ShipmentStatus lifecycle
/// ✅ ReadyForPickup used after successful creation (NOT Pending)
/// 
/// STATUS MAPPING (MANDATORY):
/// DHL Status          → POS ShipmentStatus
/// ------------------------------------------
/// CREATED/ACCEPTED    → ReadyForPickup
/// PICKED_UP           → PickedUp
/// IN_TRANSIT          → InTransit
/// WITH_DELIVERY_COURIER → OutForDelivery
/// DELIVERED           → Delivered
/// CANCELLED           → Cancelled
/// RETURNED            → Returned
/// EXCEPTION           → Failed
/// </summary>
public sealed class DhlAdapter : IShippingCarrierAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DhlAdapter> _logger;
    
    private const string ProductionUrl = "https://express.api.dhl.com/mydhlapi";
    private const string SandboxUrl = "https://express.api.dhl.com/mydhlapi/test";
    
    public string CarrierCode => "dhl";
    public string DisplayName => "DHL Express";
    
    public DhlAdapter(
        IHttpClientFactory httpClientFactory,
        ILogger<DhlAdapter> logger)
    {
        _httpClient = httpClientFactory.CreateClient("DHL");
        _logger = logger;
    }
    
    public async Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (!HasRequiredCredentials(credentials))
        {
            return new ShippingQuoteResult
            {
                Success = false,
                ErrorCode = "DHL_CREDENTIALS_INVALID",
                ErrorMessage = "DHL MyDHL API key, secret, and account number are required."
            };
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            // Build DHL rate request
            var dhlRequest = new DhlRateRequest
            {
                CustomerDetails = new DhlCustomerDetails
                {
                    ShipperDetails = new DhlAddressDetails
                    {
                        CityName = request.OriginCity,
                        CountryCode = request.OriginCountry,
                        PostalCode = request.OriginZipCode
                    },
                    ReceiverDetails = new DhlAddressDetails
                    {
                        CityName = request.DestinationCity,
                        CountryCode = request.DestinationCountry,
                        PostalCode = request.DestinationZipCode
                    }
                },
                Accounts = new List<DhlAccount>
                {
                    new DhlAccount
                    {
                        TypeCode = "shipper",
                        Number = credentials.AccountId ?? ""
                    }
                },
                PlannedShippingDateAndTime = DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss") + " GMT+00:00",
                UnitOfMeasurement = "metric",
                IsCustomsDeclarable = IsInternational(request.OriginCountry, request.DestinationCountry),
                Packages = new List<DhlPackage>
                {
                    new DhlPackage
                    {
                        Weight = request.WeightKg,
                        Dimensions = new DhlDimensions { Length = 30, Width = 20, Height = 10 }
                    }
                }
            };
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rates");
            AddAuthHeaders(httpRequest, credentials);
            httpRequest.Content = JsonContent.Create(dhlRequest, options: GetJsonOptions());
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("DHL rate calculation failed: {StatusCode} - {Body}", 
                    response.StatusCode, responseBody);
                
                return new ShippingQuoteResult
                {
                    Success = false,
                    ErrorCode = "DHL_RATE_API_ERROR",
                    ErrorMessage = TryParseError(responseBody) ?? $"HTTP {(int)response.StatusCode}"
                };
            }
            
            var rateResponse = JsonSerializer.Deserialize<DhlRateResponse>(responseBody, GetJsonOptions());
            
            if (rateResponse?.Products == null || !rateResponse.Products.Any())
            {
                return new ShippingQuoteResult
                {
                    Success = false,
                    ErrorCode = "DHL_RATE_RESPONSE_INVALID",
                    ErrorMessage = "DHL returned no rated products."
                };
            }
            
            var quotes = rateResponse.Products.Select(product => new ShippingQuote
            {
                CarrierCode = CarrierCode,
                ServiceLevel = MapDhlProductToServiceLevel(product.ProductCode),
                ServiceName = product.ProductName ?? GetServiceName(product.ProductCode),
                Cost = product.TotalPrice?.FirstOrDefault()?.Price ?? 0,
                Currency = product.TotalPrice?.FirstOrDefault()?.CurrencyType ?? "USD",
                EstimatedDeliveryDaysMin = product.DeliveryCapabilities?.EstimatedDeliveryDateAndTime != null ? 1 : 2,
                EstimatedDeliveryDaysMax = product.DeliveryCapabilities?.EstimatedDeliveryDateAndTime != null ? 3 : 5,
                SupportsCod = false, // DHL typically prepaid only
                CarrierQuoteId = product.ProductCode
            }).ToList();
            
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
            _logger.LogError(ex, "DHL rate calculation HTTP error");
            return new ShippingQuoteResult
            {
                Success = false,
                ErrorCode = "DHL_RATE_HTTP_ERROR",
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DHL rate calculation unexpected error");
            return new ShippingQuoteResult
            {
                Success = false,
                ErrorCode = "DHL_RATE_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }
    
    public async Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var isInternational = IsInternational(request.OriginCountry, request.RecipientCountry);

        if (!HasRequiredCredentials(credentials))
        {
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "DHL_CREDENTIALS_INVALID",
                ErrorMessage = "DHL MyDHL API key, secret, and account number are required."
            };
        }

        if (request.WeightKg <= 0 ||
            request.PackageCount != 1 ||
            string.IsNullOrWhiteSpace(request.OriginCountry) ||
            string.IsNullOrWhiteSpace(request.RecipientCountry))
        {
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "DHL_REQUEST_CONTRACT_INVALID",
                ErrorMessage = "DHL shipment creation requires positive weight, one package, and ISO origin/destination countries."
            };
        }

        DhlExportDeclaration? exportDeclaration = null;
        if (isInternational)
        {
            var customsValidationFailure = TryBuildExportDeclaration(request.Customs, out exportDeclaration);
            if (customsValidationFailure is not null)
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = customsValidationFailure,
                    ErrorMessage = "International DHL shipment creation requires a complete admitted shipment customs snapshot."
                };
            }
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            // Build DHL shipment creation request
            var dhlRequest = new DhlCreateShipmentRequest
            {
                PlannedShippingDateAndTime = DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss") + " GMT+00:00",
                Pickup = new DhlPickup
                {
                    IsRequested = false // Pickup scheduled separately
                },
                ProductCode = MapServiceLevelToProduct(request.ServiceLevel, isInternational),
                Accounts = new List<DhlAccount>
                {
                    new DhlAccount
                    {
                        TypeCode = "shipper",
                        Number = credentials.AccountId ?? ""
                    }
                },
                CustomerDetails = new DhlShipmentCustomerDetails
                {
                    ShipperDetails = new DhlParty
                    {
                        PostalAddress = new DhlPostalAddress
                        {
                            AddressLine1 = request.OriginAddress,
                            CityName = request.OriginCity,
                            CountryCode = request.OriginCountry,
                            PostalCode = request.OriginZipCode
                        },
                        ContactInformation = new DhlContactInfo
                        {
                            FullName = request.OriginName,
                            Phone = request.OriginPhone,
                            CompanyName = request.OriginName
                        }
                    },
                    ReceiverDetails = new DhlParty
                    {
                        PostalAddress = new DhlPostalAddress
                        {
                            AddressLine1 = request.RecipientAddress,
                            AddressLine2 = request.RecipientAddressLine2,
                            CityName = request.RecipientCity,
                            CountryCode = request.RecipientCountry,
                            PostalCode = request.RecipientZipCode
                        },
                        ContactInformation = new DhlContactInfo
                        {
                            FullName = request.RecipientName,
                            Phone = request.RecipientPhone,
                            Email = request.RecipientEmail,
                            CompanyName = request.RecipientName
                        }
                    }
                },
                Content = new DhlContent
                {
                    Packages = new List<DhlShipmentPackage>
                    {
                        new DhlShipmentPackage
                        {
                            Weight = request.WeightKg,
                            Dimensions = new DhlDimensions
                            {
                                Length = request.LengthCm ?? 30,
                                Width = request.WidthCm ?? 20,
                                Height = request.HeightCm ?? 10
                            },
                            CustomerReferences = new List<DhlReference>
                            {
                                new DhlReference { Value = request.OrderNumber, TypeCode = "CU" }
                            },
                            Description = request.PackageDescription ?? "Package"
                        }
                    },
                    IsCustomsDeclarable = isInternational,
                    DeclaredValue = isInternational
                        ? request.Customs!.Lines.Sum(line => line.DeclaredValue)
                        : request.DeclaredValue,
                    DeclaredValueCurrency = isInternational
                        ? request.Customs!.Lines[0].Currency
                        : request.DeclaredValue.HasValue ? request.Currency : null,
                    Description = request.PackageDescription ?? "E-commerce shipment",
                    IncoTerm = "DAP",
                    UnitOfMeasurement = "metric",
                    ExportDeclaration = exportDeclaration
                },
                ShipmentNotification = string.IsNullOrWhiteSpace(request.RecipientEmail)
                    ? null
                    : new List<DhlNotification>
                    {
                        new()
                        {
                            TypeCode = "email",
                            ReceiverEmail = request.RecipientEmail
                        }
                    },
                CustomerReferences = new List<DhlReference>
                {
                    new DhlReference { Value = request.OrderNumber, TypeCode = "CU" },
                    new DhlReference { Value = request.InternalShipmentId, TypeCode = "AAO" }
                },
                GetRateEstimates = false,
                GetTransliteratedResponse = false,
                OutputImageProperties = new DhlOutputImageProperties
                {
                    EncodingFormat = "pdf",
                    ImageOptions = new List<DhlImageOption>
                    {
                        new DhlImageOption { TypeCode = "label", TemplateName = "ECOM26_84_001" }
                    }
                }
            };
            
            _logger.LogInformation(
                "Creating DHL shipment for order {OrderNumber}",
                request.OrderNumber);
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/shipments");
            AddAuthHeaders(httpRequest, credentials);
            httpRequest.Content = JsonContent.Create(dhlRequest, options: GetJsonOptions());
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "DHL API error: {StatusCode} - {Body}",
                    response.StatusCode, responseBody);
                    
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "DHL_API_ERROR",
                    ErrorMessage = TryParseError(responseBody) ?? $"HTTP {(int)response.StatusCode}",
                    RawResponse = responseBody
                };
            }
            
            var dhlResponse = JsonSerializer.Deserialize<DhlCreateShipmentResponse>(responseBody, GetJsonOptions());
            
            if (string.IsNullOrWhiteSpace(dhlResponse?.ShipmentTrackingNumber))
            {
                var errorMessage = "Shipment creation failed - no tracking number returned";
                _logger.LogError("DHL shipment creation failed: {Error}", errorMessage);
                
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "DHL_CREATE_ERROR",
                    ErrorMessage = errorMessage,
                    RawResponse = responseBody
                };
            }
            
            var trackingNumber = dhlResponse.ShipmentTrackingNumber.Trim();
            if (!string.IsNullOrWhiteSpace(dhlResponse.ShipmentTrackingNumber) &&
                dhlResponse.ShipmentTrackingNumber.Any(char.IsWhiteSpace))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "DHL_RESPONSE_IDENTITY_INVALID",
                    ErrorMessage = "DHL returned a malformed shipment tracking number.",
                    RawResponse = responseBody
                };
            }

            var labelDoc = dhlResponse.Documents?.FirstOrDefault(d => d.TypeCode == "label");
            
            // Build tracking URL
            var trackingUrl = $"https://www.dhl.com/global-en/home/tracking.html?tracking-id={trackingNumber}";
            
            // Store DHL-specific metadata
            var metadata = JsonSerializer.Serialize(new
            {
                dispatchConfirmationNumber = dhlResponse.DispatchConfirmationNumber,
                serviceType = dhlResponse.ServiceType,
                estimatedDeliveryDate = dhlResponse.EstimatedDeliveryDate,
                cancelPickupUrl = dhlResponse.CancelPickupUrl
            });
            
            _logger.LogInformation(
                "Created DHL shipment. TrackingNumber: {TrackingNumber}, OrderNumber: {OrderNumber}",
                trackingNumber, request.OrderNumber);
            
            // Determine ExternalReferenceId: prefer DispatchConfirmationNumber, fallback to trackingNumber
            var externalReferenceId = !string.IsNullOrWhiteSpace(dhlResponse.DispatchConfirmationNumber)
                ? dhlResponse.DispatchConfirmationNumber.Trim()
                : trackingNumber;
            
            return new CreateShipmentResult
            {
                Success = true,
                ExternalReferenceId = externalReferenceId,
                TrackingNumber = trackingNumber,
                TrackingUrl = trackingUrl,
                AwbNumber = trackingNumber,
                // CANONICAL RULE: Successful carrier creation → ReadyForPickup (NEVER Pending)
                InitialStatus = ShipmentStatus.ReadyForPickup,
                LabelUrl = null, // DHL returns base64 encoded labels
                LabelData = labelDoc?.Content,
                LabelFormat = "PDF",
                CarrierMetadata = metadata,
                RawResponse = responseBody
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "DHL HTTP request failed");
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "HTTP_ERROR",
                ErrorMessage = $"Connection error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating DHL shipment");
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
        // MyDHL REST v3.3.1 supports cancellation of pickup bookings through
        // DELETE /pickups/{dispatchConfirmationNumber}; it does not publish a
        // DELETE shipment operation. Never treat a guessed route as proof that
        // the external shipment was cancelled.
        await Task.CompletedTask;
        return new CancelShipmentResult
        {
            Success = false,
            ErrorCode = "DHL_SHIPMENT_CANCELLATION_UNSUPPORTED",
            ErrorMessage = "DHL MyDHL REST does not publish shipment cancellation for this integration."
        };
    }
    
    public async Task<ShipmentTrackingResult> GetTrackingAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (!HasRequiredCredentials(credentials))
        {
            return new ShipmentTrackingResult
            {
                Success = false,
                ErrorCode = "DHL_CREDENTIALS_INVALID",
                ErrorMessage = "DHL MyDHL API key, secret, and account number are required."
            };
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, 
                $"{baseUrl}/tracking?shipmentTrackingNumber={trackingNumber}");
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
            
            var trackResponse = JsonSerializer.Deserialize<DhlTrackingResponse>(responseBody, GetJsonOptions());
            var shipment = trackResponse?.Shipments?.FirstOrDefault();
            
            if (shipment == null)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "NO_TRACKING_DATA",
                    ErrorMessage = "No tracking information available",
                    RawResponse = responseBody
                };
            }

            if (!string.Equals(
                    shipment.ShipmentTrackingNumber?.Trim(),
                    trackingNumber.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_IDENTITY_MISMATCH",
                    ErrorMessage = "DHL tracking response does not match the requested shipment.",
                    RawResponse = responseBody
                };
            }
            
            var latestEvent = shipment.Events?.OrderByDescending(e => e.Timestamp).FirstOrDefault();
            var mappedCurrentStatus = TryMapDhlStatus(
                latestEvent?.StatusCode ?? shipment.Status?.StatusCode ?? "");
            if (!mappedCurrentStatus.HasValue)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_STATUS_UNADMITTED",
                    ErrorMessage = "DHL returned an unknown tracking status.",
                    RawResponse = responseBody
                };
            }

            var currentStatus = mappedCurrentStatus.Value;
            var mappedEvents = new List<TrackingEvent>();
            foreach (var trackingEvent in shipment.Events ?? new List<DhlTrackingEvent>())
            {
                var mappedStatus = TryMapDhlStatus(trackingEvent.StatusCode ?? "");
                if (!mappedStatus.HasValue)
                {
                    return new ShipmentTrackingResult
                    {
                        Success = false,
                        ErrorCode = "TRACKING_STATUS_UNADMITTED",
                        ErrorMessage = "DHL returned an unknown tracking event status.",
                        RawResponse = responseBody
                    };
                }

                mappedEvents.Add(new TrackingEvent
                {
                    Timestamp = trackingEvent.Timestamp ?? DateTime.UtcNow,
                    Status = mappedStatus.Value,
                    Description = trackingEvent.Description ?? "",
                    Location = trackingEvent.ServiceArea?.Description,
                    CarrierStatusCode = trackingEvent.StatusCode
                });
            }
            
            var result = new ShipmentTrackingResult
            {
                Success = true,
                CurrentStatus = currentStatus,
                CurrentStatusDescription = latestEvent?.Description ?? shipment.Status?.Description,
                CurrentLocation = latestEvent?.ServiceArea?.Description,
                EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
                ActualDeliveryDate = currentStatus == ShipmentStatus.Delivered 
                    ? latestEvent?.Timestamp 
                    : null,
                RawResponse = responseBody,
                Events = mappedEvents.OrderByDescending(e => e.Timestamp).ToList()
            };
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking DHL shipment {TrackingNumber}", trackingNumber);
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
        await Task.CompletedTask;

        if (!HasRequiredCredentials(credentials))
        {
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "DHL_CREDENTIALS_INVALID",
                ErrorMessage = "DHL MyDHL API key, secret, and account number are required."
            };
        }

        return new CarrierValidationResult
        {
            Success = true,
            AccountName = $"DHL Account ({credentials.AccountId})"
        };
    }
    
    public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels()
    {
        return new List<ServiceLevelInfo>
        {
            new ServiceLevelInfo
            {
                Code = "express",
                Name = "DHL Express Worldwide",
                Description = "International express delivery (1-3 business days)",
                TypicalDaysMin = 1,
                TypicalDaysMax = 3
            },
            new ServiceLevelInfo
            {
                Code = "economy",
                Name = "DHL Economy Select",
                Description = "Economy international delivery (4-8 business days)",
                TypicalDaysMin = 4,
                TypicalDaysMax = 8
            },
            new ServiceLevelInfo
            {
                Code = "same_day",
                Name = "DHL Same Day",
                Description = "Same day express delivery (selected cities)",
                TypicalDaysMin = 0,
                TypicalDaysMax = 0
            },
            new ServiceLevelInfo
            {
                Code = "domestic",
                Name = "DHL Domestic Express",
                Description = "Domestic express delivery (1-2 business days)",
                TypicalDaysMin = 1,
                TypicalDaysMax = 2
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
            // Validate signature if secret is configured
            if (!string.IsNullOrEmpty(webhookSecret))
            {
                if (string.IsNullOrWhiteSpace(signature))
                {
                    _logger.LogWarning("DHL webhook signature missing but webhookSecret is configured");
                    return null;
                }

                if (!ValidateWebhookSignature(payload, signature, webhookSecret))
                {
                    _logger.LogWarning("DHL webhook signature validation failed");
                    return null;
                }
            }
            
            var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            
            // DHL webhook structure
            // Extract tracking number
            string? trackingNumber = null;
            if (root.TryGetProperty("shipmentTrackingNumber", out var trackProp))
                trackingNumber = trackProp.GetString();
            else if (root.TryGetProperty("awbNumber", out trackProp))
                trackingNumber = trackProp.GetString();
            else if (root.TryGetProperty("pieceId", out trackProp))
                trackingNumber = trackProp.GetString();
            
            if (string.IsNullOrEmpty(trackingNumber))
            {
                _logger.LogWarning("DHL webhook missing tracking number");
                return null;
            }
            
            // Extract status
            string? statusCode = null;
            string? statusDescription = null;
            
            if (root.TryGetProperty("status", out var statusProp))
            {
                if (statusProp.ValueKind == JsonValueKind.Object)
                {
                    statusCode = statusProp.TryGetProperty("statusCode", out var codeProp) 
                        ? codeProp.GetString() : null;
                    statusDescription = statusProp.TryGetProperty("description", out var descProp) 
                        ? descProp.GetString() : null;
                }
                else
                {
                    statusCode = statusProp.GetString();
                }
            }
            
            if (string.IsNullOrEmpty(statusCode))
            {
                _logger.LogWarning("DHL webhook missing status for {TrackingNumber}", trackingNumber);
                return null;
            }
            
            var status = TryMapDhlStatus(statusCode);
            if (!status.HasValue)
            {
                _logger.LogWarning(
                    "DHL webhook returned unknown status code {Code} for {TrackingNumber}",
                    statusCode,
                    trackingNumber);
                return null;
            }
            
            // Extract timestamp
            DateTime? eventTime = null;
            if (root.TryGetProperty("timestamp", out var timeProp) ||
                root.TryGetProperty("eventTime", out timeProp))
            {
                if (DateTime.TryParse(timeProp.GetString(), out var parsed))
                    eventTime = parsed;
            }
            
            // Extract location
            string? location = null;
            if (root.TryGetProperty("serviceArea", out var areaProp) &&
                areaProp.TryGetProperty("description", out var areaDescProp))
            {
                location = areaDescProp.GetString();
            }
            
            _logger.LogInformation(
                "Parsed DHL webhook: TrackingNumber={TrackingNumber}, Status={Status}, Code={Code}",
                trackingNumber, status.Value, statusCode);
            
            return new WebhookParseResult
            {
                TrackingNumber = trackingNumber,
                Status = status.Value,
                StatusDescription = statusDescription,
                EventTimestamp = eventTime ?? DateTime.UtcNow,
                Location = location,
                CodCollected = false, // DHL typically prepaid
                RawPayload = payload
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse DHL webhook payload");
            return null;
        }
    }
    
    #region Helper Methods
    
    /// <summary>
    /// Maps DHL status codes to canonical POS ShipmentStatus.
    /// 
    /// CANONICAL RULES (ENFORCED):
    /// - Carrier "created" → ReadyForPickup (NEVER Pending)
    /// - Unknown status -> reject before lifecycle admission
    /// </summary>
    private ShipmentStatus? TryMapDhlStatus(string dhlStatus)
    {
        // CRITICAL: Map all DHL statuses to canonical POS statuses ONLY
        // No DHL enums leak outside this adapter
        return dhlStatus.ToUpperInvariant().Replace("-", "_").Replace(" ", "_") switch
        {
            // Shipment created/accepted - CANONICAL RULE: Use ReadyForPickup, NOT Pending
            "CREATED" or "SHIPMENT_ACCEPTED" or "SHIPMENT_CREATED" or "LA" or "PL" or "AF"
                => ShipmentStatus.ReadyForPickup,
            
            // Picked up by carrier
            "PICKED_UP" or "PU" or "RW" or "AR"
                => ShipmentStatus.PickedUp,
            
            // In transit through network
            "IN_TRANSIT" or "TRANSIT" or "DF" or "CC" or "FD" or "TF" or "HP" or "DD" or "OH"
                => ShipmentStatus.InTransit,
            
            // Out for final delivery
            "WITH_DELIVERY_COURIER" or "OUT_FOR_DELIVERY" or "WC" or "CR" or "MD"
                => ShipmentStatus.OutForDelivery,
            
            // Successfully delivered
            "DELIVERED" or "OK" or "DL"
                => ShipmentStatus.Delivered,
            
            // Cancelled
            "CANCELLED" or "CANCELED" or "CA"
                => ShipmentStatus.Cancelled,
            
            // Returned to sender
            "RETURNED" or "RETURN_TO_SENDER" or "RR" or "RT" or "BR"
                => ShipmentStatus.Returned,
            
            // Failed delivery / Exception
            "EXCEPTION" or "FAILED" or "DELIVERY_FAILED" or "MS" or "NH" or "CM" or "BA" or "BN"
                => ShipmentStatus.Failed,
            
            // Unknown status is not admitted by authoritative tracking sync.
            _ => null
        };
    }
    
    private static string GetBaseUrl(CarrierCredentials credentials)
    {
        return credentials.UseSandbox ? SandboxUrl : ProductionUrl;
    }

    private static bool HasRequiredCredentials(CarrierCredentials credentials) =>
        !string.IsNullOrWhiteSpace(credentials.ApiKey) &&
        !string.IsNullOrWhiteSpace(credentials.ApiSecret) &&
        !string.IsNullOrWhiteSpace(credentials.AccountId);
    
    private static void AddAuthHeaders(HttpRequestMessage request, CarrierCredentials credentials)
    {
        // DHL uses Basic Auth with API Key as username and API Secret as password
        var authBytes = System.Text.Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}");
        var authBase64 = Convert.ToBase64String(authBytes);
        request.Headers.Add("Authorization", $"Basic {authBase64}");
        request.Headers.Add("Accept", "application/json");
    }
    
    private static bool IsInternational(string originCountry, string destCountry)
    {
        return !string.Equals(originCountry, destCountry, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryBuildExportDeclaration(
        CarrierNeutralCustomsDto? customs,
        out DhlExportDeclaration? exportDeclaration)
    {
        exportDeclaration = null;
        if (customs is null || customs.Lines.Count == 0)
        {
            return "DHL_CUSTOMS_SNAPSHOT_MISSING";
        }

        if (customs.ShipmentCustomsSnapshotId == Guid.Empty ||
            customs.ShipmentId == Guid.Empty ||
            customs.OrderId == Guid.Empty ||
            customs.SnapshotVersion != 1 ||
            customs.DtoVersion != 1)
        {
            return "DHL_CUSTOMS_VERSION_MISMATCH";
        }

        if (!IsIso2(customs.OriginCountryIso2) ||
            !IsIso2(customs.DestinationCountryIso2) ||
            customs.ShipmentGrossWeight <= 0 ||
            !TryMapDhlWeightUnit(customs.ShipmentGrossWeightUnit, out _))
        {
            return "DHL_CUSTOMS_SNAPSHOT_INCOMPLETE";
        }

        var currency = customs.Lines[0].Currency;
        if (!IsCurrency(currency))
        {
            return "DHL_CUSTOMS_SNAPSHOT_INCOMPLETE";
        }

        var lineItems = new List<DhlExportLineItem>();
        for (var index = 0; index < customs.Lines.Count; index++)
        {
            var line = customs.Lines[index];
            if (line.OrderItemId == Guid.Empty ||
                line.ProductId == Guid.Empty ||
                line.CustomsEvidenceId == Guid.Empty ||
                line.CustomsEvidenceVersionId == Guid.Empty ||
                line.EvidenceVersionNumber <= 0 ||
                string.IsNullOrWhiteSpace(line.HsCode) ||
                string.IsNullOrWhiteSpace(line.CommodityDescription) ||
                !IsIso2(line.CountryOfOriginIso2) ||
                (line.CountryOfManufactureIso2 is not null && !IsIso2(line.CountryOfManufactureIso2)) ||
                line.DeclaredValue <= 0 ||
                !string.Equals(line.Currency, currency, StringComparison.OrdinalIgnoreCase) ||
                line.Quantity <= 0 ||
                line.NetWeight <= 0)
            {
                return "DHL_CUSTOMS_SNAPSHOT_INCOMPLETE";
            }

            if (!TryMapDhlQuantityUnit(line.QuantityUnit, out var dhlQuantityUnit) ||
                !TryMapDhlWeightUnit(line.NetWeightUnit, out var dhlWeightUnit))
            {
                return "DHL_CUSTOMS_ENUM_UNSUPPORTED";
            }

            lineItems.Add(new DhlExportLineItem
            {
                Number = index + 1,
                Description = line.CommodityDescription.Trim(),
                Price = line.DeclaredValue,
                Quantity = new DhlExportQuantity
                {
                    Value = line.Quantity,
                    UnitOfMeasurement = dhlQuantityUnit
                },
                CommodityCodes = new List<DhlCommodityCode>
                {
                    new() { TypeCode = "outbound", Value = line.HsCode.Trim() }
                },
                ManufacturerCountry = line.CountryOfManufactureIso2?.Trim().ToUpperInvariant(),
                Weight = new DhlExportWeight
                {
                    NetValue = line.NetWeight,
                    GrossValue = line.NetWeight,
                    UnitOfMeasurement = dhlWeightUnit
                }
            });
        }

        exportDeclaration = new DhlExportDeclaration
        {
            LineItems = lineItems
        };
        return null;
    }

    private static bool IsIso2(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length == 2 &&
        value.Trim().All(char.IsLetter);

    private static bool IsCurrency(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length == 3 &&
        value.Trim().All(char.IsLetter);

    private static bool TryMapDhlQuantityUnit(string? unit, out string dhlUnit)
    {
        dhlUnit = unit?.Trim().ToLowerInvariant() switch
        {
            "pcs" or "pc" or "piece" or "pieces" or "ea" or "each" => "PCS",
            _ => string.Empty
        };
        return dhlUnit.Length > 0;
    }

    private static bool TryMapDhlWeightUnit(string? unit, out string dhlUnit)
    {
        dhlUnit = unit?.Trim().ToLowerInvariant() switch
        {
            "kg" or "g" => "metric",
            "lb" or "oz" => "imperial",
            _ => string.Empty
        };
        return dhlUnit.Length > 0;
    }
    
    private static string MapServiceLevelToProduct(string? serviceLevel, bool isInternational)
    {
        return (serviceLevel?.ToLowerInvariant()) switch
        {
            "express" => isInternational ? "P" : "N", // Express Worldwide or Domestic Express
            "economy" => "H", // Economy Select
            "same_day" => "1", // Same Day
            "domestic" => "N", // Domestic Express
            _ => isInternational ? "P" : "N"
        };
    }
    
    private static string MapDhlProductToServiceLevel(string? productCode)
    {
        return productCode?.ToUpperInvariant() switch
        {
            "P" or "D" or "U" => "express",
            "H" or "W" => "economy",
            "1" or "I" => "same_day",
            "N" => "domestic",
            _ => "express"
        };
    }
    
    private static string GetServiceName(string? productCode)
    {
        return productCode?.ToUpperInvariant() switch
        {
            "P" => "DHL Express Worldwide",
            "D" => "DHL Express Worldwide Document",
            "H" => "DHL Economy Select",
            "1" => "DHL Same Day",
            "N" => "DHL Domestic Express",
            _ => "DHL Express"
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
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
    }
    
    private static string? TryParseError(string responseBody)
    {
        try
        {
            var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
                return detail.GetString();
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString();
            if (doc.RootElement.TryGetProperty("title", out var title))
                return title.GetString();
            if (doc.RootElement.TryGetProperty("additionalDetails", out var additionalDetails) &&
                additionalDetails.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in additionalDetails.EnumerateArray())
                {
                    if (item.TryGetProperty("message", out var message))
                        return message.GetString();
                }
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

#region DHL API Models

/// <summary>
/// DHL API models - INTERNAL to adapter only.
/// No DHL concepts leak to core domain.
/// </summary>

internal class DhlAccount
{
    [JsonPropertyName("typeCode")]
    public string? TypeCode { get; set; }
    
    [JsonPropertyName("number")]
    public string? Number { get; set; }
}

internal class DhlAddressDetails
{
    [JsonPropertyName("cityName")]
    public string? CityName { get; set; }
    
    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }
    
    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }
}

internal class DhlPostalAddress : DhlAddressDetails
{
    [JsonPropertyName("addressLine1")]
    public string? AddressLine1 { get; set; }
    
    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; set; }
    
    [JsonPropertyName("addressLine3")]
    public string? AddressLine3 { get; set; }
}

internal class DhlContactInfo
{
    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }
    
    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }
    
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
    
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal class DhlParty
{
    [JsonPropertyName("postalAddress")]
    public DhlPostalAddress? PostalAddress { get; set; }
    
    [JsonPropertyName("contactInformation")]
    public DhlContactInfo? ContactInformation { get; set; }
}

internal class DhlCustomerDetails
{
    [JsonPropertyName("shipperDetails")]
    public DhlAddressDetails? ShipperDetails { get; set; }
    
    [JsonPropertyName("receiverDetails")]
    public DhlAddressDetails? ReceiverDetails { get; set; }
}

internal class DhlShipmentCustomerDetails
{
    [JsonPropertyName("shipperDetails")]
    public DhlParty? ShipperDetails { get; set; }
    
    [JsonPropertyName("receiverDetails")]
    public DhlParty? ReceiverDetails { get; set; }
}

internal class DhlDimensions
{
    [JsonPropertyName("length")]
    public decimal Length { get; set; }
    
    [JsonPropertyName("width")]
    public decimal Width { get; set; }
    
    [JsonPropertyName("height")]
    public decimal Height { get; set; }
}

internal class DhlPackage
{
    [JsonPropertyName("weight")]
    public decimal Weight { get; set; }
    
    [JsonPropertyName("dimensions")]
    public DhlDimensions? Dimensions { get; set; }
}

internal class DhlReference
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }
    
    [JsonPropertyName("typeCode")]
    public string? TypeCode { get; set; }
}

internal class DhlShipmentPackage : DhlPackage
{
    [JsonPropertyName("customerReferences")]
    public List<DhlReference>? CustomerReferences { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class DhlPickup
{
    [JsonPropertyName("isRequested")]
    public bool IsRequested { get; set; }
}

internal class DhlContent
{
    [JsonPropertyName("packages")]
    public List<DhlShipmentPackage>? Packages { get; set; }
    
    [JsonPropertyName("isCustomsDeclarable")]
    public bool IsCustomsDeclarable { get; set; }
    
    [JsonPropertyName("declaredValue")]
    public decimal? DeclaredValue { get; set; }
    
    [JsonPropertyName("declaredValueCurrency")]
    public string? DeclaredValueCurrency { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("incoterm")]
    public string? IncoTerm { get; set; }
    
    [JsonPropertyName("unitOfMeasurement")]
    public string? UnitOfMeasurement { get; set; }

    [JsonPropertyName("exportDeclaration")]
    public DhlExportDeclaration? ExportDeclaration { get; set; }
}

internal class DhlExportDeclaration
{
    [JsonPropertyName("lineItems")]
    public List<DhlExportLineItem>? LineItems { get; set; }
}

internal class DhlExportLineItem
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("quantity")]
    public DhlExportQuantity? Quantity { get; set; }

    [JsonPropertyName("commodityCodes")]
    public List<DhlCommodityCode>? CommodityCodes { get; set; }

    [JsonPropertyName("manufacturerCountry")]
    public string? ManufacturerCountry { get; set; }

    [JsonPropertyName("exportReasonType")]
    public string? ExportReasonType { get; set; }

    [JsonPropertyName("weight")]
    public DhlExportWeight? Weight { get; set; }
}

internal class DhlExportQuantity
{
    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("unitOfMeasurement")]
    public string? UnitOfMeasurement { get; set; }
}

internal class DhlCommodityCode
{
    [JsonPropertyName("typeCode")]
    public string? TypeCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal class DhlExportWeight
{
    [JsonPropertyName("netValue")]
    public decimal NetValue { get; set; }

    [JsonPropertyName("grossValue")]
    public decimal GrossValue { get; set; }

    [JsonPropertyName("unitOfMeasurement")]
    public string? UnitOfMeasurement { get; set; }
}

internal class DhlNotification
{
    [JsonPropertyName("typeCode")]
    public string? TypeCode { get; set; }
    
    [JsonPropertyName("receiverEmail")]
    public string? ReceiverEmail { get; set; }
}

internal class DhlImageOption
{
    [JsonPropertyName("typeCode")]
    public string? TypeCode { get; set; }
    
    [JsonPropertyName("templateName")]
    public string? TemplateName { get; set; }
}

internal class DhlOutputImageProperties
{
    [JsonPropertyName("encodingFormat")]
    public string? EncodingFormat { get; set; }
    
    [JsonPropertyName("imageOptions")]
    public List<DhlImageOption>? ImageOptions { get; set; }
}

// Request models
internal class DhlRateRequest
{
    [JsonPropertyName("customerDetails")]
    public DhlCustomerDetails? CustomerDetails { get; set; }
    
    [JsonPropertyName("accounts")]
    public List<DhlAccount>? Accounts { get; set; }
    
    [JsonPropertyName("plannedShippingDateAndTime")]
    public string? PlannedShippingDateAndTime { get; set; }
    
    [JsonPropertyName("unitOfMeasurement")]
    public string? UnitOfMeasurement { get; set; }
    
    [JsonPropertyName("isCustomsDeclarable")]
    public bool IsCustomsDeclarable { get; set; }
    
    [JsonPropertyName("packages")]
    public List<DhlPackage>? Packages { get; set; }
}

internal class DhlCreateShipmentRequest
{
    [JsonPropertyName("plannedShippingDateAndTime")]
    public string? PlannedShippingDateAndTime { get; set; }
    
    [JsonPropertyName("pickup")]
    public DhlPickup? Pickup { get; set; }
    
    [JsonPropertyName("productCode")]
    public string? ProductCode { get; set; }
    
    [JsonPropertyName("accounts")]
    public List<DhlAccount>? Accounts { get; set; }
    
    [JsonPropertyName("customerDetails")]
    public DhlShipmentCustomerDetails? CustomerDetails { get; set; }
    
    [JsonPropertyName("content")]
    public DhlContent? Content { get; set; }
    
    [JsonPropertyName("shipmentNotification")]
    public List<DhlNotification>? ShipmentNotification { get; set; }
    
    [JsonPropertyName("customerReferences")]
    public List<DhlReference>? CustomerReferences { get; set; }
    
    [JsonPropertyName("getRateEstimates")]
    public bool GetRateEstimates { get; set; }
    
    [JsonPropertyName("getTransliteratedResponse")]
    public bool GetTransliteratedResponse { get; set; }
    
    [JsonPropertyName("outputImageProperties")]
    public DhlOutputImageProperties? OutputImageProperties { get; set; }
}

// Response models
internal class DhlPrice
{
    [JsonPropertyName("price")]
    public decimal Price { get; set; }
    
    [JsonPropertyName("currencyType")]
    public string? CurrencyType { get; set; }
}

internal class DhlDeliveryCapabilities
{
    [JsonPropertyName("estimatedDeliveryDateAndTime")]
    public DateTime? EstimatedDeliveryDateAndTime { get; set; }
}

internal class DhlProduct
{
    [JsonPropertyName("productCode")]
    public string? ProductCode { get; set; }
    
    [JsonPropertyName("productName")]
    public string? ProductName { get; set; }
    
    [JsonPropertyName("totalPrice")]
    public List<DhlPrice>? TotalPrice { get; set; }
    
    [JsonPropertyName("deliveryCapabilities")]
    public DhlDeliveryCapabilities? DeliveryCapabilities { get; set; }
}

internal class DhlRateResponse
{
    [JsonPropertyName("products")]
    public List<DhlProduct>? Products { get; set; }
}

internal class DhlDocument
{
    [JsonPropertyName("typeCode")]
    public string? TypeCode { get; set; }
    
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal class DhlCreateShipmentResponse
{
    [JsonPropertyName("shipmentTrackingNumber")]
    public string? ShipmentTrackingNumber { get; set; }
    
    [JsonPropertyName("dispatchConfirmationNumber")]
    public string? DispatchConfirmationNumber { get; set; }
    
    [JsonPropertyName("serviceType")]
    public string? ServiceType { get; set; }
    
    [JsonPropertyName("estimatedDeliveryDate")]
    public DateTime? EstimatedDeliveryDate { get; set; }
    
    [JsonPropertyName("cancelPickupUrl")]
    public string? CancelPickupUrl { get; set; }
    
    [JsonPropertyName("documents")]
    public List<DhlDocument>? Documents { get; set; }
}

internal class DhlServiceArea
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class DhlTrackingEvent
{
    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
    
    [JsonPropertyName("statusCode")]
    public string? StatusCode { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("serviceArea")]
    public DhlServiceArea? ServiceArea { get; set; }
}

internal class DhlTrackingStatus
{
    [JsonPropertyName("statusCode")]
    public string? StatusCode { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class DhlShipmentTracking
{
    [JsonPropertyName("shipmentTrackingNumber")]
    public string? ShipmentTrackingNumber { get; set; }
    
    [JsonPropertyName("status")]
    public DhlTrackingStatus? Status { get; set; }
    
    [JsonPropertyName("estimatedDeliveryDate")]
    public DateTime? EstimatedDeliveryDate { get; set; }
    
    [JsonPropertyName("events")]
    public List<DhlTrackingEvent>? Events { get; set; }
}

internal class DhlTrackingResponse
{
    [JsonPropertyName("shipments")]
    public List<DhlShipmentTracking>? Shipments { get; set; }
}

#endregion
