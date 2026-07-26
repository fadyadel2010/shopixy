namespace PointOfSale.Core.Enums;

/// <summary>
/// SECURITY: Fulfillment Request Source Tracking
/// 
/// Every fulfillment transition MUST declare its source.
/// This prevents:
/// - Webhooks from updating manual orders
/// - Admins from updating carrier orders
/// - Unauthorized bypass attempts
/// 
/// CRITICAL: Caller MUST explicitly pass this. No defaults allowed.
/// </summary>
public enum FulfillmentRequestSource
{
    /// <summary>
    /// Request originated from admin/POS UI.
    /// ONLY allowed for Manual fulfillment orders.
    /// </summary>
    Manual = 0,
    
    /// <summary>
    /// Request originated from validated carrier webhook.
    /// ONLY allowed for Carrier fulfillment orders.
    /// MUST include webhook signature validation.
    /// </summary>
    Webhook = 1,
    
    /// <summary>
    /// Request originated from internal system process (e.g., automated workflow).
    /// Allowed for both Manual and Carrier orders.
    /// Used for: Auto-cancellation, timeout handling, etc.
    /// </summary>
    System = 2
}

/// <summary>
/// Extension methods for FulfillmentRequestSource.
/// </summary>
public static class FulfillmentRequestSourceExtensions
{
    /// <summary>
    /// Returns true if this source is allowed to update the given fulfillment mode.
    /// </summary>
    public static bool IsAllowedFor(this FulfillmentRequestSource source, FulfillmentMode mode)
    {
        return (source, mode) switch
        {
            // Manual source can only update Manual orders
            (FulfillmentRequestSource.Manual, FulfillmentMode.Manual) => true,
            (FulfillmentRequestSource.Manual, FulfillmentMode.Carrier) => false,
            
            // Webhook source can only update Carrier orders
            (FulfillmentRequestSource.Webhook, FulfillmentMode.Manual) => false,
            (FulfillmentRequestSource.Webhook, FulfillmentMode.Carrier) => true,
            
            // System source can update both (for automated workflows)
            (FulfillmentRequestSource.System, _) => true,
            
            _ => false
        };
    }
    
    /// <summary>
    /// Returns human-readable description for logging.
    /// </summary>
    public static string GetDescription(this FulfillmentRequestSource source)
    {
        return source switch
        {
            FulfillmentRequestSource.Manual => "Manual (Admin/POS)",
            FulfillmentRequestSource.Webhook => "Webhook (Carrier)",
            FulfillmentRequestSource.System => "System (Automated)",
            _ => source.ToString()
        };
    }
}
