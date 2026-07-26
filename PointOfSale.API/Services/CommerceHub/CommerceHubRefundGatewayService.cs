using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubRefundGatewayService
{
    Task<CommerceHubGatewayRefundResult> RefundAsync(
        CommerceHubPayment? payment,
        decimal amount,
        string currency,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CommerceHubPayment? payment,
        string? refundReference,
        string? lastKnownGatewayStatus,
        CancellationToken cancellationToken = default);
}

public sealed record CommerceHubGatewayRefundResult(
    bool Accepted,
    bool IsConfirmed,
    string Provider,
    string? Reference,
    string? GatewayStatus,
    string? FailureReason);

public class CommerceHubRefundGatewayService : ICommerceHubRefundGatewayService
{
    private readonly IRefundGateway _refundGateway;
    private readonly ApplicationDbContext _dbContext;

    public CommerceHubRefundGatewayService(
        IRefundGateway refundGateway,
        ApplicationDbContext dbContext)
    {
        _refundGateway = refundGateway;
        _dbContext = dbContext;
    }

    public Task<CommerceHubGatewayRefundResult> RefundAsync(
        CommerceHubPayment? payment,
        decimal amount,
        string currency,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureCommerceHubPaymentCurrency(payment, currency);
        EnsureNoActiveLocalTransactionForExternalRefund();

        return _refundGateway.RefundAsync(
            new CanonicalRefundRequest(payment, amount, currency, reason, idempotencyKey),
            cancellationToken);
    }

    public Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CommerceHubPayment? payment,
        string? refundReference,
        string? lastKnownGatewayStatus,
        CancellationToken cancellationToken = default)
    {
        return _refundGateway.RefreshRefundStatusAsync(
            new CanonicalRefundStatusRequest(payment, refundReference, lastKnownGatewayStatus),
            cancellationToken);
    }

    private static void EnsureCommerceHubPaymentCurrency(CommerceHubPayment? payment, string currency)
    {
        if (!string.Equals(currency?.Trim(), FinancialCurrencyGuard.PlatformBaseCurrencyCode, StringComparison.OrdinalIgnoreCase) ||
            (payment != null && !string.Equals(payment.Currency?.Trim(), FinancialCurrencyGuard.PlatformBaseCurrencyCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"CommerceHub gateway refunds require {FinancialCurrencyGuard.PlatformBaseCurrencyCode} payment authority.");
        }
    }

    private void EnsureNoActiveLocalTransactionForExternalRefund()
    {
        if (_dbContext.Database.CurrentTransaction == null)
        {
            return;
        }

        throw new InvalidOperationException(
            "External refund provider calls cannot execute inside an active EF transaction. " +
            "Persist a durable refund command first, commit it, then execute the provider refund and confirm locally.");
    }
}
