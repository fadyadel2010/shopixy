using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Payments.Administration;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentAdmin3ProviderConnectivityAndHealthTests
{
    [Fact]
    public async Task TestConnection_SuccessAuthenticatesAndReturnsCanonicalHealthyResult()
    {
        var service = CreateService(new FakePaymobClient(new PaymobAdapterAuthResponse(
            "provider-token",
            new PaymobMerchantProfileResponse { Id = 123 })));

        var result = await service.TestConnectionAsync("paymob");

        Assert.True(result.Result.Success);
        Assert.Equal("Warning", result.Result.Status);
        Assert.Equal("PAYMOB", result.Result.Provider);
        Assert.Equal("Sandbox", result.Result.Environment);
        Assert.Equal("Paymob Accept API", result.Result.ProviderVersion);
        Assert.NotEmpty(result.Result.Capabilities);
        Assert.Equal("Warning", result.Health.Status);
        Assert.NotNull(result.Health.LastSuccessfulCheckUtc);
        Assert.NotEmpty(result.Result.Warnings);
    }

    [Fact]
    public async Task TestConnection_AuthenticationFailureReturnsInvalidCredentialsWithoutRawProviderPayload()
    {
        var service = CreateService(new FakePaymobClient(new PaymobOfficialApiException(
            HttpStatusCode.Unauthorized,
            "{\"api_key\":\"secret\",\"detail\":\"invalid\"}",
            "provider-request")));

        var result = await service.TestConnectionAsync("PAYMOB");
        var json = JsonSerializer.Serialize(result.Result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.False(result.Result.Success);
        Assert.Equal("Error", result.Result.Status);
        Assert.Equal("Invalid Credentials", result.Result.ErrorCategory);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-request", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnection_AuthenticatedMerchantMismatchFailsClosed()
    {
        var service = CreateService(new FakePaymobClient(new PaymobAdapterAuthResponse(
            "provider-token",
            new PaymobMerchantProfileResponse { Id = 999 })));

        var result = await service.TestConnectionAsync("PAYMOB");

        Assert.False(result.Result.Success);
        Assert.Equal("Error", result.Result.Status);
        Assert.Equal("Merchant Identity Mismatch", result.Result.ErrorCategory);
    }

    [Fact]
    public async Task TestConnection_MissingConfigurationAndMissingCredentialsAreBlockedBeforeProviderCall()
    {
        var missingConfiguration = new StubAdministrationService(
            OperationalConfiguration(enabled: true, configuration: new Dictionary<string, object?>(), credentials: ValidCredentials()));
        var configurationAction = new PaymentProviderActionService(
            missingConfiguration,
            [new ThrowingConnectivityTester()],
            NullLogger<PaymentProviderActionService>.Instance);

        var configurationResult = await configurationAction.TestConnectionAsync("PAYMOB");

        Assert.Equal("Configuration Missing", configurationResult.Result.ErrorCategory);

        var missingCredentials = new StubAdministrationService(
            OperationalConfiguration(enabled: true, configuration: ValidConfiguration(), credentials: new Dictionary<string, string>()));
        var credentialAction = new PaymentProviderActionService(
            missingCredentials,
            [new ThrowingConnectivityTester()],
            NullLogger<PaymentProviderActionService>.Instance);

        var credentialResult = await credentialAction.TestConnectionAsync("PAYMOB");

        Assert.Equal("Invalid Credentials", credentialResult.Result.ErrorCategory);
    }

    [Fact]
    public async Task TestConnection_ProviderTimeoutAndUnknownProviderReturnCanonicalResults()
    {
        var timeoutService = CreateService(new FakePaymobClient(new TaskCanceledException("timeout")));
        var timeout = await timeoutService.TestConnectionAsync("PAYMOB");

        Assert.False(timeout.Result.Success);
        Assert.Equal("Provider Timeout", timeout.Result.ErrorCategory);

        var unknownAdministration = new StubAdministrationService(
            OperationalConfiguration(enabled: true, configuration: ValidConfiguration(), credentials: ValidCredentials()),
            throwUnknown: true);
        var unknownService = new PaymentProviderActionService(
            unknownAdministration,
            [new ThrowingConnectivityTester()],
            NullLogger<PaymentProviderActionService>.Instance);

        var unknown = await unknownService.TestConnectionAsync("stripe");

        Assert.False(unknown.Result.Success);
        Assert.Equal("Unknown Provider", unknown.Result.ErrorCategory);
        Assert.Equal("STRIPE", unknown.Result.Provider);
    }

    [Fact]
    public void HealthCalculationAndCanonicalSerializationAreProviderIndependent()
    {
        var administration = new PaymentProviderAdministrationService();
        administration.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = true,
                Environment = "Production",
                Configuration = ValidConfiguration("Production"),
                SecretReplacements = ValidSecretReplacements("Production")
            });

        var updated = administration.RecordProviderActionResult(
            "paymob",
            new PaymentProviderActionResultDto
            {
                Success = true,
                Status = "Healthy",
                Provider = "PAYMOB",
                Environment = "Production",
                Message = "Provider authentication and profile verification succeeded.",
                CheckedAtUtc = DateTime.UtcNow,
                LatencyMs = 42,
                ProviderVersion = "Paymob Accept API"
            });

        var json = JsonSerializer.Serialize(new PaymentProviderActionResponse
        {
            Result = updated.Health.LatestResult!,
            Health = updated.Health,
            Permissions = PaymentProviderPermissions.All
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("Healthy", updated.Health.Status);
        Assert.Equal("Excellent", updated.Health.ConnectionQuality);
        Assert.NotNull(updated.Health.LastSuccessfulCheckUtc);
        Assert.Contains("\"success\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Healthy\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hmac", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendArchitecture_UsesCanonicalActionRouteAuditPermissionAndAvoidsPaymentExecution()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs");
        var actionService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/PaymentProviderActionService.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("[HttpPost(\"{providerCode}/actions/test-connection\")]", controller, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_CONNECTION_TESTED", controller, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderPermissions.TestConnection", controller, StringComparison.Ordinal);
        Assert.Contains("SecretsRedacted = true", controller, StringComparison.Ordinal);
        Assert.Contains("ProviderPayloadPersisted = false", controller, StringComparison.Ordinal);
        Assert.Contains("SuperAdminBaseController", controller, StringComparison.Ordinal);
        Assert.Contains("IPaymentProviderActionService", program, StringComparison.Ordinal);
        Assert.Contains("PaymobConnectivityTester", program, StringComparison.Ordinal);

        Assert.Contains("IPaymentProviderConnectivityTester", actionService, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobPaymentService", actionService, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", actionService, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterOrderAsync", actionService, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePaymentKeyAsync", actionService, StringComparison.Ordinal);
        Assert.DoesNotContain("RefundAsync", actionService, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendRendersHealthMessagesResultPanelAndTestAction()
    {
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/paymentProvidersService.ts");
        var state = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/usePaymentProviderAdministrationState.ts");
        var healthBadge = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/HealthBadge.tsx");
        var healthCard = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/ProviderHealthCard.tsx");
        var resultPanel = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/ConnectionResultPanel.tsx");
        var testButton = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/TestConnectionButton.tsx");
        var messages = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/OperationalMessages.tsx");
        var frontendSource = page + service + state + healthBadge + healthCard + resultPanel + testButton + messages;

        foreach (var component in new[]
                 {
                     "ProviderHealthCard",
                     "ConnectionResultPanel",
                     "TestConnectionButton",
                     "HealthBadge",
                     "OperationalMessages"
                 })
        {
            Assert.Contains(component, frontendSource, StringComparison.Ordinal);
        }

        Assert.Contains("/actions/test-connection", service, StringComparison.Ordinal);
        Assert.Contains("runningTest", state, StringComparison.Ordinal);
        Assert.Contains("refreshing", state, StringComparison.Ordinal);
        Assert.Contains("latestResult", state, StringComparison.Ordinal);
        Assert.Contains("historicalResult", state, StringComparison.Ordinal);
        Assert.Contains("failureMessage", state, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationArtifactExistsWithRequiredSections()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/payment/PAYMENT-ADMIN-3_PROVIDER_CONNECTIVITY_AND_HEALTH.md");

        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "Operational Architecture",
                     "Provider Action Model",
                     "Test Connection Flow",
                     "Health Model",
                     "API Design",
                     "Generic Response Contracts",
                     "Frontend Design",
                     "Security",
                     "Audit",
                     "Testing",
                     "Files Added",
                     "Files Modified",
                     "Compatibility",
                     "Readiness for PAYMENT-ADMIN-4"
                 })
        {
            Assert.Contains(heading, document, StringComparison.Ordinal);
        }
    }

    private static PaymentProviderActionService CreateService(IPaymobOfficialApiClient client)
    {
        var administration = new PaymentProviderAdministrationService();
        administration.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = true,
                Environment = "Sandbox",
                Configuration = ValidConfiguration(),
                SecretReplacements = ValidSecretReplacements()
            });

        return new PaymentProviderActionService(
            administration,
            [new PaymobConnectivityTester(client, NullLogger<PaymobConnectivityTester>.Instance)],
            NullLogger<PaymentProviderActionService>.Instance);
    }

    private static PaymentProviderOperationalConfiguration OperationalConfiguration(
        bool enabled,
        IReadOnlyDictionary<string, object?> configuration,
        IReadOnlyDictionary<string, string> credentials) =>
        new(
            "PAYMOB",
            "Paymob",
            enabled,
            "Sandbox",
            configuration,
            credentials,
            Capabilities(),
            new PaymentProviderHealthDto { Status = "Unknown", Environment = "Sandbox" });

    private static Dictionary<string, object?> ValidConfiguration(string environment = "Sandbox") =>
        new(StringComparer.Ordinal)
        {
            ["currency"] = "EGP",
            ["integrationId"] = "123456",
            ["publicKey"] = environment == "Production" ? "egy_pk_live_test-public" : "egy_pk_test_test-public",
            ["merchantIdentifier"] = "123",
            ["displayName"] = "Paymob",
            ["description"] = "Paymob provider account for CommerceHub.",
            ["timeoutSeconds"] = "30",
            ["retryCount"] = "2",
            ["requestTimeoutSeconds"] = "30",
            ["maximumRetryAttempts"] = "3",
            ["environment"] = environment,
            ["webhookUrl"] = "https://api.example.test/api/payments/paymob/webhook",
            ["browserCallbackUrl"] = "https://market.example.test/api/commercehub/payments/paymob/callback"
        };

    private static Dictionary<string, string> ValidCredentials() =>
        new(StringComparer.Ordinal)
        {
            ["apiKey"] = "paymob-test-api-key",
            ["secretKey"] = "egy_sk_test_checkout-secret",
            ["hmacSecret"] = "paymob-test-hmac-secret"
        };

    private static Dictionary<string, string?> ValidSecretReplacements(string environment = "Sandbox") =>
        new(StringComparer.Ordinal)
        {
            ["apiKey"] = "paymob-test-api-key",
            ["secretKey"] = environment == "Production"
                ? "egy_sk_live_checkout-secret"
                : "egy_sk_test_checkout-secret",
            ["hmacSecret"] = "paymob-test-hmac-secret"
        };

    private static IReadOnlyList<PaymentProviderCapabilityDto> Capabilities() =>
    [
        new PaymentProviderCapabilityDto
        {
            Code = "test_connection",
            DisplayName = "Test Connection",
            Description = "Run provider connectivity diagnostics.",
            Supported = true,
            RequiresConfiguration = true
        }
    ];

    private sealed class FakePaymobClient : IPaymobOfficialApiClient
    {
        private readonly object _result;

        public FakePaymobClient(object result)
        {
            _result = result;
        }

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(
            PaymobAuthRequest request,
            CancellationToken cancellationToken)
        {
            return _result switch
            {
                PaymobAdapterAuthResponse response => Task.FromResult(response),
                Exception exception => Task.FromException<PaymobAdapterAuthResponse>(exception),
                _ => Task.FromResult(new PaymobAdapterAuthResponse(string.Empty))
            };
        }

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(
            PaymobOrderRegistrationRequest request,
            string authToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(
            PaymobPaymentKeyRequest request,
            string authToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PaymobOperationResponse> RefundAsync(
            PaymobOperationAmountRequest request,
            string authToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PaymobOperationResponse> VoidAsync(
            PaymobOperationTransactionRequest request,
            string authToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PaymobOperationResponse> CaptureAsync(
            PaymobOperationAmountRequest request,
            string authToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PaymobOperationResponse> RetrieveTransactionAsync(
            string transactionId,
            string authToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingConnectivityTester : IPaymentProviderConnectivityTester
    {
        public string ProviderCode => "PAYMOB";

        public Task<PaymentProviderActionResultDto> TestConnectionAsync(
            PaymentProviderOperationalConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provider should not be called for validation failures.");
    }

    private sealed class StubAdministrationService : IPaymentProviderAdministrationService
    {
        private readonly PaymentProviderOperationalConfiguration _configuration;
        private readonly bool _throwUnknown;

        public StubAdministrationService(
            PaymentProviderOperationalConfiguration configuration,
            bool throwUnknown = false)
        {
            _configuration = configuration;
            _throwUnknown = throwUnknown;
        }

        public int RecordedProviderCalls { get; private set; }

        public IReadOnlyList<PaymentProviderSummaryDto> GetProviders() => Array.Empty<PaymentProviderSummaryDto>();

        public PaymentProviderDashboardDto GetDashboard(
            IReadOnlyList<PaymentProviderAdministrationActivityDto>? recentActivity = null) => new();

        public PaymentProviderConfigurationDto GetProviderConfiguration(string providerCode) => new();

        public PaymentProviderOperationalConfiguration GetProviderOperationalConfiguration(string providerCode)
        {
            if (_throwUnknown)
            {
                throw new PaymentProviderAdministrationException(
                    StatusCodes.Status404NotFound,
                    "UNKNOWN_PROVIDER",
                    "The requested payment provider is not registered.");
            }

            return _configuration;
        }

        public PaymentProviderConfigurationSaveResult SaveProviderConfiguration(
            string providerCode,
            PaymentProviderConfigurationRequest request) => new();

        public PaymentProviderConfigurationDto RecordProviderActionResult(
            string providerCode,
            PaymentProviderActionResultDto result)
        {
            RecordedProviderCalls++;
            return new PaymentProviderConfigurationDto
            {
                ProviderCode = result.Provider,
                Environment = result.Environment,
                Status = result.Status,
                Health = new PaymentProviderHealthDto
                {
                    Status = result.Status,
                    Environment = result.Environment,
                    LastFailedCheckUtc = result.Success ? null : result.CheckedAtUtc,
                    LastSuccessfulCheckUtc = result.Success ? result.CheckedAtUtc : null,
                    LastCheckDurationMs = result.LatencyMs,
                    ConnectionQuality = result.LatencyMs <= 1000 ? "Excellent" : "Good",
                    OperationalMessages = result.Errors,
                    LatestResult = result
                }
            };
        }
    }
}
