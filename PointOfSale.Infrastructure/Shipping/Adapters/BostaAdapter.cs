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
/// Installed contracts do not by themselves certify an account for production.
/// Execution additionally requires:
/// - Valid Bosta API credentials
/// - Proper API URL (production vs sandbox)
/// - Webhook secret for signature validation
/// </summary>
public partial class BostaAdapter : IShippingCarrierAdapter, IIdentityBoundCarrierCancellation, IBostaPickupLocationDiscovery, IBostaGeographyDiscovery
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
    
    public Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        // The implemented Bosta surface has no authenticated remote-rate operation.
        // Merchant fixed prices belong to ShippingMethod/CarrierConfiguration and
        // must never be presented as provider-verified Bosta quotes.
        return Task.FromResult(new ShippingQuoteResult
        {
            Success = false,
            ErrorCode = "BOSTA_REMOTE_QUOTES_UNSUPPORTED",
            ErrorMessage = "Bosta remote rate quotes are not implemented."
        });
    }
    
    public async Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        
        try
        {
            if (!string.Equals(request.ServiceLevel, "standard", StringComparison.OrdinalIgnoreCase))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_SERVICE_LEVEL_UNSUPPORTED",
                    ErrorMessage = "The Bosta request contract does not encode the selected service level."
                };
            }

            if (!string.Equals(request.OriginCountry, "EG", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.RecipientCountry, "EG", StringComparison.OrdinalIgnoreCase))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_DOMESTIC_EGYPT_ROUTE_REQUIRED",
                    ErrorMessage = "The admitted Bosta integration supports Egypt domestic shipments only."
                };
            }

            var hasExplicitPickup = credentials.ExtraConfig.TryGetValue("businessLocationId", out var configuredPickup);
            if (hasExplicitPickup && !BostaPickupSelections.ValidId(configuredPickup))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_BUSINESS_LOCATION_INVALID",
                    ErrorMessage = "The explicit Bosta pickup selection is invalid; it cannot fall back to a default."
                };
            }
            if (!hasExplicitPickup && request.BostaPickupOrigin == null)
                return new() { ErrorCode = "BOSTA_VERIFIED_DEFAULT_ORIGIN_REQUIRED", ErrorMessage = "The account default must be verified and frozen before preparing the delivery request." };

            if (!string.Equals(request.Currency, "EGP", StringComparison.Ordinal))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_CURRENCY_UNSUPPORTED",
                    ErrorMessage = "The admitted Bosta integration requires EGP currency."
                };
            }

            if (request.IsCashOnDelivery &&
                (!request.CodAmount.HasValue || request.CodAmount <= 0m || request.CodAmount > 30000m))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_COD_AMOUNT_UNSUPPORTED",
                    ErrorMessage = "Bosta COD amount must be greater than zero and no more than 30000 EGP."
                };
            }

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

            if (request.PackageCount != 1 || !ParcelMeasurements.IsMetricWeight(request.WeightKg) ||
                !ParcelMeasurements.IsMetricDimension(request.LengthCm) || !ParcelMeasurements.IsMetricDimension(request.WidthCm) ||
                !ParcelMeasurements.IsMetricDimension(request.HeightCm))
                return new() { ErrorCode = "BOSTA_MEASURED_PARCEL_REQUIRED", ErrorMessage = "One measured parcel is required." };
            if (request.BostaDestination != null && (string.IsNullOrWhiteSpace(request.RecipientName) ||
                string.IsNullOrWhiteSpace(request.RecipientAddress) || request.RecipientAddress.Trim().Length <= 5 ||
                string.IsNullOrWhiteSpace(request.RecipientCity) ||
                !System.Text.RegularExpressions.Regex.IsMatch(NormalizePhone(request.RecipientPhone ?? ""), @"^01[0125][0-9]{8}$")))
                return new() { ErrorCode = "BOSTA_RECIPIENT_ADDRESS_INVALID", ErrorMessage = "A real recipient, Egyptian mobile number and complete delivery address are required." };
            if (!BostaParcelContents.IsDescription(request.PackageDescription) || request.PackageItemCount is not > 0)
                return new() { ErrorCode = "BOSTA_PACKAGE_CONTENTS_REQUIRED", ErrorMessage = "Actual parcel contents and item count are required." };
            if (!BostaParcelContents.IsValue(request.DeclaredValue))
                return new() { ErrorCode = "BOSTA_GOODS_VALUE_REQUIRED", ErrorMessage = "An explicit positive goods value in EGP is required; COD is not goods value." };
            if (!IsBoundedIdentifier(request.CarrierRequestId, 200) || request.CarrierRequestId != request.InternalShipmentId)
                return new() { ErrorCode = "BOSTA_REQUEST_IDENTITY_REQUIRED", ErrorMessage = "A stable persisted shipment request identity is required." };

            // Resolve selection against this exact authenticated account/environment.
            // A free-text or stale pickup ID can never authorize a delivery POST.
            var pickups = await GetPickupLocationsAsync(credentials, cancellationToken);
            var selection = BostaPickupSelections.Resolve(credentials, pickups);
            if (selection.Location is not { } verifiedPickup)
                return new() { ErrorCode = selection.ErrorCode, ErrorMessage = "The explicit selection or unique provider default could not be verified for this account and environment." };
            var businessLocationId = verifiedPickup.Id;
            if (request.BostaPickupOrigin is { } origin && (origin.BusinessLocationId != businessLocationId ||
                origin.UseSandbox != credentials.UseSandbox || origin.CountryCode != verifiedPickup.CountryCode ||
                origin.CountryId != verifiedPickup.CountryId || origin.CityId != verifiedPickup.CityId || origin.DistrictId != verifiedPickup.DistrictId ||
                origin.ObservedAtUtc < DateTime.UtcNow.AddMinutes(-10) || origin.ObservedAtUtc > DateTime.UtcNow))
                return new() { ErrorCode = "BOSTA_PICKUP_CHANGED", ErrorMessage = "The provider pickup changed after the shipment intent was prepared." };

            // Build Bosta delivery request
            if (request.BostaDestination is { } destination)
            {
                if (destination.Kind != BostaNativeDestination.SnapshotKind || destination.Version != 1 ||
                    destination.UseSandbox != credentials.UseSandbox || destination.CredentialIdentityHash != BostaNativeDestination.CredentialHash(credentials) ||
                    request.RecipientCityId != destination.City.Id || request.RecipientDistrictId != destination.District.Id)
                    return new() { ErrorCode = "BOSTA_DESTINATION_ACCOUNT_CHANGED" };
                var selected = BostaNativeDestination.Select(await GetGeographyAsync(credentials, destination.City.Id, cancellationToken), destination.City.Id, destination.District.Id);
                if (selected.Country.Id != destination.Country.Id || selected.City.ParentId != destination.City.ParentId ||
                    selected.District.ParentId != destination.District.ParentId)
                    return new() { ErrorCode = "BOSTA_DESTINATION_CHANGED" };
            }
            var bostaRequest = new BostaCreateDeliveryRequest
            {
                Type = 10, // Delivery type: 10 = SEND
                Notes = request.Notes ?? "",
                Cod = request.IsCashOnDelivery ? request.CodAmount ?? 0 : 0,
                Specs = new BostaSpecs { PackageType = "Parcel", PackageDetails = new BostaPackageDetails {
                    ItemsCount = request.PackageItemCount.Value, Description = request.PackageDescription } },
                GoodsInfo = new BostaGoodsInfo { Amount = request.DeclaredValue.Value },
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
                UniqueBusinessReference = request.CarrierRequestId,
                BusinessLocationId = businessLocationId,
                // Product-image authority is not represented by the carrier-neutral
                // request, so open-package must remain disabled.
                AllowToOpenPackage = false
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
            
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Bosta API error: {StatusCode}", response.StatusCode);
                    
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_API_ERROR",
                    ErrorMessage = "Bosta rejected shipment creation. The provider outcome requires review.",
                    RawResponse = responseBody
                };
            }
            
            var bostaResponse = JsonSerializer.Deserialize<BostaCreateDeliveryResponse>(
                responseBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (bostaResponse?.Success == false)
                return new() { ErrorCode = "BOSTA_PROVIDER_DECLINED", ErrorMessage = "Bosta did not acknowledge shipment creation. Reconciliation is required.", RawResponse = responseBody };
            
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
            
            var trackingNumber = bostaResponse.Data.TrackingNumber;
            var providerDeliveryId = bostaResponse.Data.Id;
            if (!IsBoundedIdentifier(providerDeliveryId, 200) || !IsBoundedIdentifier(trackingNumber, 100))
            {
                return new CreateShipmentResult
                {
                    Success = false,
                    ErrorCode = "BOSTA_RESPONSE_IDENTITY_UNADMITTED",
                    ErrorMessage = "Bosta response requires exact delivery and tracking identities.",
                    RawResponse = responseBody
                };
            }
            
            var trackingUrl = $"https://bosta.co/tracking?tracking_number={trackingNumber}";
            
            // Store Bosta-specific metadata
            var metadata = JsonSerializer.Serialize(new
            {
                bostaId = providerDeliveryId,
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
                ExternalReferenceId = providerDeliveryId,
                TrackingNumber = trackingNumber,
                TrackingUrl = trackingUrl,
                AwbNumber = bostaResponse.Data.DeliveryId,
                InitialStatus = initialStatus,
                CarrierMetadata = metadata,
                RawResponse = responseBody
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException ex) when (ex.Message.StartsWith("BOSTA_", StringComparison.Ordinal))
        { return new() { ErrorCode = ex.Message, ErrorMessage = "Bosta destination is no longer admissible; no delivery was submitted." }; }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Bosta HTTP request failed ({FailureType})", ex.GetType().Name);
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "HTTP_ERROR",
                ErrorMessage = "Bosta connection failed; the provider outcome is not established."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Bosta creation failed ({FailureType})", ex.GetType().Name);
            return new CreateShipmentResult
            {
                Success = false,
                ErrorCode = "UNEXPECTED_ERROR",
                ErrorMessage = "Bosta creation could not be verified. Reconciliation is required before retry."
            };
        }
    }
    
    public Task<CancelShipmentResult> CancelShipmentAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A tracking string alone is not authority to mutate an external delivery.
        return Task.FromResult(new CancelShipmentResult { ErrorCode = "BOSTA_CANCELLATION_TARGET_BINDING_REQUIRED",
            ErrorMessage = "The original persisted delivery identity is required before cancellation." });
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
            
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_FAILED",
                    ErrorMessage = "Bosta tracking could not be verified.",
                    RawResponse = responseBody
                };
            }
            
            var trackingResponse = JsonSerializer.Deserialize<BostaDeliveryResponse>(
                responseBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (trackingResponse == null || trackingResponse.Data == null || trackingResponse.Success == false)
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
            if (!string.Equals(delivery.TrackingNumber, trackingNumber, StringComparison.Ordinal))
            {
                return new ShipmentTrackingResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_IDENTITY_MISMATCH",
                    ErrorMessage = "Bosta tracking response does not match the requested shipment.",
                    RawResponse = responseBody
                };
            }

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
                    if (!history.Timestamp.HasValue ||
                        history.Timestamp.Value < DateTimeOffset.UnixEpoch ||
                        history.Timestamp.Value > DateTimeOffset.UtcNow.AddMinutes(5))
                    {
                        return new ShipmentTrackingResult
                        {
                            Success = false,
                            ErrorCode = "TRACKING_EVENT_TIME_UNADMITTED",
                            ErrorMessage = "Bosta tracking history contains a missing or unsupported event timestamp.",
                            RawResponse = responseBody
                        };
                    }

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
                        Timestamp = history.Timestamp.Value.UtcDateTime,
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
            _logger.LogError("Bosta tracking failed ({FailureType})", ex.GetType().Name);
            return new ShipmentTrackingResult
            {
                Success = false,
                ErrorCode = "TRACKING_ERROR",
                ErrorMessage = "Bosta tracking response could not be verified."
            };
        }
    }
    
    public async Task<CarrierValidationResult> ValidateCredentialsAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        if (string.IsNullOrWhiteSpace(credentials.ApiKey))
            return CarrierConnectionDiagnostics.Failure("CREDENTIAL_APIKEY_MISSING");
        try
        {
            // Authenticated business read, unlike the public cities catalog.
            // Official Businessgetpickup-locations; no pickup is created or changed.
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/pickup-locations");
            request.Headers.Add("Authorization", credentials.ApiKey);
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, request, cancellationToken);
            var failure = CarrierConnectionDiagnostics.HttpFailure(response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (failure != null)
            {
                try
                {
                    using var error = JsonDocument.Parse(body);
                    var code = CarrierConnectionDiagnostics.Code(error.RootElement, "errorCode", credentials);
                    return code == null ? failure : CarrierConnectionDiagnostics.Failure($"{failure.ErrorCode}_{code}");
                }
                catch (JsonException) { return failure; }
            }
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var providerCode = CarrierConnectionDiagnostics.Code(root, "errorCode", credentials);
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array ||
                !data.TryGetProperty("total", out var total) || !total.TryGetInt32(out var count) || count != list.GetArrayLength() || count > 1000)
                return CarrierConnectionDiagnostics.Failure(providerCode == null ? "PROVIDER_RESPONSE_INVALID" : $"BOSTA_{providerCode}");
            // Pickup ownership is origin evidence, not API-key authentication.
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
                Code = "standard",
                Name = "Standard Delivery",
                Description = "Delivery within 2-4 business days",
                TypicalDaysMin = 2,
                TypicalDaysMax = 4
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
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            // Official delivery status-change contract. Legacy aliases never supply money authority.
            if (root.TryGetProperty("_id", out _) || root.TryGetProperty("trackingNumber", out _) || root.TryGetProperty("state", out _))
                return ParseOfficialDeliveryWebhook(root);
            
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

    private static WebhookParseResult? ParseOfficialDeliveryWebhook(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name)) return null; // duplicate/case-conflicting keys cannot choose an identity or amount
        if (root.TryGetProperty("EventType", out _) || root.TryGetProperty("TrackingNumber", out _)) return null;
        if (!root.TryGetProperty("_id", out var id) || id.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(id.GetString()) || id.GetString()!.Length > 200 || id.GetString() != id.GetString()!.Trim() ||
            !root.TryGetProperty("trackingNumber", out var tracking) ||
            !root.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Number || !state.TryGetInt32(out var stateCode) ||
            !root.TryGetProperty("type", out var type) || !TryParseDeliveryType(type, out var typeCode) ||
            !root.TryGetProperty("timeStamp", out var timestamp) || timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetInt64(out var milliseconds)) return null;
        var trackingNumber = tracking.ValueKind == JsonValueKind.String ? tracking.GetString() :
            tracking.ValueKind == JsonValueKind.Number ? tracking.GetRawText() : null;
        // Never round through floating point, strip leading zeroes, trim or expand exponents.
        if (string.IsNullOrEmpty(trackingNumber) || trackingNumber.Length > 100 ||
            !trackingNumber.All(c => c is >= '0' and <= '9') || trackingNumber.All(c => c == '0')) return null;
        if (milliseconds < 946684800000L || milliseconds > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()) return null;
        var eventAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        var status = stateCode switch
        {
            41 => ShipmentStatus.OutForDelivery,
            45 => ShipmentStatus.Delivered,
            46 => ShipmentStatus.Returned,
            _ => (ShipmentStatus?)null
        };
        if (!status.HasValue) return null; // exception/termination semantics are not invented here
        bool? confirmed = null;
        if (root.TryGetProperty("isConfirmedDelivery", out var confirmation))
        {
            if (confirmation.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            confirmed = confirmation.GetBoolean();
        }
        decimal? amount = null;
        var amountCode = "COD_AMOUNT_ABSENT";
        string? rejectedAmountHash = null;
        if (root.TryGetProperty("cod", out var cod))
        {
            // The documented contract is a JSON number, not a localized or floating-point string.
            if (cod.ValueKind == JsonValueKind.Number &&
                System.Text.RegularExpressions.Regex.IsMatch(cod.GetRawText(), @"^(0|[1-9][0-9]{0,11})(\.[0-9]{1,6})?$") && cod.TryGetDecimal(out var value) &&
                value >= 0m && value <= 999999999999.999999m && decimal.Round(value, 6) == value)
            {
                amount = value;
                amountCode = value > 0m ? "COD_AMOUNT_OBSERVED" : "COD_AMOUNT_ZERO";
            }
            else
            {
                amountCode = "COD_AMOUNT_INVALID";
                rejectedAmountHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cod.GetRawText())));
            }
        }
        const string contract = "bosta:delivery-status-webhook:v1";
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { contract, delivery = id.GetString(), trackingNumber, stateCode, typeCode, milliseconds,
                amount = amount?.ToString("G29", System.Globalization.CultureInfo.InvariantCulture), amountCode, rejectedAmountHash, confirmed }))));
        return new WebhookParseResult
        {
            TrackingNumber = trackingNumber, Status = status.Value, EventTimestamp = eventAt,
            StatusDescription = $"Bosta delivery state {stateCode}", CodCollected = false,
            MoneyObservation = new(id.GetString()!, stateCode, typeCode, eventAt, amount, amountCode, confirmed, contract, fingerprint)
        };
    }
    
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

    private static bool TryParseDeliveryType(JsonElement type, out int code)
    {
        if (type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out code)) return true;
        if (type.ValueKind == JsonValueKind.String && type.GetString() == "SEND")
        {
            code = 10;
            return true;
        }
        code = 0;
        return false;
    }

    private static bool IsBoundedIdentifier(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        value == value.Trim() &&
        !value.Any(char.IsControl);
    
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

    [JsonPropertyName("uniqueBusinessReference")]
    public string? UniqueBusinessReference { get; set; }

    [JsonPropertyName("goodsInfo")]
    public BostaGoodsInfo? GoodsInfo { get; set; }

    [JsonPropertyName("businessLocationId")]
    public string? BusinessLocationId { get; set; }
    
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

internal class BostaGoodsInfo
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
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
    public bool? Success { get; set; }
    public BostaDeliveryData? Data { get; set; }
    public string? Message { get; set; }
}

internal class BostaDeliveryResponse
{
    public bool? Success { get; set; }
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
            if (element.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.String)
                return valueProp.GetString() ?? "";
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
    public DateTimeOffset? Timestamp { get; set; }
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
            if (element.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.String)
                return valueProp.GetString() ?? "";
        }
        
        return "";
    }
}

#endregion
