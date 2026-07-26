namespace PointOfSale.API.Exceptions;

/// <summary>
/// AF-12: Exception thrown when a product is not eligible for the current context.
/// Used when attempting to add non-affiliate products to cart in Affiliate Mode.
/// </summary>
public class ProductNotEligibleException : Exception
{
    public Guid ProductId { get; }
    
    public ProductNotEligibleException(Guid productId, string message) : base(message)
    {
        ProductId = productId;
    }
}
