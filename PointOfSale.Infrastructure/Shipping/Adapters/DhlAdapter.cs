using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.Core.DTOs.Customs;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;

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
/// Admitted MyDHL 3.3.4 tracking checkpoints:
/// PU → PickedUp; AF/PL/DF/RR/CR/AR → InTransit; WC → OutForDelivery; OK → Delivered.
/// Other checkpoints require separately verified semantics before admission.
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

        if (request.IsCashOnDelivery || request.CodAmount is > 0 || request.PackageCount != 1 || !ParcelMeasurements.IsMetricWeight(request.WeightKg) ||
            !ParcelMeasurements.IsMetricDimension(request.LengthCm) || !ParcelMeasurements.IsMetricDimension(request.WidthCm) || !ParcelMeasurements.IsMetricDimension(request.HeightCm) ||
            !IsIso2(request.OriginCountry) || !IsIso2(request.DestinationCountry) ||
            !IsRequiredText(request.OriginCity, 45) || !IsRequiredText(request.DestinationCity, 45) ||
            !IsPostalCode(request.OriginZipCode) || !IsPostalCode(request.DestinationZipCode))
            return new ShippingQuoteResult { ErrorCode = "DHL_QUOTE_REQUEST_UNSUPPORTED", ErrorMessage = "Only single-package prepaid DHL quotes with positive actual weight and dimensions are supported." };

        var requestedProduct = string.IsNullOrEmpty(request.ServiceLevel) ? null :
            MapServiceLevelToProduct(request.ServiceLevel, IsInternational(request.OriginCountry, request.DestinationCountry));
        if (!string.IsNullOrEmpty(request.ServiceLevel) && requestedProduct == null)
            return new ShippingQuoteResult { ErrorCode = "DHL_SERVICE_UNSUPPORTED", ErrorMessage = "Requested DHL product mapping is not supported." };

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
                        // MyDHL requires the property even where the country has no postcode.
                        PostalCode = request.OriginZipCode ?? string.Empty
                    },
                    ReceiverDetails = new DhlAddressDetails
                    {
                        CityName = request.DestinationCity,
                        CountryCode = request.DestinationCountry,
                        PostalCode = request.DestinationZipCode ?? string.Empty
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
                        Dimensions = new DhlDimensions { Length = request.LengthCm.Value, Width = request.WidthCm.Value, Height = request.HeightCm.Value }
                    }
                }
            };
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rates");
            AddAuthHeaders(httpRequest, credentials);
            httpRequest.Content = JsonContent.Create(dhlRequest, options: GetJsonOptions());
            
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("DHL rate calculation failed: {StatusCode}", response.StatusCode);
                
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
            
            var quotes = new List<ShippingQuote>();
            foreach (var product in rateResponse.Products)
            {
                // Billing currency is the account quote basis; array order is not authority.
                var billingPrices = product.TotalPrice?.Where(p => p.CurrencyType == "BILLC").ToList();
                var price = billingPrices?.Count == 1 ? billingPrices[0] : null;
                var serviceLevel = MapDhlProductToServiceLevel(product.ProductCode);
                if (price?.Price is null || price.Price < 0 || !IsCurrency(price.PriceCurrency) || serviceLevel is null)
                {
                    return new ShippingQuoteResult
                    {
                        Success = false,
                        ErrorCode = "DHL_RATE_RESPONSE_INVALID",
                        ErrorMessage = "DHL did not return an unambiguous billing price, monetary currency, and supported product."
                    };
                }
                quotes.Add(new ShippingQuote
                {
                    CarrierCode = CarrierCode,
                    ServiceLevel = serviceLevel,
                    ServiceName = product.ProductName ?? GetServiceName(product.ProductCode),
                    Cost = price.Price.Value,
                    Currency = price.PriceCurrency!.ToUpperInvariant(),
                    EstimatedDeliveryDaysMin = product.DeliveryCapabilities?.EstimatedDeliveryDateAndTime != null ? 1 : 2,
                    EstimatedDeliveryDaysMax = product.DeliveryCapabilities?.EstimatedDeliveryDateAndTime != null ? 3 : 5,
                    SupportsCod = false,
                    CarrierQuoteId = product.ProductCode
                });
            }
            
            // Filter by service level if specified
            if (requestedProduct != null)
            {
                quotes = quotes
                    .Where(q => string.Equals(q.CarrierQuoteId, requestedProduct, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            if (quotes.Count == 0)
                return new ShippingQuoteResult { ErrorCode = "DHL_RATE_UNAVAILABLE", ErrorMessage = "DHL did not return an estimate for the admitted route product." };
            
            return new ShippingQuoteResult
            {
                Success = true,
                Quotes = quotes
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("DHL rating transport failed ({FailureType})", ex.GetType().Name);
            return new ShippingQuoteResult
            {
                Success = false,
                ErrorCode = "DHL_RATE_HTTP_ERROR",
                ErrorMessage = "DHL rating connection failed."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("DHL rating failed ({FailureType})", ex.GetType().Name);
            return new ShippingQuoteResult
            {
                Success = false,
                ErrorCode = "DHL_RATE_ERROR",
                ErrorMessage = "DHL rating response could not be verified."
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

        if (!ParcelMeasurements.IsMetricWeight(request.WeightKg) ||
            request.PackageCount != 1 ||
            !IsIso2(request.OriginCountry) ||
            !IsIso2(request.RecipientCountry) ||
            string.IsNullOrWhiteSpace(request.CarrierRequestId) ||
            string.IsNullOrWhiteSpace(request.InternalShipmentId) ||
            string.IsNullOrWhiteSpace(request.OrderNumber) || request.OrderNumber.Length > 35 ||
            request.InsuranceAmount is < 0 || request.DeclaredValue is < 0 ||
            (request.PieceReference != null && (request.PieceReference.Length is 0 or > 35 || request.PieceReference.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))) ||
            !ParcelMeasurements.IsMetricDimension(request.LengthCm) || !ParcelMeasurements.IsMetricDimension(request.WidthCm) || !ParcelMeasurements.IsMetricDimension(request.HeightCm) ||
            !IsRequiredText(request.OriginName, 100) || !IsRequiredText(request.RecipientName, 100) ||
            !IsRequiredText(request.OriginAddress, 45) || !IsRequiredText(request.RecipientAddress, 45) ||
            !IsOptionalText(request.RecipientAddressLine2, 45) ||
            !IsRequiredText(request.OriginCity, 45) || !IsRequiredText(request.RecipientCity, 45) ||
            !IsPostalCode(request.OriginZipCode) || !IsPostalCode(request.RecipientZipCode) ||
            !IsRequiredText(request.OriginPhone, 70) || !IsRequiredText(request.RecipientPhone, 70) ||
            !IsOptionalText(request.RecipientEmail, 70) || !IsOptionalText(request.PackageDescription, 70))
        {
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "DHL_REQUEST_CONTRACT_INVALID",
                ErrorMessage = "DHL shipment creation requires positive package measurements, one package, ISO countries, durable request identity, and an order reference of at most 35 characters."
            };
        }

        if (request.IsCashOnDelivery || request.CodAmount is > 0)
            return new CreateShipmentResult { ErrorCode = "DHL_COD_UNSUPPORTED", ErrorMessage = "DHL COD is not implemented or certified by this integration." };
        if (request.InsuranceAmount is > 0)
            return new CreateShipmentResult { ErrorCode = "DHL_INSURANCE_UNSUPPORTED", ErrorMessage = "Requested DHL insurance service is not implemented by this integration." };

        var productCode = MapServiceLevelToProduct(request.ServiceLevel, isInternational);
        if (productCode is null)
            return new CreateShipmentResult { ErrorCode = "DHL_SERVICE_UNSUPPORTED", ErrorMessage = "Requested DHL product mapping is not supported." };

        DhlExportDeclaration? exportDeclaration = null;
        if (isInternational)
        {
            var customsValidationFailure = TryBuildExportDeclaration(request.Customs, out exportDeclaration);
            if (customsValidationFailure is null &&
                (!string.Equals(request.Customs!.OriginCountryIso2, request.OriginCountry, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(request.Customs.DestinationCountryIso2, request.RecipientCountry, StringComparison.OrdinalIgnoreCase)))
                customsValidationFailure = "DHL_CUSTOMS_ROUTE_MISMATCH";
            if (customsValidationFailure is null &&
                (!TryConvertToKg(request.Customs!.ShipmentGrossWeight, request.Customs.ShipmentGrossWeightUnit, out var grossKg) ||
                 grossKg != request.WeightKg || request.Customs.Lines.Sum(l => ConvertCustomsWeight(l.NetWeight, l.NetWeightUnit)) > grossKg))
                customsValidationFailure = "DHL_CUSTOMS_WEIGHT_MISMATCH";
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
            var messageReference = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.CarrierRequestId!)))[..32];
            var dhlRequest = new DhlCreateShipmentRequest
            {
                PlannedShippingDateAndTime = DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss") + " GMT+00:00",
                Pickup = new DhlPickup
                {
                    IsRequested = false // Pickup scheduling is not implemented by this integration.
                },
                ProductCode = productCode,
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
                            PostalCode = request.OriginZipCode ?? string.Empty
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
                            PostalCode = request.RecipientZipCode ?? string.Empty
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
                                Length = request.LengthCm.Value,
                                Width = request.WidthCm.Value,
                                Height = request.HeightCm.Value
                            },
                            CustomerReferences = new List<DhlReference>
                            {
                                new DhlReference { Value = request.PieceReference ?? request.OrderNumber, TypeCode = "CU" }
                            },
                            Description = request.PackageDescription ?? "Package"
                        }
                    },
                    IsCustomsDeclarable = isInternational,
                    DeclaredValue = isInternational
                        ? request.Customs!.Lines.Sum(line => line.DeclaredValue * line.Quantity)
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
                    new DhlReference { Value = messageReference, TypeCode = "OID" }
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
            // Official request/response correlation, NOT a provider idempotency guarantee.
            httpRequest.Headers.Add("Message-Reference", messageReference);
            httpRequest.Content = JsonContent.Create(dhlRequest, options: GetJsonOptions());
            
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "DHL API error: {StatusCode}", response.StatusCode);

                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
                    return ReconciliationRequired("DHL_CREATE_HTTP_AMBIGUOUS", responseBody);
                    
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "DHL_API_ERROR",
                    ErrorMessage = TryParseError(responseBody) ?? $"HTTP {(int)response.StatusCode}",
                    RawResponse = responseBody
                };
            }
            
            if (response.StatusCode != HttpStatusCode.Created ||
                !response.Headers.TryGetValues("Message-Reference", out var returnedReferences) ||
                !returnedReferences.SequenceEqual(new[] { messageReference }, StringComparer.Ordinal))
                return ReconciliationRequired("DHL_CREATE_CORRELATION_INVALID", responseBody);

            var dhlResponse = JsonSerializer.Deserialize<DhlCreateShipmentResponse>(responseBody, GetJsonOptions());
            
            if (string.IsNullOrWhiteSpace(dhlResponse?.ShipmentTrackingNumber))
            {
                var errorMessage = "Shipment creation failed - no tracking number returned";
                _logger.LogError("DHL shipment creation failed: {Error}", errorMessage);
                
                return ReconciliationRequired("DHL_CREATE_IDENTITY_MISSING", responseBody);
            }
            
            var trackingNumber = dhlResponse.ShipmentTrackingNumber;
            if (!IsTrackingNumber(trackingNumber) || dhlResponse.Packages?.Count != request.PackageCount ||
                dhlResponse.Packages.Any(p => !IsPieceIdentifier(p.TrackingNumber)) ||
                dhlResponse.Packages.Select(p => p.TrackingNumber).Distinct(StringComparer.Ordinal).Count() != request.PackageCount)
            {
                return ReconciliationRequired("DHL_RESPONSE_IDENTITY_INVALID", responseBody);
            }

            var labels = dhlResponse.Documents?.Where(d => d.TypeCode == "label").ToList();
            var labelDoc = labels?.Count == 1 ? labels[0] : null;
            if (labelDoc is null || !string.Equals(labelDoc.ImageFormat, "PDF", StringComparison.OrdinalIgnoreCase) ||
                !IsPdfLabel(labelDoc.Content) || (labelDoc.PackageReferenceNumber.HasValue && labelDoc.PackageReferenceNumber != 1))
                return ReconciliationRequired("DHL_LABEL_RESPONSE_INVALID", responseBody);
            
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
            
            // A dispatch confirmation identifies a pickup booking, never this shipment.
            var externalReferenceId = trackingNumber;
            
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
                LabelData = labelDoc.Content,
                LabelFormat = "PDF",
                CarrierMetadata = metadata,
                RawResponse = responseBody
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("DHL HTTP request failed ({FailureType})", ex.GetType().Name);
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "DHL_CREATE_RECONCILIATION_REQUIRED",
                ErrorMessage = "DHL connection failed; the provider outcome is not established."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("DHL creation failed ({FailureType})", ex.GetType().Name);
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "DHL_CREATE_RECONCILIATION_REQUIRED",
                ErrorMessage = "DHL creation could not be verified. Reconciliation is required before retry."
            };
        }
    }
    
    public async Task<CancelShipmentResult> CancelShipmentAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        // MyDHL REST v3.3.4 supports cancellation of pickup bookings through
        // DELETE /pickups/{dispatchConfirmationNumber}; it does not publish a
        // DELETE shipment operation. Never treat a guessed route as proof that
        // the external shipment was cancelled.
        await Task.CompletedTask;
        return new CancelShipmentResult
        {
            Success = false,
            ErrorCode = "DHL_SHIPMENT_CANCELLATION_UNSUPPORTED",
            Disposition = CarrierCancellationDisposition.Unsupported,
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
            if (!IsTrackingNumber(trackingNumber))
                return new ShipmentTrackingResult { ErrorCode = "TRACKING_IDENTITY_INVALID" };
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/shipments/{Uri.EscapeDataString(trackingNumber)}/tracking?trackingView=all-checkpoints&levelOfDetail=shipment&requestGMTOffsetPerEvent=true");
            AddAuthHeaders(httpRequest, credentials);
            
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, httpRequest, cancellationToken);
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
            var shipment = trackResponse?.Shipments?.Count == 1 ? trackResponse.Shipments[0] : null;
            
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
                    shipment.ShipmentTrackingNumber,
                    trackingNumber,
                    StringComparison.Ordinal))
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_IDENTITY_MISMATCH",
                    ErrorMessage = "DHL tracking response does not match the requested shipment.",
                    RawResponse = responseBody
                };
            }
            
            if (shipment.Status != "Success" || shipment.Events?.Count is not > 0)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_STATUS_UNADMITTED",
                    ErrorMessage = "DHL returned an unknown tracking status.",
                    RawResponse = responseBody
                };
            }

            var mappedEvents = new List<TrackingEvent>();
            foreach (var trackingEvent in shipment.Events ?? new List<DhlTrackingEvent>())
            {
                var mappedStatus = TryMapDhlStatus(trackingEvent.TypeCode ?? "");
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

                if (!TryParseScanTimestamp(trackingEvent, out var timestamp))
                    return new ShipmentTrackingResult { ErrorCode = "TRACKING_TIMESTAMP_UNADMITTED", RawResponse = responseBody };
                mappedEvents.Add(new TrackingEvent
                {
                    Timestamp = timestamp,
                    Status = mappedStatus.Value,
                    Description = trackingEvent.Description ?? "",
                    Location = string.Join("; ", trackingEvent.ServiceArea?.Select(a => a.Description).Where(d => !string.IsNullOrWhiteSpace(d)) ?? Array.Empty<string?>()),
                    CarrierStatusCode = trackingEvent.TypeCode
                });
            }
            var latestEvent = mappedEvents.OrderByDescending(e => e.Timestamp).First();
            if (mappedEvents.Any(e => e.Timestamp == latestEvent.Timestamp && e.Status != latestEvent.Status))
                return new ShipmentTrackingResult { ErrorCode = "TRACKING_STATUS_AMBIGUOUS", RawResponse = responseBody };
            var currentStatus = latestEvent.Status;
            var result = new ShipmentTrackingResult
            {
                Success = true,
                CurrentStatus = currentStatus,
                CurrentStatusDescription = latestEvent.Description,
                CurrentLocation = latestEvent.Location,
                EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
                ActualDeliveryDate = currentStatus == ShipmentStatus.Delivered 
                    ? latestEvent.Timestamp
                    : null,
                RawResponse = responseBody,
                Events = mappedEvents.OrderByDescending(e => e.Timestamp).ToList()
            };
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError("DHL tracking failed ({FailureType})", ex.GetType().Name);
            return new ShipmentTrackingResult
            {
                Success = false,
                ErrorCode = "TRACKING_ERROR",
                ErrorMessage = "DHL tracking response could not be verified."
            };
        }
    }
    
    public async Task<CarrierValidationResult> ValidateCredentialsAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentials.ApiKey)) return CarrierConnectionDiagnostics.Failure("CREDENTIAL_APIKEY_MISSING");
        if (string.IsNullOrWhiteSpace(credentials.ApiSecret)) return CarrierConnectionDiagnostics.Failure("CREDENTIAL_APISECRET_MISSING");
        try
        {
            // MyDHL REST reference data is a documented authenticated, non-mutating
            // read. It does NOT verify account/rating/shipping entitlement.
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GetBaseUrl(credentials)}/reference-data?datasetName=country");
            AddAuthHeaders(request, credentials);
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, request, cancellationToken);
            var failure = CarrierConnectionDiagnostics.HttpFailure(response.StatusCode);
            if (failure != null) return failure;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("status", out _) ||
                !root.TryGetProperty("referenceData", out var sets) || sets.ValueKind != JsonValueKind.Array ||
                !sets.EnumerateArray().Any(set => set.ValueKind == JsonValueKind.Object &&
                    set.TryGetProperty("datasetName", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "country" &&
                    set.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 &&
                    data.EnumerateArray().All(row => row.ValueKind == JsonValueKind.Array && row.EnumerateArray().Any(item =>
                        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("attribute", out var attribute) &&
                        attribute.ValueKind == JsonValueKind.String && attribute.GetString() == "countryCode" &&
                        item.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String &&
                        CountryCodeHelper.TryNormalizeCountryCode(value.GetString(), out _)))))
                return CarrierConnectionDiagnostics.Failure("PROVIDER_RESPONSE_INVALID");
            return new CarrierValidationResult { Success = true };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return CarrierConnectionDiagnostics.Failure("CONNECTION_TIMEOUT"); }
        catch (HttpRequestException) { return CarrierConnectionDiagnostics.Failure("NETWORK_ERROR"); }
        catch (JsonException) { return CarrierConnectionDiagnostics.Failure("PROVIDER_RESPONSE_INVALID"); }
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
        // No official MyDHL webhook contract/trust has been certified.
        if (!SupportsOfficialWebhookContract())
            return null;
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

    private static bool SupportsOfficialWebhookContract() => false;

    private static CreateShipmentResult ReconciliationRequired(string reason, string responseBody) => new()
    {
        ErrorCode = "DHL_CREATE_RECONCILIATION_REQUIRED",
        ErrorMessage = $"{reason}: DHL may have created the shipment; do not repeat POST without reconciliation.",
        RawResponse = responseBody
    };

    private static bool IsTrackingNumber(string? value) =>
        value is { Length: 10 } && value.All(c => c is >= '0' and <= '9');

    private static bool IsPieceIdentifier(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c));

    private static bool IsPdfLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8 * 1024 * 1024)
            return false;
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.AsSpan().StartsWith("%PDF-"u8) &&
                   Encoding.ASCII.GetString(bytes).TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal);
        }
        catch (FormatException) { return false; }
    }

    private static bool TryParseScanTimestamp(DhlTrackingEvent scan, out DateTime timestamp)
    {
        timestamp = default;
        // Never invent UTC or observation time for a local scan without an offset.
        if (scan.GmtOffset is not { Length: 6 } ||
            !DateTimeOffset.TryParseExact($"{scan.Date}T{scan.Time}{scan.GmtOffset}",
                "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        timestamp = parsed.UtcDateTime;
        return true;
    }
    
    /// <summary>
    /// Maps DHL status codes to canonical POS ShipmentStatus.
    /// 
    /// CANONICAL RULES (ENFORCED):
    /// - Carrier "created" → ReadyForPickup (NEVER Pending)
    /// - Unknown status -> reject before lifecycle admission
    /// </summary>
    private static ShipmentStatus? TryMapDhlStatus(string dhlStatus)
    {
        // CRITICAL: Map all DHL statuses to canonical POS statuses ONLY
        // No DHL enums leak outside this adapter
        return dhlStatus switch
        {
            // Picked up by carrier
            "PU"
                => ShipmentStatus.PickedUp,
            
            // In transit through network
            "DF" or "AF" or "PL" or "RR" or "CR" or "AR"
                => ShipmentStatus.InTransit,
            
            // Out for final delivery
            "WC"
                => ShipmentStatus.OutForDelivery,
            
            // Successfully delivered
            "OK"
                => ShipmentStatus.Delivered,
            
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
        if (!IsCurrency(currency) || customs.Lines.Count > 999)
        {
            return "DHL_CUSTOMS_SNAPSHOT_INCOMPLETE";
        }

        var lineItems = new List<DhlExportLineItem>();
        for (var index = 0; index < customs.Lines.Count; index++)
        {
            var line = customs.Lines[index];
            if (line.DangerousGoods == true)
                return "DHL_DANGEROUS_GOODS_UNSUPPORTED";
            if (line.OrderItemId == Guid.Empty ||
                line.ProductId == Guid.Empty ||
                line.CustomsEvidenceId == Guid.Empty ||
                line.CustomsEvidenceVersionId == Guid.Empty ||
                line.EvidenceVersionNumber <= 0 ||
                string.IsNullOrWhiteSpace(line.HsCode) ||
                string.IsNullOrWhiteSpace(line.CommodityDescription) ||
                !IsIso2(line.CountryOfOriginIso2) ||
                !IsIso2(line.CountryOfManufactureIso2) ||
                line.DeclaredValue <= 0 || line.DeclaredValue > 999999999999999m ||
                !string.Equals(line.Currency, currency, StringComparison.OrdinalIgnoreCase) ||
                line.Quantity <= 0 || line.Quantity > 1000000000m || line.Quantity != decimal.Truncate(line.Quantity) ||
                line.NetWeight <= 0 || line.NetWeight > 999999999999m)
            {
                return "DHL_CUSTOMS_SNAPSHOT_INCOMPLETE";
            }

            if (!TryMapDhlQuantityUnit(line.QuantityUnit, out var dhlQuantityUnit) ||
                !TryConvertToKg(line.NetWeight, line.NetWeightUnit, out var netWeightKg))
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
                ManufacturerCountry = line.CountryOfManufactureIso2!.Trim().ToUpperInvariant(),
                Weight = new DhlExportWeight
                {
                    NetValue = netWeightKg
                }
            });
        }

        exportDeclaration = new DhlExportDeclaration
        {
            LineItems = lineItems
        };
        if (customs.Lines.Sum(line => line.DeclaredValue * line.Quantity) > 999999999999999m)
            return "DHL_CUSTOMS_DECLARED_VALUE_OUT_OF_RANGE";
        return null;
    }

    private static bool IsIso2(string? value) =>
        value is { Length: 2 } && value.All(char.IsAsciiLetter);

    private static bool IsCurrency(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length == 3 &&
        value.Length == 3 && value.All(char.IsAsciiLetter);

    private static bool IsRequiredText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private static bool IsOptionalText(string? value, int maximum) =>
        value is null || IsRequiredText(value, maximum);

    private static bool IsPostalCode(string? value) =>
        value is null || value.Length <= 12 && !value.Any(char.IsControl);

    private static bool TryConvertToKg(decimal value, string? unit, out decimal kg)
    {
        kg = unit?.Trim().ToLowerInvariant() switch
        {
            "kg" => value,
            "g" => value / 1000m,
            "lb" => value * 0.45359237m,
            "oz" => value * 0.028349523125m,
            _ => 0
        };
        kg = decimal.Round(kg, 3, MidpointRounding.AwayFromZero);
        return kg > 0;
    }

    private static decimal ConvertCustomsWeight(decimal value, string unit) => TryConvertToKg(value, unit, out var kg) ? kg : 0m;

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
    
    private static string? MapServiceLevelToProduct(string? serviceLevel, bool isInternational)
    {
        return (serviceLevel?.ToLowerInvariant()) switch
        {
            "express" => isInternational ? "P" : "N", // Express Worldwide or Domestic Express
            "economy" => "H", // Economy Select
            "domestic" when !isInternational => "N",
            _ => null
        };
    }
    
    private static string? MapDhlProductToServiceLevel(string? productCode)
    {
        return productCode?.ToUpperInvariant() switch
        {
            "P" => "express",
            "H" => "economy",
            "N" => "domestic",
            _ => null
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
    public decimal? GrossValue { get; set; }
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
    public decimal? Price { get; set; }

    [JsonPropertyName("priceCurrency")]
    public string? PriceCurrency { get; set; }
    
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
    [JsonPropertyName("imageFormat")]
    public string? ImageFormat { get; set; }

    [JsonPropertyName("packageReferenceNumber")]
    public int? PackageReferenceNumber { get; set; }
    [JsonPropertyName("typeCode")]
    public string? TypeCode { get; set; }
    
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal class DhlCreateShipmentResponse
{
    [JsonPropertyName("packages")]
    public List<DhlCreatedPackage>? Packages { get; set; }
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

internal class DhlCreatedPackage
{
    [JsonPropertyName("trackingNumber")]
    public string? TrackingNumber { get; set; }
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
    [JsonPropertyName("date")]
    public string? Date { get; set; }
    [JsonPropertyName("time")]
    public string? Time { get; set; }
    [JsonPropertyName("GMTOffset")]
    public string? GmtOffset { get; set; }
    [JsonPropertyName("typeCode")]
    public string? TypeCode { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("serviceArea")]
    public List<DhlServiceArea>? ServiceArea { get; set; }
}

internal class DhlShipmentTracking
{
    [JsonPropertyName("shipmentTrackingNumber")]
    public string? ShipmentTrackingNumber { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
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
