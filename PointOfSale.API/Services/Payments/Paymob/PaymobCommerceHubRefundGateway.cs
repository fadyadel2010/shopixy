using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Payments.Paymob;

public sealed class PaymobCommerceHubRefundGateway : IProviderRefundGateway
{
    private const string PaymobBaseUrl = "https://accept.paymob.com/api/";
    private const string PaymobAuthUrl = "auth/tokens";
    private const string PaymobRefundUrl = "acceptance/void_refund/refund";
    private const string PaymobTransactionRetrieveUrl = "acceptance/transactions/{0}";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CommerceHubPaymobOptions _paymobOptions;
    private readonly ILogger<PaymobCommerceHubRefundGateway> _logger;

    public PaymobCommerceHubRefundGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<CommerceHubPaymobOptions> paymobOptions,
        ILogger<PaymobCommerceHubRefundGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _paymobOptions = paymobOptions.Value;
        _logger = logger;
    }

    public ProviderCode ProviderCode { get; } = new("Paymob");

    public async Task<CommerceHubGatewayRefundResult> RefundAsync(
        CanonicalRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        var payment = request.Payment ?? throw new InvalidOperationException("Paymob refund requires a payment.");
        if (!_paymobOptions.Enabled || string.IsNullOrWhiteSpace(_paymobOptions.ApiKey))
        {
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, null, null, "CommerceHub Paymob refunds are not configured.");
        }

        var paymentReference = payment.ProviderTransactionId ?? payment.ProviderIntentId;
        if (!long.TryParse(paymentReference, out var transactionId))
        {
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, paymentReference, null, "Paymob refund requires a numeric transaction reference.");
        }

        try
        {
            var authToken = await AuthenticatePaymobAsync(_paymobOptions.ApiKey, cancellationToken);
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(PaymobBaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            client.DefaultRequestHeaders.Add("Idempotency-Key", request.IdempotencyKey);

            using var content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    auth_token = authToken,
                    transaction_id = transactionId,
                    amount_cents = ConvertToMinorUnits(request.Amount, request.Currency)
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await client.PostAsync(PaymobRefundUrl, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CommerceHubGatewayRefundResult(
                    false,
                    false,
                    ProviderCode.Value,
                    paymentReference,
                    response.StatusCode.ToString(),
                    $"Paymob refund failed with status {(int)response.StatusCode}: {responseBody}");
            }

            return BuildInitialRefundResultFromJson(payment, paymentReference, responseBody);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
        {
            _logger.LogWarning(ex, "CommerceHub Paymob refund failed | PaymentId: {PaymentId}", payment.Id);
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, paymentReference, null, ex.Message);
        }
    }

    public async Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CanonicalRefundStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var payment = request.Payment ?? throw new InvalidOperationException("Paymob refund status refresh requires a payment.");
        if (!_paymobOptions.Enabled || string.IsNullOrWhiteSpace(_paymobOptions.ApiKey))
        {
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, request.RefundReference, null, "CommerceHub Paymob refunds are not configured.");
        }

        var resolvedReference = !string.IsNullOrWhiteSpace(request.RefundReference)
            ? request.RefundReference.Trim()
            : payment.ProviderTransactionId ?? payment.ProviderIntentId;

        if (!long.TryParse(resolvedReference, out var transactionId))
        {
            return new CommerceHubGatewayRefundResult(
                false,
                false,
                ProviderCode.Value,
                resolvedReference,
                request.LastKnownGatewayStatus,
                "Paymob refund status refresh requires a numeric refund reference.");
        }

        try
        {
            var authToken = await AuthenticatePaymobAsync(_paymobOptions.ApiKey, cancellationToken);
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(PaymobBaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

            using var response = await client.GetAsync(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, PaymobTransactionRetrieveUrl, transactionId),
                cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CommerceHubGatewayRefundResult(
                    false,
                    false,
                    ProviderCode.Value,
                    resolvedReference,
                    response.StatusCode.ToString(),
                    $"Paymob refund status refresh failed with status {(int)response.StatusCode}: {responseBody}");
            }

            return BuildStatusResultFromJson(payment, resolvedReference, responseBody, request.LastKnownGatewayStatus);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
        {
            _logger.LogWarning(ex, "CommerceHub Paymob refund status refresh failed | PaymentId: {PaymentId}", payment.Id);
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, resolvedReference, request.LastKnownGatewayStatus, ex.Message);
        }
    }

    private async Task<string> AuthenticatePaymobAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(PaymobBaseUrl);

        using var content = new StringContent(
            JsonSerializer.Serialize(new { api_key = apiKey }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(PaymobAuthUrl, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("token", out var tokenElement))
        {
            return tokenElement.GetString() ?? throw new InvalidOperationException("Paymob authentication did not return a token.");
        }

        throw new InvalidOperationException("Paymob authentication did not return a token.");
    }

    private static CommerceHubGatewayRefundResult BuildStatusResultFromJson(
        CommerceHubPayment payment,
        string? refundReference,
        string responseBody,
        string? lastKnownGatewayStatus)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var providerReference = TryReadJsonString(root, "id") ?? TryReadJsonString(root, "refund_id") ?? refundReference;
            var success = TryReadJsonBoolean(root, "success");
            var pending = TryReadJsonBoolean(root, "pending");
            var isRefunded = TryReadJsonBoolean(root, "is_refunded");
            var isVoided = TryReadJsonBoolean(root, "is_voided");
            var hasError = TryReadJsonBoolean(root, "error_occured") ?? TryReadJsonBoolean(root, "error_occurred");

            if (hasError == true || (success == false && pending != true))
            {
                return new CommerceHubGatewayRefundResult(false, false, "Paymob", providerReference, "failed", "Paymob refund status refresh returned a failed refund status.");
            }

            var isConfirmed = isRefunded == true || isVoided == true;
            return new CommerceHubGatewayRefundResult(true, isConfirmed, "Paymob", providerReference, isConfirmed ? "confirmed" : lastKnownGatewayStatus ?? "accepted", null);
        }
        catch (JsonException ex)
        {
            return new CommerceHubGatewayRefundResult(false, false, "Paymob", refundReference, lastKnownGatewayStatus, $"Paymob refund status refresh returned an unreadable response for payment {payment.Id}: {ex.Message}");
        }
    }

    private static CommerceHubGatewayRefundResult BuildInitialRefundResultFromJson(
        CommerceHubPayment payment,
        string? paymentReference,
        string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var providerReference = TryReadJsonString(root, "id") ?? TryReadJsonString(root, "refund_id") ?? paymentReference;
            var success = TryReadJsonBoolean(root, "success");
            var pending = TryReadJsonBoolean(root, "pending");
            var isRefunded = TryReadJsonBoolean(root, "is_refunded");
            var isVoided = TryReadJsonBoolean(root, "is_voided");
            var hasError = TryReadJsonBoolean(root, "error_occured") ?? TryReadJsonBoolean(root, "error_occurred");

            if (hasError == true || (success == false && pending != true))
            {
                return new CommerceHubGatewayRefundResult(false, false, "Paymob", providerReference, "failed", $"Paymob refund execution returned a failed refund status for payment {payment.Id}.");
            }

            var isConfirmed = isRefunded == true || isVoided == true;
            return new CommerceHubGatewayRefundResult(true, isConfirmed, "Paymob", providerReference, isConfirmed ? "confirmed" : "accepted", null);
        }
        catch (JsonException)
        {
            return new CommerceHubGatewayRefundResult(true, false, "Paymob", paymentReference, "accepted_unverified", null);
        }
    }

    private static bool? TryReadJsonBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string? TryReadJsonString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element)
            ? element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString()
            : null;

    private static long ConvertToMinorUnits(decimal amount, string currency) =>
        UsesZeroDecimalCurrency(currency)
            ? (long)Math.Round(amount, MidpointRounding.AwayFromZero)
            : (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static bool UsesZeroDecimalCurrency(string currency) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
        currency.Equals("KRW", StringComparison.OrdinalIgnoreCase) ||
        currency.Equals("VND", StringComparison.OrdinalIgnoreCase);
}
