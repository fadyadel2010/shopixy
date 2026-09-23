namespace PointOfSale.Core.Security;

/// <summary>
/// Single shared POS staff password policy for invitation acceptance and
/// administrator-initiated password-reset completion.
/// Blueprint §24 delegates this definition to the implementation: POS has no
/// shared policy and adjacent identities require at least 8 characters, which
/// matches tenant registration (MinLength 8). This policy adopts that bar:
/// 8-128 characters. It is intentionally separate from Affiliate/Storefront
/// complexity rules; those authorities are untouched.
/// </summary>
public static class PosPasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static bool IsValid(string? password, out string errorCode)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            errorCode = "PASSWORD_REQUIRED";
            return false;
        }

        if (password.Length < MinLength || password.Length > MaxLength)
        {
            errorCode = "WEAK_PASSWORD";
            return false;
        }

        errorCode = string.Empty;
        return true;
    }
}
