using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Registered Mylerz integration boundary. Public vendor schemas exist, but an
/// applicable, complete Egypt merchant contract has not been established.
/// See docs/architecture/MYLERZ-2_COMPREHENSIVE_CONTRACT_REMEDIATION.md.
/// Password-token authentication alone uses MylerzPublicContractClient. Canonical
/// operations remain blocked by account, authorization, units and state semantics.
/// </summary>
public class MylerzAdapter : IShippingCarrierAdapter
{
    public const string ContractUnavailableErrorCode = "MYLERZ_ACCOUNT_CONTRACT_QUALIFICATION_REQUIRED";
    public const string ContractUnavailableErrorMessage =
        "Public Mylerz wire schemas are implemented; merchant authorization/token, warehouse/service, parcel units, billing and state semantics require independent qualification.";

    private readonly ILogger<MylerzAdapter> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public string CarrierCode => "mylerz";
    public string DisplayName => "Mylerz";

    // Construct an outbound client only for the explicit password-token test.
    public MylerzAdapter(IHttpClientFactory httpClientFactory, ILogger<MylerzAdapter> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        Block(cancellationToken);
        return Task.FromResult(new ShippingQuoteResult
        {
            Success = false,
            ErrorCode = ContractUnavailableErrorCode,
            ErrorMessage = ContractUnavailableErrorMessage
        });
    }

    public Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        Block(cancellationToken);
        return Task.FromResult(new CreateShipmentResult
        {
            Success = false,
            ErrorCode = ContractUnavailableErrorCode,
            ErrorMessage = ContractUnavailableErrorMessage
        });
    }

    /// <summary>
    /// No verified merchant-request lookup can resolve an uncertain creation.
    /// Null never authorizes the shared caller to submit a replacement shipment.
    /// </summary>
    public Task<CreateShipmentResult?> GetShipmentByCarrierRequestIdAsync(
        string carrierRequestId, CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        Block(cancellationToken);
        return Task.FromResult<CreateShipmentResult?>(null);
    }

    public Task<CancelShipmentResult> CancelShipmentAsync(
        string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        Block(cancellationToken);
        return Task.FromResult(new CancelShipmentResult
        {
            Success = false,
            Disposition = CarrierCancellationDisposition.Unsupported,
            ErrorCode = ContractUnavailableErrorCode,
            ErrorMessage = ContractUnavailableErrorMessage
        });
    }

    public Task<ShipmentTrackingResult> GetTrackingAsync(
        string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        Block(cancellationToken);
        return Task.FromResult(new ShipmentTrackingResult
        {
            Success = false,
            ErrorCode = ContractUnavailableErrorCode,
            ErrorMessage = ContractUnavailableErrorMessage
        });
    }

    public async Task<CarrierValidationResult> ValidateCredentialsAsync(
        CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentials.ApiKey)) return CarrierConnectionDiagnostics.Failure("CREDENTIAL_APIKEY_MISSING");
        if (string.IsNullOrWhiteSpace(credentials.ApiSecret)) return CarrierConnectionDiagnostics.Failure("CREDENTIAL_APISECRET_MISSING");
        if (credentials.UseSandbox || credentials.ApiUrl != null)
            return CarrierConnectionDiagnostics.Failure("MYLERZ_ENVIRONMENT_QUALIFICATION_REQUIRED");
        try
        {
            // Only the documented password-token contract is tested. No token
            // header convention, warehouse/service access or Egypt entitlement is inferred.
            var client = new MylerzPublicContractClient(_httpClientFactory.CreateClient("Mylerz"));
            await client.RequestTokenAsync(credentials.ApiKey, credentials.ApiSecret, cancellationToken);
            return new CarrierValidationResult { Success = true };
        }
        catch (MylerzContractException ex) { return CarrierConnectionDiagnostics.Failure(ex.Code); }
    }

    public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() => Array.Empty<ServiceLevelInfo>();

    public WebhookParseResult? ParseWebhook(string payload, string? signature, string? webhookSecret) => null;

    private void Block(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogWarning("Mylerz operation blocked: {ErrorCode}", ContractUnavailableErrorCode);
    }
}
