namespace PointOfSale.Core.Entities.CommerceHubFinance;

public class CommerceHubDailyFinancialAggregate
{
    public DateTime DateUtc { get; set; }
    public string CurrencyCode { get; set; } = "EGP";
    public string BaseCurrencyCode { get; set; } = "EGP";
    public decimal? ExchangeRateToBase { get; set; } = 1m;
    public decimal GrossSales { get; set; }
    public decimal GrossCommission { get; set; }
    public decimal PromoCost { get; set; }
    public decimal PromoReversalAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal RefundImpactOnRevenue { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal? GrossSalesBase { get; set; }
    public decimal? GrossCommissionBase { get; set; }
    public decimal? PromoCostBase { get; set; }
    public decimal? PromoReversalAmountBase { get; set; }
    public decimal? RefundAmountBase { get; set; }
    public decimal? RefundImpactOnRevenueBase { get; set; }
    public decimal? NetRevenueBase { get; set; }
    public int OrdersCount { get; set; }
    public int RefundCount { get; set; }
    public int RefundedOrdersCount { get; set; }
    public bool IsValid { get; set; } = true;
    public DateTime? LastValidatedAtUtc { get; set; }
    public string? ValidationError { get; set; }
    public DateTime? AggregatedThroughUtc { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
}
