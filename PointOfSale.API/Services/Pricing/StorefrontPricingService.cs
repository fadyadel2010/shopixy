using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Services.Pricing;

public interface IStorefrontPricingService
{
    Task<StorefrontPricingBreakdown> CalculateCartPreviewAsync(
        Guid tenantId,
        CartDto cart,
        ShippingMethodDocument? shippingMethod,
        PaymentMethodDocument? paymentMethod,
        string? shippingCountry,
        CancellationToken cancellationToken = default);

    Task<StorefrontPricingBreakdown> CalculateCheckoutAsync(
        Guid tenantId,
        CartSnapshot snapshot,
        ShippingMethodDocument? shippingMethod,
        PaymentMethodDocument? paymentMethod,
        string? shippingCountry,
        CancellationToken cancellationToken = default);
}

public sealed class StorefrontPricingBreakdown
{
    public decimal Subtotal { get; init; }
    public decimal Shipping { get; init; }
    public decimal Tax { get; init; }
    public decimal CouponDiscount { get; init; }
    public decimal PromotionDiscount { get; init; }
    public decimal ManualDiscount { get; init; }
    public decimal TotalDiscount { get; init; }
    public decimal CartPromotionDiscount { get; init; }
    public decimal PaymentFee { get; init; }
    public decimal FinalTotal { get; init; }
    public decimal TaxRate { get; init; }
    public string TaxSource { get; init; } = string.Empty;
    public string? TaxCountryCode { get; init; }
    public bool PricesRefreshed { get; init; }

    public decimal Discount => TotalDiscount;

    public decimal Total => FinalTotal;
}

public sealed class StorefrontPricingService : IStorefrontPricingService
{
    private const int CurrencyScale = 2;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly ITaxCalculationService _taxCalculationService;

    public StorefrontPricingService(
        IShippingCalculationService shippingCalculationService,
        ITaxCalculationService taxCalculationService)
    {
        _shippingCalculationService = shippingCalculationService;
        _taxCalculationService = taxCalculationService;
    }

    public async Task<StorefrontPricingBreakdown> CalculateCartPreviewAsync(
        Guid tenantId,
        CartDto cart,
        ShippingMethodDocument? shippingMethod,
        PaymentMethodDocument? paymentMethod,
        string? shippingCountry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cart);

        return await BuildBreakdownAsync(
            tenantId,
            new PricingRequestTotals(
                Subtotal: RoundCurrency(cart.Items.Sum(item => item.UnitPrice * item.Quantity)),
                TotalWeight: CalculateTotalWeight(cart.Items.Select(item => (item.Weight, item.Quantity))),
                CouponDiscount: RoundCurrency(cart.DiscountTotal),
                ItemPromotionDiscount: RoundCurrency(cart.PromotionDiscountTotal),
                CartPromotionDiscount: RoundCurrency(cart.CartPromotionDiscountTotal),
                ManualDiscount: 0m),
            shippingMethod,
            paymentMethod,
            shippingCountry,
            pricesRefreshed: false,
            cancellationToken);
    }

    public async Task<StorefrontPricingBreakdown> CalculateCheckoutAsync(
        Guid tenantId,
        CartSnapshot snapshot,
        ShippingMethodDocument? shippingMethod,
        PaymentMethodDocument? paymentMethod,
        string? shippingCountry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return await BuildBreakdownAsync(
            tenantId,
            new PricingRequestTotals(
                Subtotal: RoundCurrency(snapshot.Items.Sum(item => item.CurrentPrice * item.Quantity)),
                TotalWeight: CalculateTotalWeight(snapshot.Items.Select(item => (item.Weight, item.Quantity))),
                CouponDiscount: RoundCurrency(snapshot.CouponDiscountTotal > 0m ? snapshot.CouponDiscountTotal : snapshot.DiscountTotal),
                ItemPromotionDiscount: RoundCurrency(snapshot.PromotionDiscountTotal),
                CartPromotionDiscount: RoundCurrency(snapshot.CartPromotionDiscountTotal),
                ManualDiscount: RoundCurrency(snapshot.ManualDiscountTotal)),
            shippingMethod,
            paymentMethod,
            shippingCountry,
            pricesRefreshed: false,
            cancellationToken);
    }

    private async Task<StorefrontPricingBreakdown> BuildBreakdownAsync(
        Guid tenantId,
        PricingRequestTotals requestTotals,
        ShippingMethodDocument? shippingMethod,
        PaymentMethodDocument? paymentMethod,
        string? shippingCountry,
        bool pricesRefreshed,
        CancellationToken cancellationToken)
    {
        var shipping = shippingMethod == null
            ? 0m
            : _shippingCalculationService.Calculate(
                shippingMethod,
                requestTotals.Subtotal,
                requestTotals.TotalWeight);

        var taxComputation = await _taxCalculationService.CalculateAsync(
            tenantId,
            requestTotals.Subtotal,
            shippingCountry,
            cancellationToken);

        var paymentFee = paymentMethod == null
            ? 0m
            : CalculatePaymentFee(paymentMethod, requestTotals.Subtotal);

        var aggregatePromotionDiscount = RoundCurrency(
            Math.Max(0m, requestTotals.ItemPromotionDiscount + requestTotals.CartPromotionDiscount));
        var breakdown = new DiscountBreakdown(
            CouponDiscount: requestTotals.CouponDiscount,
            PromotionDiscount: aggregatePromotionDiscount,
            ManualDiscount: requestTotals.ManualDiscount);
        var total = RoundCurrency(requestTotals.Subtotal - breakdown.TotalDiscount + shipping + taxComputation.TaxAmount + paymentFee);

        return new StorefrontPricingBreakdown
        {
            Subtotal = requestTotals.Subtotal,
            Shipping = shipping,
            Tax = taxComputation.TaxAmount,
            CouponDiscount = breakdown.CouponDiscount,
            PromotionDiscount = breakdown.PromotionDiscount,
            ManualDiscount = breakdown.ManualDiscount,
            TotalDiscount = breakdown.TotalDiscount,
            CartPromotionDiscount = requestTotals.CartPromotionDiscount,
            PaymentFee = paymentFee,
            FinalTotal = total,
            TaxRate = taxComputation.TaxRate,
            TaxSource = taxComputation.Source,
            TaxCountryCode = taxComputation.CountryCode,
            PricesRefreshed = pricesRefreshed
        };
    }

    public static decimal CalculatePaymentFee(PaymentMethodDocument method, decimal subtotal)
    {
        if (method.Fee <= 0m)
        {
            return 0m;
        }

        var calculatedFee = method.FeeType?.ToLowerInvariant() switch
        {
            "percentage" => subtotal * method.Fee / 100m,
            "fixed" => method.Fee,
            _ => method.Fee
        };

        return RoundCurrency(Math.Max(0m, calculatedFee));
    }

    private static decimal RoundCurrency(decimal value)
    {
        return Math.Round(value, CurrencyScale, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateTotalWeight(IEnumerable<(decimal Weight, int Quantity)> lines) =>
        lines.Sum(line => Math.Max(0m, line.Weight) * Math.Max(0, line.Quantity));

    private readonly record struct PricingRequestTotals(
        decimal Subtotal,
        decimal TotalWeight,
        decimal CouponDiscount,
        decimal ItemPromotionDiscount,
        decimal CartPromotionDiscount,
        decimal ManualDiscount);
}
