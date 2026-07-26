using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;
using Stripe;

namespace PointOfSale.API.Services.Payments.Stripe;

public sealed class StripeCommerceHubRefundGateway : IProviderRefundGateway
{
    private readonly CommerceHubPaymentsOptions _paymentsOptions;
    private readonly ILogger<StripeCommerceHubRefundGateway> _logger;

    public StripeCommerceHubRefundGateway(
        IOptions<CommerceHubPaymentsOptions> paymentsOptions,
        ILogger<StripeCommerceHubRefundGateway> logger)
    {
        _paymentsOptions = paymentsOptions.Value;
        _logger = logger;
    }

    public ProviderCode ProviderCode { get; } = new("Stripe");

    public async Task<CommerceHubGatewayRefundResult> RefundAsync(
        CanonicalRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        var payment = request.Payment ?? throw new InvalidOperationException("Stripe refund requires a payment.");
        var stripeOptions = _paymentsOptions.Stripe;
        if (!stripeOptions.Enabled || string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
        {
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, null, null, "CommerceHub Stripe refunds are not configured.");
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderIntentId) && string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
        {
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, null, null, "Stripe refund is missing both payment intent and charge references.");
        }

        var options = new RefundCreateOptions
        {
            Amount = ConvertToMinorUnits(request.Amount, request.Currency),
            Reason = RefundReasons.RequestedByCustomer,
            Metadata = new Dictionary<string, string>
            {
                ["commerceHubPaymentId"] = payment.Id.ToString("N"),
                ["idempotencyKey"] = request.IdempotencyKey,
                ["reason"] = request.Reason
            }
        };

        if (!string.IsNullOrWhiteSpace(payment.ProviderIntentId))
        {
            options.PaymentIntent = payment.ProviderIntentId;
        }
        else
        {
            options.Charge = payment.ProviderTransactionId;
        }

        try
        {
            var refund = await new RefundService().CreateAsync(
                options,
                new RequestOptions { IdempotencyKey = request.IdempotencyKey, ApiKey = stripeOptions.SecretKey },
                cancellationToken);

            var refundStatus = refund.Status ?? "unknown";
            if (IsRejected(refundStatus))
            {
                return new CommerceHubGatewayRefundResult(
                    false,
                    false,
                    ProviderCode.Value,
                    refund.Id,
                    refundStatus,
                    refund.FailureReason ?? $"Stripe refund status is {refund.Status}.");
            }

            return new CommerceHubGatewayRefundResult(
                true,
                string.Equals(refund.Status, "succeeded", StringComparison.OrdinalIgnoreCase),
                ProviderCode.Value,
                refund.Id,
                refundStatus,
                null);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "CommerceHub Stripe refund failed | PaymentId: {PaymentId}", payment.Id);
            return new CommerceHubGatewayRefundResult(
                false,
                false,
                ProviderCode.Value,
                null,
                ex.StripeError?.Code,
                ex.StripeError?.Message ?? ex.Message);
        }
    }

    public async Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CanonicalRefundStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var payment = request.Payment ?? throw new InvalidOperationException("Stripe refund status refresh requires a payment.");
        var stripeOptions = _paymentsOptions.Stripe;
        if (!stripeOptions.Enabled || string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
        {
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, request.RefundReference, null, "CommerceHub Stripe refunds are not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.RefundReference))
        {
            return new CommerceHubGatewayRefundResult(false, false, ProviderCode.Value, null, null, "Stripe refund status refresh requires a refund reference.");
        }

        try
        {
            var refund = await new RefundService().GetAsync(
                request.RefundReference,
                requestOptions: new RequestOptions { ApiKey = stripeOptions.SecretKey },
                cancellationToken: cancellationToken);

            var status = refund.Status ?? "unknown";
            if (IsRejected(status))
            {
                return new CommerceHubGatewayRefundResult(
                    false,
                    false,
                    ProviderCode.Value,
                    refund.Id,
                    status,
                    refund.FailureReason ?? $"Stripe refund status is {status}.");
            }

            return new CommerceHubGatewayRefundResult(
                true,
                IsConfirmed(status),
                ProviderCode.Value,
                refund.Id,
                status,
                null);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "CommerceHub Stripe refund status refresh failed | PaymentId: {PaymentId}", payment.Id);
            return new CommerceHubGatewayRefundResult(
                false,
                false,
                ProviderCode.Value,
                request.RefundReference,
                ex.StripeError?.Code,
                ex.StripeError?.Message ?? ex.Message);
        }
    }

    private static bool IsRejected(string? status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase);

    private static bool IsConfirmed(string? status) =>
        string.Equals(status, "confirmed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);

    private static long ConvertToMinorUnits(decimal amount, string currency) =>
        UsesZeroDecimalCurrency(currency)
            ? (long)Math.Round(amount, MidpointRounding.AwayFromZero)
            : (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static bool UsesZeroDecimalCurrency(string currency) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
        currency.Equals("KRW", StringComparison.OrdinalIgnoreCase) ||
        currency.Equals("VND", StringComparison.OrdinalIgnoreCase);
}
