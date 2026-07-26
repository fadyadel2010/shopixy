namespace PointOfSale.Core.DTOs;

/// <summary>
/// Request model for updating a checkout session with partial data.
/// Used for progressive autosave as customer fills the checkout form.
/// All fields are nullable to support incremental updates.
/// </summary>
public class UpdateCheckoutSessionRequest
{
    /// <summary>
    /// The checkout session ID to update.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// The tenant ID that owns this session.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Customer's full name.
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// Customer's phone number.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Customer's email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Delivery city.
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Delivery area/district.
    /// </summary>
    public string? Area { get; set; }

    /// <summary>
    /// Delivery street address.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Delivery country.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Additional notes or delivery instructions.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Selected shipping method ID.
    /// </summary>
    public Guid? ShippingMethodId { get; set; }

    /// <summary>
    /// Selected payment method code (e.g., "cash", "card", "paymob").
    /// </summary>
    public string? PaymentMethodCode { get; set; }

    /// <summary>
    /// Selected wallet type for manual wallet checkout.
    /// </summary>
    public string? WalletType { get; set; }

    /// <summary>
    /// Customer wallet phone number for manual wallet checkout.
    /// </summary>
    public string? WalletPhoneNumber { get; set; }

    /// <summary>
    /// JSON snapshot of cart items at the time of update.
    /// </summary>
    public string? ItemsSnapshotJson { get; set; }

    /// <summary>
    /// Total cart value.
    /// </summary>
    public decimal? CartTotal { get; set; }

    /// <summary>
    /// Currency code (e.g., "EGP", "USD").
    /// </summary>
    public string? Currency { get; set; }
}
