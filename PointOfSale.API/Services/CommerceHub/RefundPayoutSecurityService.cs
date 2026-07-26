using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public interface IRefundPayoutSecurityService
{
    string Encrypt(string plainText);
    string Decrypt(string? encryptedOrPlainText);
    bool IsEncrypted(string? value);
}

public sealed class RefundPayoutSecurityService : IRefundPayoutSecurityService
{
    private const string ProtectorPurpose = "CommerceHub.CustomerRefundPayout.Secrets";
    private const string EncryptedPrefix = "ENC:";

    private readonly IDataProtector _protector;
    private readonly ILogger<RefundPayoutSecurityService> _logger;

    public RefundPayoutSecurityService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<RefundPayoutSecurityService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        if (IsEncrypted(plainText))
        {
            return plainText;
        }

        try
        {
            return $"{EncryptedPrefix}{_protector.Protect(plainText)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt refund payout detail.");
            throw new InvalidOperationException("Refund payout encryption failed.", ex);
        }
    }

    public string Decrypt(string? encryptedOrPlainText)
    {
        if (string.IsNullOrWhiteSpace(encryptedOrPlainText))
        {
            return string.Empty;
        }

        if (!IsEncrypted(encryptedOrPlainText))
        {
            return encryptedOrPlainText.Trim();
        }

        try
        {
            return _protector.Unprotect(encryptedOrPlainText[EncryptedPrefix.Length..]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt refund payout detail.");
            return string.Empty;
        }
    }

    public bool IsEncrypted(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
}

public sealed class ResolvedRefundPayoutDetails
{
    public Guid? SavedPayoutDetailsId { get; init; }
    public RefundPayoutMethod Method { get; init; }
    public string AccountIdentifier { get; init; } = string.Empty;
    public string EncryptedAccountIdentifier { get; init; } = string.Empty;
    public string MaskedAccountIdentifier { get; init; } = string.Empty;
    public string? AccountHolderName { get; init; }
    public bool IsVerified { get; init; }
}

internal sealed record NormalizedRefundPayoutInput(
    RefundPayoutMethod Method,
    string AccountIdentifier,
    string MaskedAccountIdentifier,
    string? AccountHolderName);

internal static class RefundPayoutSupport
{
    public const int MaxEncryptedAccountIdentifierLength = 500;
    public const int MaxMaskedAccountIdentifierLength = 32;
    public const int MaxAccountHolderNameLength = 200;

    private const int MaxPlainAccountIdentifierLength = 120;

    private static readonly Regex EgyptianMobileCharactersPattern = new(
        @"^[\d\s()+-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EgyptianMobilePattern = new(
        @"^01[0125]\d{8}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InstaPayHandlePattern = new(
        @"^[a-zA-Z][a-zA-Z0-9._-]{2,49}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IbanPattern = new(
        @"^[A-Z]{2}\d{2}[A-Z0-9]{10,30}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BankAccountPattern = new(
        @"^\d{10,34}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static NormalizedRefundPayoutInput NormalizeRequired(
        RefundPayoutMethod? method,
        string? accountIdentifier,
        string? accountHolderName)
    {
        if (!method.HasValue)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_METHOD_REQUIRED",
                "Please choose how you want to receive the refund.",
                StatusCodes.Status400BadRequest);
        }

        if (!Enum.IsDefined(method.Value))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_METHOD_INVALID",
                "Refund payout method is invalid.",
                StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(accountIdentifier))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_ACCOUNT_REQUIRED",
                "Refund payout account details are required.",
                StatusCodes.Status400BadRequest);
        }

        var normalizedAccount = method.Value switch
        {
            RefundPayoutMethod.VodafoneCash => NormalizeEgyptianMobile(
                accountIdentifier,
                "REFUND_PAYOUT_VODAFONE_INVALID",
                "Vodafone Cash requires a valid Egyptian mobile number."),
            RefundPayoutMethod.InstaPay => NormalizeInstaPay(accountIdentifier),
            RefundPayoutMethod.BankTransfer => NormalizeBankTransfer(accountIdentifier),
            _ => throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_METHOD_INVALID",
                "Refund payout method is invalid.",
                StatusCodes.Status400BadRequest)
        };

        if (normalizedAccount.Length > MaxPlainAccountIdentifierLength)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_ACCOUNT_TOO_LONG",
                "Refund payout account is too long.",
                StatusCodes.Status400BadRequest);
        }

        return new NormalizedRefundPayoutInput(
            method.Value,
            normalizedAccount,
            MaskAccountIdentifier(normalizedAccount),
            NormalizeAccountHolderName(accountHolderName));
    }

    public static string? NormalizeAccountHolderName(string? accountHolderName)
    {
        if (string.IsNullOrWhiteSpace(accountHolderName))
        {
            return null;
        }

        var normalized = accountHolderName.Trim();
        if (normalized.Length > MaxAccountHolderNameLength)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_ACCOUNT_HOLDER_TOO_LONG",
                $"Account holder name cannot exceed {MaxAccountHolderNameLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    public static string MaskAccountIdentifier(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier))
        {
            return "****";
        }

        var normalized = accountIdentifier.Trim();
        if (normalized.Length <= 4)
        {
            return "****";
        }

        if (normalized.Length <= 6)
        {
            return $"{normalized[..1]}****{normalized[^1..]}";
        }

        return $"{normalized[..3]}****{normalized[^3..]}";
    }

    public static bool SnapshotMatches(
        CommerceHubRefundRequest refundRequest,
        ResolvedRefundPayoutDetails payoutDetails,
        IRefundPayoutSecurityService securityService)
    {
        if (refundRequest.PayoutMethodSnapshot != payoutDetails.Method)
        {
            return false;
        }

        if (!string.Equals(
                refundRequest.PayoutAccountMaskedSnapshot ?? string.Empty,
                payoutDetails.MaskedAccountIdentifier,
                StringComparison.Ordinal))
        {
            return false;
        }

        var existingAccount = securityService.Decrypt(refundRequest.PayoutAccountSnapshot);
        return string.Equals(existingAccount, payoutDetails.AccountIdentifier, StringComparison.Ordinal) &&
               string.Equals(
                   refundRequest.PayoutAccountHolderNameSnapshot ?? string.Empty,
                   payoutDetails.AccountHolderName ?? string.Empty,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEgyptianMobile(string input, string errorCode, string errorMessage)
    {
        var digits = Regex.Replace(input, @"\D", string.Empty);

        if (digits.StartsWith("0020", StringComparison.Ordinal) && digits.Length == 14)
        {
            digits = $"0{digits[4..]}";
        }
        else if (digits.StartsWith("20", StringComparison.Ordinal) && digits.Length == 12)
        {
            digits = $"0{digits[2..]}";
        }
        else if (digits.StartsWith("1", StringComparison.Ordinal) && digits.Length == 10)
        {
            digits = $"0{digits}";
        }

        if (!EgyptianMobilePattern.IsMatch(digits))
        {
            throw new CommerceHubCheckoutException(
                errorCode,
                errorMessage,
                StatusCodes.Status400BadRequest);
        }

        return digits;
    }

    private static string NormalizeInstaPay(string input)
    {
        var trimmed = input.Trim();
        if (EgyptianMobileCharactersPattern.IsMatch(trimmed))
        {
            return NormalizeEgyptianMobile(
                trimmed,
                "REFUND_PAYOUT_INSTAPAY_INVALID",
                "InstaPay must be a valid Egyptian mobile number or a supported handle.");
        }

        var normalized = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized.StartsWith('@'))
        {
            normalized = normalized[1..];
        }

        normalized = normalized.ToLowerInvariant();
        if (!InstaPayHandlePattern.IsMatch(normalized))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_INSTAPAY_INVALID",
                "InstaPay must be a valid Egyptian mobile number or a supported handle.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string NormalizeBankTransfer(string input)
    {
        var normalized = Regex.Replace(input.Trim(), @"[\s-]+", string.Empty)
            .ToUpperInvariant();

        if (IbanPattern.IsMatch(normalized) || BankAccountPattern.IsMatch(normalized))
        {
            return normalized;
        }

        throw new CommerceHubCheckoutException(
            "REFUND_PAYOUT_BANK_INVALID",
            "Bank transfer details must be a valid IBAN or account number.",
            StatusCodes.Status400BadRequest);
    }
}
