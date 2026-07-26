
/// <summary>
/// Request to apply an AI-generated order draft to the cart.
/// </summary>
public class ApplyOrderDraftRequest
{
    /// <summary>
    /// Items to add to the cart.
    /// </summary>
    public List<OrderDraftItemRequest> Items { get; set; } = new();

    /// <summary>
    /// Optional shipping method to select.
    /// </summary>
    public Guid? ShippingMethodId { get; set; }
}

/// <summary>
/// Item in the order draft request.
/// </summary>
public class OrderDraftItemRequest
{
    /// <summary>
    /// Product ID (legacy).
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Variant ID (required for variant products).
    /// </summary>
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Quantity to add.
    /// </summary>
    public int Quantity { get; set; }
}
