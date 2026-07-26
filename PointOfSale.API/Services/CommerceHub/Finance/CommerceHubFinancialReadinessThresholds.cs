namespace PointOfSale.API.Services.CommerceHub.Finance;

internal static class CommerceHubFinancialReadinessThresholds
{
    internal static readonly TimeSpan ProcessingPayoutMaxLag = TimeSpan.FromHours(2);
}
