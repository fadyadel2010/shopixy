namespace PointOfSale.Core.Exceptions.Affiliate;

public sealed class AffiliatePayoutAuthorityException : InvalidOperationException
{
    public AffiliatePayoutAuthorityException(string message)
        : base(message)
    {
    }
}
