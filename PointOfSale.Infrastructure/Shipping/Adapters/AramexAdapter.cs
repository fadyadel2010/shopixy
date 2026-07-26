using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Helpers;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Aramex shipping carrier adapter.
/// 
/// Aramex is a major MENA region carrier with global reach.
/// API Documentation: https://www.aramex.com/developers
/// 
/// CARRIER-AGNOSTIC ARCHITECTURE:
/// ❌ NO Aramex enums outside this file
/// ❌ NO Aramex field names in core entities
/// ✅ Translates Aramex → Canonical POS models
/// ✅ Follows canonical ShipmentStatus lifecycle
/// ✅ ReadyForPickup used after successful creation (NOT Pending)
/// 
/// STATUS MAPPING (MANDATORY):
/// Aramex Status       → POS ShipmentStatus
/// ------------------------------------------
/// SH005/Created       → ReadyForPickup
/// SH006/PickedUp      → PickedUp
/// SH007/InTransit     → InTransit
/// SH014/OutForDel     → OutForDelivery
/// SH003/Delivered     → Delivered
/// SH004/Cancelled     → Cancelled
/// SH066/Returned      → Returned
/// Failed/Exception    → Failed
/// </summary>
public class AramexAdapter : IShippingCarrierAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AramexAdapter> _logger;
    
    private const string ProductionUrl = "https://ws.aramex.net/ShippingAPI.V2";
    private const string SandboxUrl = "https://ws.sbx.aramex.net/ShippingAPI.V2";
    
    public string CarrierCode => "aramex";
    public string DisplayName => "Aramex";
    
    public AramexAdapter(
        IHttpClientFactory httpClientFactory,
        ILogger<AramexAdapter> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Aramex");
        _logger = logger;
    }
    
    public async Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            // Build Aramex rate calculation request
            var aramexRequest = new AramexRateCalculatorRequest
            {
                ClientInfo = BuildClientInfo(credentials),
                OriginAddress = new AramexAddress
                {
                    City = request.OriginCity,
                    CountryCode = request.OriginCountry,
                    PostCode = request.OriginZipCode
                },
                DestinationAddress = new AramexAddress
                {
                    City = request.DestinationCity,
                    CountryCode = request.DestinationCountry,
                    PostCode = request.DestinationZipCode
                },
                ShipmentDetails = new AramexShipmentDetails
                {
                    ActualWeight = new AramexWeight { Value = request.WeightKg, Unit = "KG" },
                    NumberOfPieces = request.PackageCount,
                    PaymentType = request.IsCashOnDelivery ? "C" : "P",
                    ProductGroup = ResolveProductGroup(request.OriginCountry, request.DestinationCountry),
                    ProductType = MapServiceLevel(
                        request.ServiceLevel,
                        request.OriginCountry,
                        request.DestinationCountry)
                }
            };
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/RateCalculator/Service_1_0.svc/json/CalculateRate");
            httpRequest.Content = JsonContent.Create(aramexRequest, options: GetJsonOptions());
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Aramex rate calculation failed: {StatusCode} - {Body}", 
                    response.StatusCode, responseBody);
                    
                return new ShippingQuoteResult
                {
                    Success = false,
                    ErrorCode = "ARAMEX_RATE_ERROR",
                    ErrorMessage = TryParseError(responseBody) ?? $"HTTP {(int)response.StatusCode}"
                };
            }
            
            var rateResponse = JsonSerializer.Deserialize<AramexRateResponse>(responseBody, GetJsonOptions());
            
            if (rateResponse?.HasErrors == true)
            {
                return new ShippingQuoteResult
                {
                    Success = false,
                    ErrorCode = "ARAMEX_RATE_ERROR",
                    ErrorMessage = rateResponse.Notifications?.FirstOrDefault()?.Message ?? "Rate calculation failed"
                };
            }
            
            var quotes = new List<ShippingQuote>();
            
            var rateCurrency = rateResponse?.TotalAmount?.CurrencyCode?.Trim().ToUpperInvariant();
            if (rateResponse?.TotalAmount == null ||
                rateResponse.TotalAmount.Value < 0 ||
                !Iso4217CurrencyProvider.IsValidCurrency(rateCurrency ?? string.Empty))
            {
                return new ShippingQuoteResult
                {
                    Success = false,
                    ErrorCode = "ARAMEX_RATE_RESPONSE_INVALID",
                    ErrorMessage = "Aramex did not return an admissible rate amount and currency."
                };
            }

            quotes.Add(new ShippingQuote
            {
                CarrierCode = CarrierCode,
                ServiceLevel = request.ServiceLevel ?? "standard",
                ServiceName = GetServiceName(request.ServiceLevel),
                Cost = rateResponse.TotalAmount.Value,
                Currency = rateCurrency!,
                EstimatedDeliveryDaysMin = GetEstimatedDays(request.ServiceLevel).Min,
                EstimatedDeliveryDaysMax = GetEstimatedDays(request.ServiceLevel).Max,
                SupportsCod = true
            });
            
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
            _logger.LogError(ex, "Aramex rate calculation HTTP error");
            return new ShippingQuoteResult
            {
                Success = false,
                ErrorCode = "ARAMEX_RATE_TRANSPORT_ERROR",
                ErrorMessage = "Aramex rate calculation could not be completed."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Aramex rate calculation unexpected error");
            return new ShippingQuoteResult
            {
                Success = false,
                ErrorCode = "ARAMEX_RATE_RESPONSE_INVALID",
                ErrorMessage = "Aramex rate calculation returned an invalid response."
            };
        }
    }
    
    public async Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateShipmentRequest(request, credentials);
        if (!validation.IsValid)
        {
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "ARAMEX_REQUEST_CONTRACT_INVALID",
                ErrorMessage = validation.Error
            };
        }

        var originCountry = validation.OriginCountry!;
        var recipientCountry = validation.RecipientCountry!;
        var currency = validation.Currency!;
        var productGroup = ResolveProductGroup(originCountry, recipientCountry);
        var productType = MapServiceLevel(request.ServiceLevel, originCountry, recipientCountry);
        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            // Build Aramex shipment creation request
            var aramexRequest = new AramexCreateShipmentRequest
            {
                ClientInfo = BuildClientInfo(credentials),
                LabelInfo = new AramexLabelInfo
                {
                    ReportID = 9201,
                    ReportType = "URL"
                },
                Shipments = new List<AramexShipment>
                {
                    new AramexShipment
                    {
                        Reference1 = request.OrderNumber,
                        Reference2 = request.InternalShipmentId,
                        Shipper = new AramexParty
                        {
                            Reference1 = credentials.AccountId,
                            AccountNumber = credentials.AccountId ?? "",
                            PartyAddress = new AramexAddress
                            {
                                Line1 = request.OriginAddress,
                                City = request.OriginCity,
                                StateOrProvinceCode = NullIfWhiteSpace(request.OriginStateOrProvinceCode),
                                CountryCode = originCountry,
                                PostCode = request.OriginZipCode
                            },
                            Contact = new AramexContact
                            {
                                PersonName = request.OriginName,
                                CompanyName = request.OriginName,
                                PhoneNumber1 = request.OriginPhone,
                                CellPhone = request.OriginPhone,
                                EmailAddress = credentials.ApiKey
                            }
                        },
                        Consignee = new AramexParty
                        {
                            PartyAddress = new AramexAddress
                            {
                                Line1 = request.RecipientAddress,
                                Line2 = request.RecipientAddressLine2,
                                City = request.RecipientCity,
                                StateOrProvinceCode = NullIfWhiteSpace(request.RecipientStateOrProvinceCode),
                                CountryCode = recipientCountry,
                                PostCode = request.RecipientZipCode
                            },
                            Contact = new AramexContact
                            {
                                PersonName = request.RecipientName,
                                CompanyName = request.RecipientName,
                                PhoneNumber1 = request.RecipientPhone,
                                CellPhone = request.RecipientPhone,
                                EmailAddress = request.RecipientEmail
                            }
                        },
                        ShippingDateTime = FormatWcfJsonDate(DateTimeOffset.UtcNow),
                        Comments = NullIfWhiteSpace(request.Notes),
                        ShipmentDetails = new AramexShipmentDetails
                        {
                            ActualWeight = new AramexWeight { Value = request.WeightKg, Unit = "KG" },
                            Dimensions = request.LengthCm.HasValue
                                ? new AramexDimensions
                                {
                                    Length = request.LengthCm.Value,
                                    Width = request.WidthCm!.Value,
                                    Height = request.HeightCm!.Value,
                                    Unit = "CM"
                                }
                                : null,
                            NumberOfPieces = request.PackageCount,
                            ProductType = productType,
                            ProductGroup = productGroup,
                            PaymentType = "P",
                            DescriptionOfGoods = request.PackageDescription!.Trim(),
                            GoodsOriginCountry = originCountry,
                            CustomsValueAmount = request.DeclaredValue.HasValue
                                ? new AramexMoney { Value = request.DeclaredValue.Value, CurrencyCode = currency }
                                : null,
                            InsuranceAmount = request.InsuranceAmount.HasValue
                                ? new AramexMoney { Value = request.InsuranceAmount.Value, CurrencyCode = currency }
                                : null,
                            CashOnDeliveryAmount = request.IsCashOnDelivery 
                                ? new AramexMoney { Value = request.CodAmount!.Value, CurrencyCode = currency }
                                : null,
                            Services = request.IsCashOnDelivery ? "CODS" : null
                        }
                    }
                }
            };
            
            _logger.LogInformation(
                "Creating Aramex shipment for order {OrderNumber}",
                request.OrderNumber);
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Shipping/Service_1_0.svc/json/CreateShipments");
            httpRequest.Content = JsonContent.Create(aramexRequest, options: GetJsonOptions());
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Aramex API error: {StatusCode} - {Body}",
                    response.StatusCode, responseBody);
                    
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "ARAMEX_API_ERROR",
                    ErrorMessage = TryParseError(responseBody) ?? $"HTTP {(int)response.StatusCode}",
                    RawResponse = responseBody
                };
            }
            
            var aramexResponse = JsonSerializer.Deserialize<AramexCreateShipmentResponse>(responseBody, GetJsonOptions());
            
            if (aramexResponse?.HasErrors == true ||
                aramexResponse?.Shipments == null ||
                aramexResponse.Shipments.Count != 1)
            {
                var errorMessage = aramexResponse?.Notifications?.FirstOrDefault()?.Message ?? "Shipment creation failed";
                
                _logger.LogError("Aramex shipment creation failed: {Error}", errorMessage);
                
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "ARAMEX_CREATE_ERROR",
                    ErrorMessage = errorMessage,
                    RawResponse = responseBody
                };
            }
            
            var shipment = aramexResponse.Shipments.First();
            if ((!string.IsNullOrWhiteSpace(shipment.Reference1) &&
                 !string.Equals(shipment.Reference1.Trim(), request.OrderNumber.Trim(), StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(shipment.Reference2) &&
                 !string.Equals(shipment.Reference2.Trim(), request.InternalShipmentId.Trim(), StringComparison.Ordinal)))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "ARAMEX_RESPONSE_IDENTITY_MISMATCH",
                    ErrorMessage = "Aramex returned a processed shipment for a different order or shipment reference.",
                    RawResponse = responseBody
                };
            }

            var trackingNumber = (shipment.ID ?? "").Trim();
            var labelUrl = shipment.ShipmentLabel?.LabelURL?.Trim();
            var labelData = shipment.ShipmentLabel?.LabelFileContents?.Trim();
            
            // Build tracking URL
            var trackingUrl = $"https://www.aramex.com/track/shipments?ShipmentNumber={trackingNumber}";
            
            // Determine ExternalReferenceId and AwbNumber: prefer ForeignHAWB, fallback to trackingNumber only if non-empty
            string? externalReferenceId = null;
            if (!string.IsNullOrWhiteSpace(shipment.ForeignHAWB))
            {
                externalReferenceId = shipment.ForeignHAWB.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(trackingNumber))
            {
                externalReferenceId = trackingNumber;
            }
            
            // Validate that we have at least one identifier before returning success
            if (string.IsNullOrWhiteSpace(externalReferenceId))
            {
                _logger.LogError(
                    "Aramex shipment creation failed: No valid identifier (ForeignHAWB or TrackingNumber) returned. OrderNumber: {OrderNumber}",
                    request.OrderNumber);
                
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "ARAMEX_MISSING_IDENTIFIER",
                    ErrorMessage = "Aramex did not return a valid shipment identifier (ForeignHAWB or ID). Cannot persist shipment without ExternalReferenceId.",
                    RawResponse = responseBody
                };
            }
            
            // Store Aramex-specific metadata
            var metadata = JsonSerializer.Serialize(new
            {
                aramexId = shipment.ID,
                foreignHawb = shipment.ForeignHAWB,
                reference = shipment.Reference1,
                originEntity = shipment.ShipmentDetails?.OriginEntity,
                productGroup = shipment.ShipmentDetails?.ProductGroup
            });
            
            _logger.LogInformation(
                "Created Aramex shipment. TrackingNumber: {TrackingNumber}, OrderNumber: {OrderNumber}",
                trackingNumber, request.OrderNumber);
            
            return new CreateShipmentResult
            {
                Success = true,
                ExternalReferenceId = externalReferenceId,
                TrackingNumber = trackingNumber,
                TrackingUrl = trackingUrl,
                AwbNumber = externalReferenceId,
                // CANONICAL RULE: Successful carrier creation → ReadyForPickup (NEVER Pending)
                InitialStatus = ShipmentStatus.ReadyForPickup,
                LabelUrl = labelUrl,
                LabelData = labelData,
                LabelFormat = "PDF",
                CarrierMetadata = metadata,
                RawResponse = responseBody
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Aramex HTTP request failed");
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "HTTP_ERROR",
                ErrorMessage = $"Connection error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating Aramex shipment");
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "UNEXPECTED_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }
    
    public Task<CancelShipmentResult> CancelShipmentAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Aramex shipment cancellation is unsupported by the public API contract. TrackingNumber={TrackingNumber}",
            trackingNumber);

        return Task.FromResult(new CancelShipmentResult
        {
            Success = false,
            ErrorCode = "ARAMEX_SHIPMENT_CANCELLATION_UNSUPPORTED",
            ErrorMessage = "The public Aramex Shipping Services API supports pickup cancellation, not shipment cancellation."
        });
    }
    
    public async Task<ShipmentTrackingResult> GetTrackingAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            var trackRequest = new AramexTrackRequest
            {
                ClientInfo = BuildClientInfo(credentials),
                Shipments = new List<string> { trackingNumber },
                GetLastTrackingUpdateOnly = false
            };
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Tracking/Service_1_0.svc/json/TrackShipments");
            httpRequest.Content = JsonContent.Create(trackRequest, options: GetJsonOptions());
            
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
            
            var trackResponse = JsonSerializer.Deserialize<AramexTrackResponse>(responseBody, GetJsonOptions());
            
            if (trackResponse?.HasErrors == true)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_ERROR",
                    ErrorMessage = trackResponse.Notifications?.FirstOrDefault()?.Message ?? "Tracking failed",
                    RawResponse = responseBody
                };
            }
            
            var matchingResults = trackResponse?.TrackingResults?
                .Where(result => string.Equals(result.Key?.Trim(), trackingNumber.Trim(), StringComparison.Ordinal))
                .ToList();
            if (matchingResults == null || matchingResults.Count != 1)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_IDENTITY_MISMATCH",
                    ErrorMessage = "Aramex tracking response did not identify exactly the requested shipment.",
                    RawResponse = responseBody
                };
            }

            var shipmentResults = matchingResults[0];
            if (shipmentResults?.Value == null || !shipmentResults.Value.Any())
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "NO_TRACKING_DATA",
                    ErrorMessage = "No tracking information available",
                    RawResponse = responseBody
                };
            }
            
            var trackingEvents = shipmentResults.Value
                .OrderByDescending(t => t.UpdateDateTime)
                .ToList();

            if (trackingEvents.Any(trackingEvent =>
                    !trackingEvent.UpdateDateTime.HasValue ||
                    !TryMapAramexStatus(trackingEvent.UpdateCode, out _)))
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_STATUS_UNADMITTED",
                    ErrorMessage = "Aramex tracking response contained an unknown status or missing event timestamp.",
                    RawResponse = responseBody
                };
            }
            
            var latestEvent = trackingEvents.First();
            _ = TryMapAramexStatus(latestEvent.UpdateCode, out var currentStatus);
            
            var result = new ShipmentTrackingResult
            {
                Success = true,
                CurrentStatus = currentStatus,
                CurrentStatusDescription = latestEvent.UpdateDescription,
                CurrentLocation = latestEvent.UpdateLocation,
                RawResponse = responseBody,
                Events = trackingEvents.Select(t => new TrackingEvent
                {
                    Timestamp = t.UpdateDateTime!.Value,
                    Status = MapAdmittedAramexStatus(t.UpdateCode!),
                    Description = t.UpdateDescription ?? "",
                    Location = t.UpdateLocation,
                    CarrierStatusCode = t.UpdateCode
                }).ToList()
            };
            
            // Check for COD collection
            if (currentStatus == ShipmentStatus.Delivered)
            {
                // Aramex sends COD info in the delivery confirmation
                var codEvent = trackingEvents.FirstOrDefault(t => 
                    t.UpdateCode?.Contains("COD", StringComparison.OrdinalIgnoreCase) == true);
                    
                if (codEvent != null)
                {
                    result.CodCollected = true;
                    result.CodCollectedAt = codEvent.UpdateDateTime;
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking Aramex shipment {TrackingNumber}", trackingNumber);
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
        var credentialError = ValidateClientInfo(credentials);
        if (credentialError != null)
        {
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "INVALID_CREDENTIAL_CONFIGURATION",
                ErrorMessage = credentialError
            };
        }

        var baseUrl = GetBaseUrl(credentials);
        
        try
        {
            // Test credentials by making a simple API call
            var testRequest = new
            {
                ClientInfo = BuildClientInfo(credentials)
            };
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Location/Service_1_0.svc/json/FetchCountries");
            httpRequest.Content = JsonContent.Create(testRequest, options: GetJsonOptions());
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var providerHasErrors = false;
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                using var document = JsonDocument.Parse(responseBody);
                providerHasErrors =
                    document.RootElement.TryGetProperty("HasErrors", out var hasErrors) &&
                    hasErrors.ValueKind == JsonValueKind.True;
            }

            if (response.IsSuccessStatusCode && !providerHasErrors)
            {
                return new CarrierValidationResult
                {
                    Success = true,
                    AccountName = $"Aramex Account ({credentials.AccountId})"
                };
            }
            
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "INVALID_CREDENTIALS",
                ErrorMessage = "Aramex API credentials validation failed"
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
                Name = "Domestic Express",
                Description = "Standard domestic delivery (2-5 business days)",
                TypicalDaysMin = 2,
                TypicalDaysMax = 5
            },
            new ServiceLevelInfo
            {
                Code = "express",
                Name = "Priority Express",
                Description = "Priority domestic delivery (1-2 business days)",
                TypicalDaysMin = 1,
                TypicalDaysMax = 2
            },
            new ServiceLevelInfo
            {
                Code = "international",
                Name = "International Express",
                Description = "International delivery (5-10 business days)",
                TypicalDaysMin = 5,
                TypicalDaysMax = 10
            },
            new ServiceLevelInfo
            {
                Code = "economy",
                Name = "Economy Express",
                Description = "Economy international delivery (7-14 business days)",
                TypicalDaysMin = 7,
                TypicalDaysMax = 14
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
            // Aramex webhook payload structure
            var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            
            // Extract tracking number
            if (!root.TryGetProperty("WaybillNumber", out var waybillProp) &&
                !root.TryGetProperty("ShipmentNumber", out waybillProp))
            {
                _logger.LogWarning("Aramex webhook missing tracking number");
                return null;
            }
            
            var trackingNumber = waybillProp.GetString();
            if (string.IsNullOrEmpty(trackingNumber))
                return null;
            
            // Extract status
            string? statusCode = null;
            string? statusDescription = null;
            
            if (root.TryGetProperty("UpdateCode", out var codeProp))
                statusCode = codeProp.GetString();
            if (root.TryGetProperty("UpdateDescription", out var descProp))
                statusDescription = descProp.GetString();
            
            if (string.IsNullOrEmpty(statusCode))
            {
                _logger.LogWarning("Aramex webhook missing status code for {TrackingNumber}", trackingNumber);
                return null;
            }
            
            if (!TryMapAramexStatus(statusCode, out var status))
            {
                _logger.LogWarning(
                    "Rejected Aramex webhook with unknown status code {Code} for {TrackingNumber}",
                    statusCode,
                    trackingNumber);
                return null;
            }
            
            // Extract timestamp
            if (!root.TryGetProperty("UpdateDateTime", out var timeProp) ||
                !DateTime.TryParse(timeProp.GetString(), out var eventTime))
            {
                _logger.LogWarning(
                    "Rejected Aramex webhook with missing or malformed timestamp for {TrackingNumber}",
                    trackingNumber);
                return null;
            }
            
            // Check for COD collection
            var codCollected = statusCode.Contains("COD", StringComparison.OrdinalIgnoreCase) &&
                               statusDescription?.Contains("collected", StringComparison.OrdinalIgnoreCase) == true;
            
            _logger.LogInformation(
                "Parsed Aramex webhook: TrackingNumber={TrackingNumber}, Status={Status}, Code={Code}",
                trackingNumber, status, statusCode);
            
            return new WebhookParseResult
            {
                TrackingNumber = trackingNumber,
                Status = status,
                StatusDescription = statusDescription,
                EventTimestamp = eventTime,
                CodCollected = codCollected,
                CodCollectedAt = codCollected ? eventTime : null,
                RawPayload = payload
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Aramex webhook payload");
            return null;
        }
    }
    
    #region Helper Methods
    
    /// <summary>
    /// Maps Aramex status codes to canonical POS ShipmentStatus.
    /// 
    /// CANONICAL RULES (ENFORCED):
    /// - Carrier "created" → ReadyForPickup (NEVER Pending)
    /// - Unknown status → fail closed; no canonical status is invented
    /// </summary>
    private static bool TryMapAramexStatus(string? aramexCode, out ShipmentStatus status)
    {
        status = (aramexCode ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            // Shipment created/accepted - CANONICAL RULE: Use ReadyForPickup, NOT Pending
            "SH005" or "SH001" or "CREATED" or "SHIPMENT_CREATED" or "PICKUP_SCHEDULED" 
                => ShipmentStatus.ReadyForPickup,
            
            // Picked up by carrier
            "SH006" or "SH010" or "PICKED_UP" or "PICKEDUP" or "COLLECTED" 
                => ShipmentStatus.PickedUp,
            
            // In transit through network
            "SH007" or "SH008" or "SH009" or "IN_TRANSIT" or "INTRANSIT" or "DEPARTED" or "ARRIVED" or "IN_HUB"
                => ShipmentStatus.InTransit,
            
            // Out for final delivery
            "SH014" or "OUT_FOR_DELIVERY" or "OUTFORDELIVERY" or "WITH_COURIER" 
                => ShipmentStatus.OutForDelivery,
            
            // Successfully delivered
            "SH003" or "DELIVERED" or "DELIVERY_SUCCESS" 
                => ShipmentStatus.Delivered,
            
            // Cancelled
            "SH004" or "CANCELLED" or "CANCELED" or "VOIDED" 
                => ShipmentStatus.Cancelled,
            
            // Returned to sender
            "SH066" or "SH067" or "RETURNED" or "RETURN_TO_SENDER" or "RTS" 
                => ShipmentStatus.Returned,
            
            // Failed delivery
            "SH012" or "SH013" or "FAILED" or "DELIVERY_FAILED" or "EXCEPTION" or "UNDELIVERABLE" 
                => ShipmentStatus.Failed,
            
            _ => default
        };

        return !string.IsNullOrWhiteSpace(aramexCode) &&
               status != default;
    }

    private static ShipmentStatus MapAdmittedAramexStatus(string aramexCode)
    {
        if (TryMapAramexStatus(aramexCode, out var status))
        {
            return status;
        }

        throw new InvalidOperationException("Aramex status must be admitted before canonical mapping.");
    }
    
    private static string GetBaseUrl(CarrierCredentials credentials)
    {
        return credentials.UseSandbox ? SandboxUrl : ProductionUrl;
    }
    
    private static AramexClientInfo BuildClientInfo(CarrierCredentials credentials)
    {
        CountryCodeHelper.TryNormalizeCountryCode(
            credentials.ExtraConfig.GetValueOrDefault("AccountCountryCode"),
            out var accountCountryCode);

        return new AramexClientInfo
        {
            UserName = credentials.ApiKey.Trim(),
            Password = credentials.ApiSecret!.Trim(),
            AccountNumber = credentials.AccountId!.Trim(),
            AccountPin = credentials.ExtraConfig["AccountPin"].Trim(),
            AccountEntity = credentials.ExtraConfig["AccountEntity"].Trim().ToUpperInvariant(),
            AccountCountryCode = accountCountryCode,
            Version = "v1.0",
            Source = 24
        };
    }
    
    private static string ResolveProductGroup(string originCountry, string destinationCountry)
    {
        return string.Equals(originCountry, destinationCountry, StringComparison.OrdinalIgnoreCase)
            ? "DOM"
            : "EXP";
    }

    private static string? ValidateClientInfo(CarrierCredentials? credentials)
    {
        if (credentials == null)
        {
            return "Aramex credentials are required.";
        }

        foreach (var field in new[]
                 {
                     ("ClientInfo.UserName", credentials.ApiKey, 50, 1),
                     ("ClientInfo.Password", credentials.ApiSecret, 50, 1),
                     ("ClientInfo.AccountNumber", credentials.AccountId, 50, 1),
                     ("ClientInfo.AccountPin", credentials.ExtraConfig.GetValueOrDefault("AccountPin"), 50, 1),
                     ("ClientInfo.AccountEntity", credentials.ExtraConfig.GetValueOrDefault("AccountEntity"), 3, 3)
                 })
        {
            var error = ValidateRequiredText(field.Item1, field.Item2, field.Item3, field.Item4);
            if (error != null)
            {
                return error;
            }
        }

        return CountryCodeHelper.TryNormalizeCountryCode(
            credentials.ExtraConfig.GetValueOrDefault("AccountCountryCode"),
            out _)
            ? null
            : "ClientInfo.AccountCountryCode must be a valid ISO 3166-1 alpha-2 country code.";
    }

    private static string MapServiceLevel(
        string? serviceLevel,
        string originCountry,
        string destinationCountry)
    {
        if (ResolveProductGroup(originCountry, destinationCountry) == "DOM")
        {
            return "OND";
        }

        return (serviceLevel?.ToLowerInvariant()) switch
        {
            "international" => "PPX", // Priority Parcel Express
            "economy" => "EPX", // Economy Parcel Express
            _ => "PPX"
        };
    }

    private static AramexCreateValidation ValidateCreateShipmentRequest(
        CreateShipmentRequest request,
        CarrierCredentials credentials)
    {
        if (request == null)
        {
            return Invalid("Shipment request is required.");
        }

        if (credentials == null)
        {
            return Invalid("Aramex credentials are required.");
        }

        foreach (var field in new[]
                 {
                     ("ClientInfo.UserName", credentials.ApiKey, 50, 1),
                     ("ClientInfo.Password", credentials.ApiSecret, 50, 1),
                     ("ClientInfo.AccountNumber", credentials.AccountId, 50, 1),
                     ("ClientInfo.AccountPin", credentials.ExtraConfig.GetValueOrDefault("AccountPin"), 50, 1),
                     ("ClientInfo.AccountEntity", credentials.ExtraConfig.GetValueOrDefault("AccountEntity"), 3, 3),
                     ("Shipment.Reference1", request.OrderNumber, 50, 1),
                     ("Shipment.Reference2", request.InternalShipmentId, 50, 1),
                     ("Shipper.Address.Line1", request.OriginAddress, 50, 4),
                     ("Shipper.Address.City", request.OriginCity, 50, 1),
                     ("Shipper.Contact.PersonName", request.OriginName, 50, 1),
                     ("Consignee.Address.Line1", request.RecipientAddress, 50, 4),
                     ("Consignee.Address.City", request.RecipientCity, 50, 1),
                     ("Consignee.Contact.PersonName", request.RecipientName, 50, 1),
                     ("Shipment.Details.DescriptionOfGoods", request.PackageDescription, 100, 1)
                 })
        {
            var error = ValidateRequiredText(field.Item1, field.Item2, field.Item3, field.Item4);
            if (error != null)
            {
                return Invalid(error);
            }
        }

        if (!CountryCodeHelper.TryNormalizeCountryCode(request.OriginCountry, out var originCountry))
        {
            return Invalid("Shipper.Address.CountryCode must be a valid ISO 3166-1 alpha-2 country code.");
        }

        if (!CountryCodeHelper.TryNormalizeCountryCode(request.RecipientCountry, out var recipientCountry))
        {
            return Invalid("Consignee.Address.CountryCode must be a valid ISO 3166-1 alpha-2 country code.");
        }

        if (!CountryCodeHelper.TryNormalizeCountryCode(
                credentials.ExtraConfig.GetValueOrDefault("AccountCountryCode"),
                out _))
        {
            return Invalid("ClientInfo.AccountCountryCode must be a valid ISO 3166-1 alpha-2 country code.");
        }

        var optionalError =
            ValidateOptionalText("Consignee.Address.Line2", request.RecipientAddressLine2, 50) ??
            ValidateOptionalText("Shipper.Address.StateOrProvinceCode", request.OriginStateOrProvinceCode, 100) ??
            ValidateOptionalText("Consignee.Address.StateOrProvinceCode", request.RecipientStateOrProvinceCode, 100) ??
            ValidateOptionalText("Shipper.Address.PostCode", request.OriginZipCode, 30) ??
            ValidateOptionalText("Consignee.Address.PostCode", request.RecipientZipCode, 30);
        if (optionalError != null)
        {
            return Invalid(optionalError);
        }

        var phoneError =
            ValidatePhone("Shipper.Contact.PhoneNumber1", request.OriginPhone) ??
            ValidatePhone("Consignee.Contact.PhoneNumber1", request.RecipientPhone);
        if (phoneError != null)
        {
            return Invalid(phoneError);
        }

        var emailError =
            ValidateEmail("Shipper.Contact.EmailAddress", credentials.ApiKey) ??
            ValidateEmail("Consignee.Contact.EmailAddress", request.RecipientEmail);
        if (emailError != null)
        {
            return Invalid(emailError);
        }

        if (request.WeightKg <= 0m || request.WeightKg > 100m || DecimalPlaces(request.WeightKg) > 3)
        {
            return Invalid("Shipment.Details.ActualWeight.Value must be greater than 0, no more than 100, and have at most three decimal places.");
        }

        if (request.PackageCount is < 1 or > 100)
        {
            return Invalid("Shipment.Details.NumberOfPieces must be between 1 and 100.");
        }

        var dimensions = new[] { request.LengthCm, request.WidthCm, request.HeightCm };
        if (dimensions.Any(value => value.HasValue) && dimensions.Any(value => !value.HasValue))
        {
            return Invalid("Shipment.Details.Dimensions requires Length, Width, Height, and Unit together.");
        }

        if (dimensions.All(value => value.HasValue) &&
            dimensions.Any(value => value <= 0m || value > 100m || DecimalPlaces(value!.Value) > 3))
        {
            return Invalid("Shipment.Details.Dimensions values must be greater than 0, no more than 100, and have at most three decimal places.");
        }

        var serviceLevel = request.ServiceLevel?.Trim().ToLowerInvariant();
        if (serviceLevel is not ("standard" or "express" or "international" or "economy"))
        {
            return Invalid("Shipment.Details.ProductType cannot be resolved from the supplied service level.");
        }

        var currency = request.Currency?.Trim().ToUpperInvariant() ?? "";
        if (!Iso4217CurrencyProvider.IsValidCurrency(currency))
        {
            return Invalid("Shipment money currency must be a valid ISO 4217 alpha-3 code.");
        }

        var international = !string.Equals(originCountry, recipientCountry, StringComparison.Ordinal);
        if (international &&
            (string.IsNullOrWhiteSpace(request.OriginStateOrProvinceCode) ||
             string.IsNullOrWhiteSpace(request.RecipientStateOrProvinceCode) ||
             string.IsNullOrWhiteSpace(request.OriginZipCode) ||
             string.IsNullOrWhiteSpace(request.RecipientZipCode)))
        {
            return Invalid("International shipment addresses require StateOrProvinceCode and PostCode so country-specific conditional address rules cannot be bypassed.");
        }

        if (international && (!request.DeclaredValue.HasValue || request.DeclaredValue <= 0m))
        {
            return Invalid("Shipment.Details.CustomsValueAmount is required for dutiable international parcel products.");
        }

        if (request.DeclaredValue.HasValue &&
            (request.DeclaredValue <= 0m || DecimalPlaces(request.DeclaredValue.Value) > 3))
        {
            return Invalid("Shipment.Details.CustomsValueAmount.Value must be greater than 0 with at most three decimal places.");
        }

        if (request.InsuranceAmount.HasValue &&
            (request.InsuranceAmount <= 0m || DecimalPlaces(request.InsuranceAmount.Value) > 3))
        {
            return Invalid("Shipment.Details.InsuranceAmount.Value must be greater than 0 with at most three decimal places.");
        }

        if (request.IsCashOnDelivery)
        {
            if (!request.CodAmount.HasValue || request.CodAmount <= 0m)
            {
                return Invalid("Shipment.Details.CashOnDeliveryAmount is required and must be greater than 0 when CODS is requested.");
            }

            if (!string.Equals(currency, "USD", StringComparison.Ordinal))
            {
                return Invalid("The official Shipping Services contract requires USD for CashOnDeliveryAmount.");
            }
        }
        else if (request.CodAmount.HasValue)
        {
            return Invalid("CashOnDeliveryAmount cannot be supplied unless the CODS service is requested.");
        }

        return new AramexCreateValidation(true, null, originCountry, recipientCountry, currency);
    }

    private static string? ValidateRequiredText(string field, string? value, int maximum, int minimum)
    {
        var length = value?.Trim().Length ?? 0;
        return length < minimum || length > maximum
            ? $"{field} must contain between {minimum} and {maximum} characters."
            : null;
    }

    private static string? ValidateOptionalText(string field, string? value, int maximum) =>
        value?.Trim().Length > maximum ? $"{field} cannot exceed {maximum} characters." : null;

    private static string? ValidatePhone(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 30)
        {
            return $"{field} is required and cannot exceed 30 characters.";
        }

        var trimmed = value.Trim();
        if (trimmed.Count(char.IsDigit) < 6 ||
            trimmed.Any(character => !char.IsDigit(character) && character is not ('+' or '-' or ' ' or '(' or ')')))
        {
            return $"{field} must contain a valid phone number.";
        }

        return null;
    }

    private static string? ValidateEmail(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 50)
        {
            return $"{field} is required and cannot exceed 50 characters.";
        }

        try
        {
            var address = new MailAddress(value.Trim());
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
                ? null
                : $"{field} must contain a valid email address.";
        }
        catch (FormatException)
        {
            return $"{field} must contain a valid email address.";
        }
    }

    private static int DecimalPlaces(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static string FormatWcfJsonDate(DateTimeOffset value) =>
        $"/Date({value.ToUnixTimeMilliseconds()})/";

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AramexCreateValidation Invalid(string error) =>
        new(false, error, null, null, null);
    
    private static string GetServiceName(string? serviceLevel)
    {
        return (serviceLevel?.ToLowerInvariant()) switch
        {
            "express" => "Aramex Priority Express",
            "international" => "Aramex International Express",
            "economy" => "Aramex Economy Express",
            _ => "Aramex Domestic Express"
        };
    }
    
    private static (int Min, int Max) GetEstimatedDays(string? serviceLevel)
    {
        return (serviceLevel?.ToLowerInvariant()) switch
        {
            "express" => (1, 2),
            "international" => (5, 10),
            "economy" => (7, 14),
            _ => (2, 5)
        };
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
            if (doc.RootElement.TryGetProperty("Notifications", out var notifications) &&
                notifications.ValueKind == JsonValueKind.Array)
            {
                foreach (var notification in notifications.EnumerateArray())
                {
                    if (notification.TryGetProperty("Message", out var msg))
                        return msg.GetString();
                }
            }
            if (doc.RootElement.TryGetProperty("Message", out var message))
                return message.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }
    
    #endregion
}

#region Aramex API Models

/// <summary>
/// Aramex Client Information (authentication).
/// Internal to adapter only - no leakage to core domain.
/// </summary>
internal class AramexClientInfo
{
    [JsonPropertyName("UserName")]
    public string UserName { get; set; } = "";
    
    [JsonPropertyName("Password")]
    public string Password { get; set; } = "";
    
    [JsonPropertyName("AccountNumber")]
    public string AccountNumber { get; set; } = "";
    
    [JsonPropertyName("AccountPin")]
    public string AccountPin { get; set; } = "";
    
    [JsonPropertyName("AccountEntity")]
    public string AccountEntity { get; set; } = "CAI";
    
    [JsonPropertyName("AccountCountryCode")]
    public string AccountCountryCode { get; set; } = "EG";
    
    [JsonPropertyName("Version")]
    public string Version { get; set; } = "v1.0";
    
    [JsonPropertyName("Source")]
    public int? Source { get; set; }
}

internal class AramexAddress
{
    [JsonPropertyName("Line1")]
    public string? Line1 { get; set; }
    
    [JsonPropertyName("Line2")]
    public string? Line2 { get; set; }
    
    [JsonPropertyName("Line3")]
    public string? Line3 { get; set; }
    
    [JsonPropertyName("City")]
    public string? City { get; set; }
    
    [JsonPropertyName("StateOrProvinceCode")]
    public string? StateOrProvinceCode { get; set; }
    
    [JsonPropertyName("PostCode")]
    public string? PostCode { get; set; }
    
    [JsonPropertyName("CountryCode")]
    public string? CountryCode { get; set; }
}

internal class AramexContact
{
    [JsonPropertyName("PersonName")]
    public string? PersonName { get; set; }
    
    [JsonPropertyName("CompanyName")]
    public string? CompanyName { get; set; }
    
    [JsonPropertyName("PhoneNumber1")]
    public string? PhoneNumber1 { get; set; }
    
    [JsonPropertyName("PhoneNumber2")]
    public string? PhoneNumber2 { get; set; }
    
    [JsonPropertyName("CellPhone")]
    public string? CellPhone { get; set; }
    
    [JsonPropertyName("EmailAddress")]
    public string? EmailAddress { get; set; }
}

internal class AramexParty
{
    [JsonPropertyName("Reference1")]
    public string? Reference1 { get; set; }
    
    [JsonPropertyName("Reference2")]
    public string? Reference2 { get; set; }
    
    [JsonPropertyName("AccountNumber")]
    public string? AccountNumber { get; set; }
    
    [JsonPropertyName("PartyAddress")]
    public AramexAddress? PartyAddress { get; set; }
    
    [JsonPropertyName("Contact")]
    public AramexContact? Contact { get; set; }
}

internal class AramexWeight
{
    [JsonPropertyName("Value")]
    public decimal Value { get; set; }
    
    [JsonPropertyName("Unit")]
    public string Unit { get; set; } = "KG";
}

internal class AramexDimensions
{
    [JsonPropertyName("Length")]
    public decimal Length { get; set; }

    [JsonPropertyName("Width")]
    public decimal Width { get; set; }

    [JsonPropertyName("Height")]
    public decimal Height { get; set; }

    [JsonPropertyName("Unit")]
    public string Unit { get; set; } = "CM";
}

internal class AramexMoney
{
    [JsonPropertyName("Value")]
    public decimal Value { get; set; }
    
    [JsonPropertyName("CurrencyCode")]
    public string? CurrencyCode { get; set; }
}

internal class AramexShipmentDetails
{
    [JsonPropertyName("Dimensions")]
    public AramexDimensions? Dimensions { get; set; }

    [JsonPropertyName("ActualWeight")]
    public AramexWeight? ActualWeight { get; set; }
    
    [JsonPropertyName("NumberOfPieces")]
    public int NumberOfPieces { get; set; }
    
    [JsonPropertyName("ProductGroup")]
    public string? ProductGroup { get; set; }
    
    [JsonPropertyName("ProductType")]
    public string? ProductType { get; set; }
    
    [JsonPropertyName("PaymentType")]
    public string? PaymentType { get; set; }
    
    [JsonPropertyName("PaymentOptions")]
    public string? PaymentOptions { get; set; }
    
    [JsonPropertyName("Services")]
    public string? Services { get; set; }
    
    [JsonPropertyName("DescriptionOfGoods")]
    public string? DescriptionOfGoods { get; set; }
    
    [JsonPropertyName("GoodsOriginCountry")]
    public string? GoodsOriginCountry { get; set; }
    
    [JsonPropertyName("CashOnDeliveryAmount")]
    public AramexMoney? CashOnDeliveryAmount { get; set; }

    [JsonPropertyName("CustomsValueAmount")]
    public AramexMoney? CustomsValueAmount { get; set; }

    [JsonPropertyName("InsuranceAmount")]
    public AramexMoney? InsuranceAmount { get; set; }
    
    [JsonPropertyName("OriginEntity")]
    public string? OriginEntity { get; set; }
}

internal sealed record AramexCreateValidation(
    bool IsValid,
    string? Error,
    string? OriginCountry,
    string? RecipientCountry,
    string? Currency);

internal class AramexShipment
{
    [JsonPropertyName("ID")]
    public string? ID { get; set; }
    
    [JsonPropertyName("Reference1")]
    public string? Reference1 { get; set; }
    
    [JsonPropertyName("Reference2")]
    public string? Reference2 { get; set; }
    
    [JsonPropertyName("Reference3")]
    public string? Reference3 { get; set; }
    
    [JsonPropertyName("ForeignHAWB")]
    public string? ForeignHAWB { get; set; }
    
    [JsonPropertyName("Shipper")]
    public AramexParty? Shipper { get; set; }
    
    [JsonPropertyName("Consignee")]
    public AramexParty? Consignee { get; set; }
    
    [JsonPropertyName("ShippingDateTime")]
    public string? ShippingDateTime { get; set; }
    
    [JsonPropertyName("DueDate")]
    public string? DueDate { get; set; }
    
    [JsonPropertyName("Comments")]
    public string? Comments { get; set; }
    
    [JsonPropertyName("Details")]
    public AramexShipmentDetails? ShipmentDetails { get; set; }

    [JsonPropertyName("ShipmentLabel")]
    public AramexShipmentLabel? ShipmentLabel { get; set; }
}

internal class AramexLabelInfo
{
    [JsonPropertyName("ReportID")]
    public int ReportID { get; set; }
    
    [JsonPropertyName("ReportType")]
    public string? ReportType { get; set; }
}

internal class AramexNotification
{
    [JsonPropertyName("Code")]
    public string? Code { get; set; }
    
    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}

// Request models
internal class AramexRateCalculatorRequest
{
    [JsonPropertyName("ClientInfo")]
    public AramexClientInfo? ClientInfo { get; set; }
    
    [JsonPropertyName("OriginAddress")]
    public AramexAddress? OriginAddress { get; set; }
    
    [JsonPropertyName("DestinationAddress")]
    public AramexAddress? DestinationAddress { get; set; }
    
    [JsonPropertyName("ShipmentDetails")]
    public AramexShipmentDetails? ShipmentDetails { get; set; }
}

internal class AramexCreateShipmentRequest
{
    [JsonPropertyName("ClientInfo")]
    public AramexClientInfo? ClientInfo { get; set; }
    
    [JsonPropertyName("LabelInfo")]
    public AramexLabelInfo? LabelInfo { get; set; }
    
    [JsonPropertyName("Shipments")]
    public List<AramexShipment>? Shipments { get; set; }
}

internal class AramexTrackRequest
{
    [JsonPropertyName("ClientInfo")]
    public AramexClientInfo? ClientInfo { get; set; }
    
    [JsonPropertyName("Shipments")]
    public List<string>? Shipments { get; set; }
    
    [JsonPropertyName("GetLastTrackingUpdateOnly")]
    public bool GetLastTrackingUpdateOnly { get; set; }
}

// Response models
internal class AramexRateResponse
{
    [JsonPropertyName("HasErrors")]
    public bool HasErrors { get; set; }
    
    [JsonPropertyName("Notifications")]
    public List<AramexNotification>? Notifications { get; set; }
    
    [JsonPropertyName("TotalAmount")]
    public AramexMoney? TotalAmount { get; set; }
}

internal class AramexShipmentLabel
{
    [JsonPropertyName("LabelURL")]
    public string? LabelURL { get; set; }
    
    [JsonPropertyName("LabelFileContents")]
    public string? LabelFileContents { get; set; }
}

internal class AramexCreateShipmentResponse
{
    [JsonPropertyName("HasErrors")]
    public bool HasErrors { get; set; }
    
    [JsonPropertyName("Notifications")]
    public List<AramexNotification>? Notifications { get; set; }
    
    [JsonPropertyName("Shipments")]
    public List<AramexShipment>? Shipments { get; set; }
    
}

internal class AramexTrackingUpdate
{
    [JsonPropertyName("UpdateCode")]
    public string? UpdateCode { get; set; }
    
    [JsonPropertyName("UpdateDescription")]
    public string? UpdateDescription { get; set; }
    
    [JsonPropertyName("UpdateDateTime")]
    public DateTime? UpdateDateTime { get; set; }
    
    [JsonPropertyName("UpdateLocation")]
    public string? UpdateLocation { get; set; }
    
    [JsonPropertyName("Comments")]
    public string? Comments { get; set; }
}

internal class AramexTrackingResult
{
    [JsonPropertyName("Key")]
    public string? Key { get; set; }
    
    [JsonPropertyName("Value")]
    public List<AramexTrackingUpdate>? Value { get; set; }
}

internal class AramexTrackResponse
{
    [JsonPropertyName("HasErrors")]
    public bool HasErrors { get; set; }
    
    [JsonPropertyName("Notifications")]
    public List<AramexNotification>? Notifications { get; set; }
    
    [JsonPropertyName("TrackingResults")]
    public List<AramexTrackingResult>? TrackingResults { get; set; }
}

#endregion
