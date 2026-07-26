namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// AF-16B: Payout wallet type constants for type-aware validation.
/// </summary>
public static class PayoutWalletTypes
{
    public const string VodafoneCash = "VodafoneCash";
    public const string OrangeMoney = "OrangeMoney";
    public const string EtisalatCash = "EtisalatCash";
    public const string InstaPay = "InstaPay";
    public const string Bank = "Bank";
    public const string Other = "Other";

    /// <summary>
    /// Mobile wallet types that require 11-digit Egyptian mobile format.
    /// </summary>
    public static readonly HashSet<string> MobileWalletTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        VodafoneCash,
        OrangeMoney,
        EtisalatCash,
        InstaPay
    };

    /// <summary>
    /// Checks if the wallet type is a mobile wallet.
    /// </summary>
    public static bool IsMobileWallet(string walletType)
    {
        return MobileWalletTypes.Contains(walletType);
    }
}
