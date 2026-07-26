using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Payments.Administration;

namespace PointOfSale.API.Services.Admin;

public interface IPaymentProviderActionService
{
    Task<PaymentProviderActionExecutionResult> TestConnectionAsync(
        string providerCode,
        CancellationToken cancellationToken = default);
}

public sealed class PaymentProviderActionService : IPaymentProviderActionService
{
    private readonly IPaymentProviderAdministrationService _administrationService;
    private readonly IReadOnlyDictionary<string, IPaymentProviderConnectivityTester> _testers;
    private readonly ILogger<PaymentProviderActionService> _logger;

    public PaymentProviderActionService(
        IPaymentProviderAdministrationService administrationService,
        IEnumerable<IPaymentProviderConnectivityTester> testers,
        ILogger<PaymentProviderActionService> logger)
    {
        _administrationService = administrationService;
        _testers = testers.ToDictionary(tester => tester.ProviderCode, tester => tester, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<PaymentProviderActionExecutionResult> TestConnectionAsync(
        string providerCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProviderCode(providerCode);
        PaymentProviderOperationalConfiguration configuration;
        try
        {
            configuration = _administrationService.GetProviderOperationalConfiguration(normalizedProvider);
        }
        catch (PaymentProviderAdministrationException exception)
        {
            var blockedResult = CreateBlockedResult(
                normalizedProvider,
                string.Empty,
                "Unknown Provider",
                exception.Message);
            return new PaymentProviderActionExecutionResult(blockedResult, new PaymentProviderHealthDto
            {
                Status = blockedResult.Status,
                Environment = blockedResult.Environment,
                LastFailedCheckUtc = blockedResult.CheckedAtUtc,
                LastCheckDurationMs = blockedResult.LatencyMs,
                ConnectionQuality = "Unknown",
                OperationalMessages = blockedResult.Errors,
                LatestResult = blockedResult
            });
        }

        var validationResult = ValidateActionAdmission(configuration);
        if (validationResult != null)
        {
            var provider = _administrationService.RecordProviderActionResult(configuration.ProviderCode, validationResult);
            return new PaymentProviderActionExecutionResult(validationResult, provider.Health);
        }

        if (!_testers.TryGetValue(configuration.ProviderCode, out var tester))
        {
            var unsupported = CreateBlockedResult(
                configuration.ProviderCode,
                configuration.Environment,
                "Unknown Provider",
                "No connectivity tester is registered for this payment provider.");
            var provider = _administrationService.RecordProviderActionResult(configuration.ProviderCode, unsupported);
            return new PaymentProviderActionExecutionResult(unsupported, provider.Health);
        }

        var result = await tester.TestConnectionAsync(configuration, cancellationToken);
        var updated = _administrationService.RecordProviderActionResult(configuration.ProviderCode, result);

        _logger.LogInformation(
            "Payment provider connection test completed | Provider: {Provider} | Environment: {Environment} | Status: {Status} | Success: {Success} | DurationMs: {DurationMs}",
            result.Provider,
            result.Environment,
            result.Status,
            result.Success,
            result.LatencyMs);

        return new PaymentProviderActionExecutionResult(result, updated.Health);
    }

    private static PaymentProviderActionResultDto? ValidateActionAdmission(
        PaymentProviderOperationalConfiguration configuration)
    {
        if (!configuration.Enabled)
        {
            return CreateBlockedResult(
                configuration.ProviderCode,
                configuration.Environment,
                "Configuration Missing",
                "Provider is disabled. Enable the provider before running connection diagnostics.");
        }

        var missingConfiguration = new List<string>();
        RequireConfiguration(configuration.Configuration, "currency", missingConfiguration);
        RequireConfiguration(configuration.Configuration, "integrationId", missingConfiguration);
        RequireConfiguration(configuration.Configuration, "iframeId", missingConfiguration);

        if (missingConfiguration.Count > 0)
        {
            return CreateBlockedResult(
                configuration.ProviderCode,
                configuration.Environment,
                "Configuration Missing",
                $"Provider configuration is incomplete: {string.Join(", ", missingConfiguration)}.");
        }

        var missingCredentials = new List<string>();
        RequireCredential(configuration.Credentials, "apiKey", missingCredentials);
        RequireCredential(configuration.Credentials, "hmacSecret", missingCredentials);

        if (missingCredentials.Count > 0)
        {
            return CreateBlockedResult(
                configuration.ProviderCode,
                configuration.Environment,
                "Invalid Credentials",
                $"Provider credentials are incomplete: {string.Join(", ", missingCredentials)}.");
        }

        return null;
    }

    private static void RequireConfiguration(
        IReadOnlyDictionary<string, object?> configuration,
        string key,
        ICollection<string> errors)
    {
        if (!configuration.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value?.ToString()))
        {
            errors.Add(key);
        }
    }

    private static void RequireCredential(
        IReadOnlyDictionary<string, string> credentials,
        string key,
        ICollection<string> errors)
    {
        if (!credentials.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            errors.Add(key);
        }
    }

    private static PaymentProviderActionResultDto CreateBlockedResult(
        string provider,
        string environment,
        string category,
        string message)
    {
        return new PaymentProviderActionResultDto
        {
            Success = false,
            Status = "Error",
            Provider = provider,
            Environment = environment,
            Message = message,
            CheckedAtUtc = DateTime.UtcNow,
            LatencyMs = 0,
            ErrorCategory = category,
            Errors = [message]
        };
    }

    private static string NormalizeProviderCode(string providerCode) =>
        string.IsNullOrWhiteSpace(providerCode)
            ? string.Empty
            : providerCode.Trim().ToUpperInvariant();
}

public interface IPaymentProviderConnectivityTester
{
    string ProviderCode { get; }

    Task<PaymentProviderActionResultDto> TestConnectionAsync(
        PaymentProviderOperationalConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public sealed class PaymobConnectivityTester : IPaymentProviderConnectivityTester
{
    private const string ProviderVersion = "Paymob Accept API";

    private readonly IPaymobOfficialApiClient _client;
    private readonly ILogger<PaymobConnectivityTester> _logger;

    public PaymobConnectivityTester(
        IPaymobOfficialApiClient client,
        ILogger<PaymobConnectivityTester> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderCode => "PAYMOB";

    public async Task<PaymentProviderActionResultDto> TestConnectionAsync(
        PaymentProviderOperationalConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var checkedAt = DateTime.UtcNow;

        try
        {
            var auth = await _client.AuthenticateAsync(
                new PaymobAuthRequest(configuration.Credentials["apiKey"]),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(auth.Token))
            {
                return CreateFailure(
                    configuration,
                    checkedAt,
                    stopwatch.ElapsedMilliseconds,
                    "Authentication Failed",
                    "Paymob authentication did not return an access token.");
            }

            var warnings = new List<string>();
            if (auth.Profile?.Id is null or <= 0)
            {
                warnings.Add("Paymob authenticated successfully but did not return merchant profile details.");
            }

            var status = warnings.Count == 0 ? "Healthy" : "Warning";
            return new PaymentProviderActionResultDto
            {
                Success = true,
                Status = status,
                Provider = configuration.ProviderCode,
                Environment = configuration.Environment,
                Message = status == "Healthy"
                    ? "Provider authentication and profile verification succeeded."
                    : "Provider authentication succeeded with operational warnings.",
                CheckedAtUtc = checkedAt,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                Warnings = warnings,
                Errors = Array.Empty<string>(),
                ErrorCategory = string.Empty,
                ProviderVersion = ProviderVersion,
                Capabilities = configuration.Capabilities
            };
        }
        catch (PaymobOfficialApiException exception)
        {
            var category = exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "Invalid Credentials"
                : "Authentication Failed";
            return CreateFailure(
                configuration,
                checkedAt,
                stopwatch.ElapsedMilliseconds,
                category,
                category == "Invalid Credentials"
                    ? "Paymob rejected the configured credentials."
                    : "Paymob authentication failed.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                exception,
                "Paymob connectivity test timed out | Provider: {Provider} | Environment: {Environment}",
                configuration.ProviderCode,
                configuration.Environment);
            return CreateFailure(
                configuration,
                checkedAt,
                stopwatch.ElapsedMilliseconds,
                "Provider Timeout",
                "Paymob did not respond before the diagnostic timeout.");
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(
                exception,
                "Paymob connectivity test timed out | Provider: {Provider} | Environment: {Environment}",
                configuration.ProviderCode,
                configuration.Environment);
            return CreateFailure(
                configuration,
                checkedAt,
                stopwatch.ElapsedMilliseconds,
                "Provider Timeout",
                "Paymob did not respond before the diagnostic timeout.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Paymob connectivity test network failure | Provider: {Provider} | Environment: {Environment}",
                configuration.ProviderCode,
                configuration.Environment);
            return CreateFailure(
                configuration,
                checkedAt,
                stopwatch.ElapsedMilliseconds,
                "Network Failure",
                "Network communication with Paymob failed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Paymob connectivity test failed | Provider: {Provider} | Environment: {Environment}",
                configuration.ProviderCode,
                configuration.Environment);
            return CreateFailure(
                configuration,
                checkedAt,
                stopwatch.ElapsedMilliseconds,
                "Unknown Error",
                "Provider connectivity diagnostics failed unexpectedly.");
        }
    }

    private static PaymentProviderActionResultDto CreateFailure(
        PaymentProviderOperationalConfiguration configuration,
        DateTime checkedAt,
        long latencyMs,
        string category,
        string message)
    {
        return new PaymentProviderActionResultDto
        {
            Success = false,
            Status = "Error",
            Provider = configuration.ProviderCode,
            Environment = configuration.Environment,
            Message = message,
            CheckedAtUtc = checkedAt,
            LatencyMs = latencyMs,
            ErrorCategory = category,
            Errors = [message],
            Warnings = Array.Empty<string>(),
            ProviderVersion = ProviderVersion,
            Capabilities = configuration.Capabilities
        };
    }
}

public sealed record PaymentProviderActionExecutionResult(
    PaymentProviderActionResultDto Result,
    PaymentProviderHealthDto Health);
