namespace PointOfSale.Core.Enums;

/// <summary>
/// FULFILLMENT OWNERSHIP ARCHITECTURE
/// 
/// Defines WHO controls the fulfillment lifecycle of an order.
/// This is snapshotted at Order creation and is IMMUTABLE.
/// 
/// CRITICAL INVARIANTS:
/// 1. FulfillmentMode is decided at checkout based on ShippingOption.ProviderType
/// 2. FulfillmentMode NEVER changes after order creation
/// 3. FulfillmentMode NEVER inferred from ShippingOption after creation
/// 4. UI/API must rely ONLY on Order.FulfillmentMode (never ShippingOption)
/// 
/// ARCHITECTURAL RULES:
/// - Manual orders: Admin controls all fulfillment transitions via IOrderFulfillmentService
/// - Carrier orders: ONLY carrier webhooks can update fulfillment status
/// - Manual orders CANNOT accept carrier webhook updates
/// - Carrier orders CANNOT accept manual admin transitions
/// </summary>
public enum FulfillmentMode
{
    /// <summary>
    /// Manual fulfillment - controlled by admin via POS UI.
    /// 
    /// CHARACTERISTICS:
    /// - Admin can transition fulfillment states manually
    /// - No carrier integration required
    /// - Typical for: In-store pickup, local delivery, manual shipping
    /// - FulfillmentCarrierCode MUST be NULL
    /// 
    /// ALLOWED OPERATIONS:
    /// ✅ IOrderFulfillmentService.ConfirmAsync()
    /// ✅ IOrderFulfillmentService.PackAsync()
    /// ✅ IOrderFulfillmentService.MarkOutForDeliveryAsync()
    /// ✅ IOrderFulfillmentService.MarkDeliveredAsync()
    /// ✅ IOrderFulfillmentService.CancelAsync()
    /// ❌ Carrier webhook updates (REJECTED)
    /// </summary>
    Manual = 0,
    
    /// <summary>
    /// Carrier-managed fulfillment - controlled by carrier webhooks.
    /// 
    /// CHARACTERISTICS:
    /// - Fulfillment status updated ONLY via validated carrier webhooks
    /// - Admin CANNOT manually transition states
    /// - Typical for: Bosta, Aramex, DHL, Mylerz, etc.
    /// - FulfillmentCarrierCode MUST NOT be NULL
    /// 
    /// ALLOWED OPERATIONS:
    /// ✅ Carrier webhook updates (via CarrierWebhookHandler)
    /// ✅ Read-only admin view of status
    /// ❌ Manual admin transitions (REJECTED with 409 Conflict)
    /// ❌ IOrderFulfillmentService calls (REJECTED)
    /// 
    /// WEBHOOK VALIDATION:
    /// - Signature verification required
    /// - Idempotency enforced
    /// - Status regression prevented
    /// - TenantId isolation enforced
    /// </summary>
    Carrier = 1
}

/// <summary>
/// Extension methods for FulfillmentMode.
/// </summary>
public static class FulfillmentModeExtensions
{
    /// <summary>
    /// Returns true if this order allows manual admin fulfillment transitions.
    /// </summary>
    public static bool AllowsManualTransitions(this FulfillmentMode mode)
    {
        return mode == FulfillmentMode.Manual;
    }
    
    /// <summary>
    /// Returns true if this order accepts carrier webhook updates.
    /// </summary>
    public static bool AllowsCarrierWebhooks(this FulfillmentMode mode)
    {
        return mode == FulfillmentMode.Carrier;
    }
    
    /// <summary>
    /// Returns human-readable description for UI.
    /// </summary>
    public static string GetDescription(this FulfillmentMode mode)
    {
        return mode switch
        {
            FulfillmentMode.Manual => "Manual Fulfillment (Admin-Controlled)",
            FulfillmentMode.Carrier => "Carrier Fulfillment (Webhook-Controlled)",
            _ => mode.ToString()
        };
    }
    
    /// <summary>
    /// Returns UI hint for rendering fulfillment controls.
    /// </summary>
    public static string GetUiHint(this FulfillmentMode mode)
    {
        return mode switch
        {
            FulfillmentMode.Manual => "Show manual fulfillment action buttons",
            FulfillmentMode.Carrier => "Show read-only carrier tracking panel",
            _ => "Unknown"
        };
    }
}
