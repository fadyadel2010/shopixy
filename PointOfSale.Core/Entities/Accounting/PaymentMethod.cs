using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// Payment method configuration that maps to ledger accounts.
/// 
/// Each payment method defines:
/// - TargetAccountId: Where immediate funds go (e.g., Cashbox for CASH)
/// - ClearingAccountId: Where pending funds go (e.g., Visa Clearing for CARD)
/// - FeesAccountId: Where fees are expensed (e.g., Gateway Fees for STRIPE)
/// 
/// INVARIANTS:
/// - Code is unique per tenant
/// - Either TargetAccountId or ClearingAccountId must be set
/// - System payment methods cannot be deleted
/// </summary>
public class PaymentMethod : BaseEntity, ITenantOwned
{
    /// <summary>
    /// Tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Unique code for this payment method.
    /// Examples: CASH, CARD, STRIPE, COD, WALLET.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name (English).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name (Arabic).
    /// </summary>
    [MaxLength(100)]
    public string? NameAr { get; set; }
    
    /// <summary>
    /// For immediate settlement methods (CASH):
    /// The account that receives funds immediately.
    /// </summary>
    public Guid? TargetAccountId { get; set; }
    public Account? TargetAccount { get; set; }
    
    /// <summary>
    /// For deferred settlement methods (CARD, STRIPE, COD):
    /// The clearing account that holds funds pending settlement.
    /// </summary>
    public Guid? ClearingAccountId { get; set; }
    public Account? ClearingAccount { get; set; }
    
    /// <summary>
    /// Account for payment processing fees.
    /// Used for STRIPE, WALLET, etc.
    /// </summary>
    public Guid? FeesAccountId { get; set; }
    public Account? FeesAccount { get; set; }
    
    /// <summary>
    /// If true, this is a system-created payment method.
    /// System methods cannot be deleted.
    /// </summary>
    public bool IsSystem { get; set; }
    
    /// <summary>
    /// If false, payment method is disabled.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Display order in UI.
    /// </summary>
    public int DisplayOrder { get; set; }
    
    /// <summary>
    /// Returns true if this method uses a clearing account.
    /// </summary>
    public bool UsesClearingAccount => ClearingAccountId.HasValue;
    
    /// <summary>
    /// Returns true if this method has associated fees.
    /// </summary>
    public bool HasFees => FeesAccountId.HasValue;
    
    /// <summary>
    /// Gets the primary account for this payment method.
    /// Returns TargetAccount if set, otherwise ClearingAccount.
    /// </summary>
    public Guid GetPrimaryAccountId()
    {
        return TargetAccountId ?? ClearingAccountId 
            ?? throw new InvalidOperationException("Payment method must have either TargetAccountId or ClearingAccountId");
    }
    
    #region Factory Methods
    
    /// <summary>
    /// Creates a payment method with immediate settlement (e.g., CASH).
    /// </summary>
    public static PaymentMethod CreateImmediate(
        Guid tenantId,
        string code,
        string name,
        Guid targetAccountId,
        bool isSystem = false)
    {
        return new PaymentMethod
        {
            TenantId = tenantId,
            Code = code.ToUpperInvariant(),
            Name = name,
            TargetAccountId = targetAccountId,
            ClearingAccountId = null,
            FeesAccountId = null,
            IsSystem = isSystem,
            IsActive = true
        };
    }
    
    /// <summary>
    /// Creates a payment method with deferred settlement (e.g., CARD, STRIPE).
    /// </summary>
    public static PaymentMethod CreateDeferred(
        Guid tenantId,
        string code,
        string name,
        Guid clearingAccountId,
        Guid? feesAccountId = null,
        bool isSystem = false)
    {
        return new PaymentMethod
        {
            TenantId = tenantId,
            Code = code.ToUpperInvariant(),
            Name = name,
            TargetAccountId = null,
            ClearingAccountId = clearingAccountId,
            FeesAccountId = feesAccountId,
            IsSystem = isSystem,
            IsActive = true
        };
    }
    
    #endregion
}

/// <summary>
/// Well-known payment method codes.
/// </summary>
public static class PaymentMethodCodes
{
    public const string Cash = "CASH";
    public const string Card = "CARD";
    public const string Stripe = "STRIPE";
    public const string Cod = "COD";
    public const string Wallet = "WALLET";
    public const string BankTransfer = "BANK_TRANSFER";
}
