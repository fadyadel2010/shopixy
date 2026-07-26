using System.Text.RegularExpressions;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// AF-16B: Value object for payout wallet with normalization, validation, and masking.
/// Encapsulates wallet business rules and prepares for future encryption.
/// </summary>
public sealed record PayoutWallet
{
    public string Number { get; }
    public string Type { get; }

    private PayoutWallet(string number, string type)
    {
        Number = number;
        Type = type;
    }

    /// <summary>
    /// Creates a validated and normalized payout wallet.
    /// </summary>
    /// <param name="walletNumber">Raw wallet number from user input</param>
    /// <param name="walletType">Wallet type (VodafoneCash, Bank, etc.)</param>
    /// <returns>Validated PayoutWallet value object</returns>
    /// <exception cref="ArgumentException">If validation fails</exception>
    public static PayoutWallet Create(string walletNumber, string walletType)
    {
        if (string.IsNullOrWhiteSpace(walletNumber))
            throw new ArgumentException("Wallet number is required", nameof(walletNumber));
        if (string.IsNullOrWhiteSpace(walletType))
            throw new ArgumentException("Wallet type is required", nameof(walletType));

        var normalizedType = walletType.Trim();
        var normalizedNumber = NormalizeWalletNumber(walletNumber, normalizedType);

        ValidateWalletNumber(normalizedNumber, normalizedType);

        return new PayoutWallet(normalizedNumber, normalizedType);
    }

    /// <summary>
    /// Normalizes wallet number based on wallet type.
    /// For mobile wallets: strips non-digits and handles E.164 format.
    /// For others: trims whitespace.
    /// </summary>
    private static string NormalizeWalletNumber(string input, string walletType)
    {
        if (PayoutWalletTypes.IsMobileWallet(walletType))
        {
            return NormalizeMobileWallet(input);
        }

        return input.Trim();
    }

    /// <summary>
    /// Normalizes mobile wallet numbers to Egyptian 11-digit format.
    /// Accepts: 01012345678, +201012345678, 201012345678
    /// Returns: 01012345678
    /// </summary>
    private static string NormalizeMobileWallet(string input)
    {
        // Strip all non-digit characters
        var digits = Regex.Replace(input, @"\D", "");

        // Handle E.164 format: +20 1012345678 → 01012345678
        if (digits.StartsWith("20") && digits.Length == 12)
        {
            digits = "0" + digits.Substring(2);
        }

        return digits;
    }

    /// <summary>
    /// Validates wallet number based on wallet type.
    /// Mobile wallets: must be exactly 11 digits.
    /// Bank/Other: 6-32 alphanumeric characters.
    /// </summary>
    private static void ValidateWalletNumber(string normalizedNumber, string walletType)
    {
        if (PayoutWalletTypes.IsMobileWallet(walletType))
        {
            // Mobile wallet: must be exactly 11 digits
            if (!Regex.IsMatch(normalizedNumber, @"^\d{11}$"))
            {
                throw new ArgumentException(
                    $"Mobile wallet ({walletType}) must be exactly 11 digits. " +
                    $"Accepted formats: 01012345678, +201012345678, or 201012345678",
                    nameof(normalizedNumber));
            }
        }
        else
        {
            // Bank/Other: 6-32 alphanumeric
            if (normalizedNumber.Length < 6 || normalizedNumber.Length > 32)
            {
                throw new ArgumentException(
                    $"Wallet number must be 6-32 characters for {walletType}",
                    nameof(normalizedNumber));
            }

            if (!Regex.IsMatch(normalizedNumber, @"^[a-zA-Z0-9]+$"))
            {
                throw new ArgumentException(
                    "Wallet number must contain only letters and digits",
                    nameof(normalizedNumber));
            }
        }
    }

    /// <summary>
    /// Masks the wallet number for display in logs and audit trails.
    /// Example: 01012345678 → 010*****678
    /// </summary>
    public string GetMasked()
    {
        return MaskWalletNumber(Number);
    }

    /// <summary>
    /// Static helper to mask any wallet number.
    /// Used for audit logs and affiliate profile responses.
    /// </summary>
    public static string MaskWalletNumber(string walletNumber)
    {
        if (string.IsNullOrEmpty(walletNumber) || walletNumber.Length <= 6)
            return "***";

        var first3 = walletNumber.Substring(0, 3);
        var last3 = walletNumber.Substring(walletNumber.Length - 3);
        return $"{first3}*****{last3}";
    }
}
