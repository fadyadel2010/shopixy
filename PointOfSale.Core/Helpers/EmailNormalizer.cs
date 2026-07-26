namespace PointOfSale.Core.Helpers;

/// <summary>
/// Provides culture-safe email normalization.
/// 
/// CRITICAL: All email comparisons and storage MUST use this helper.
/// 
/// Rules:
/// 1. Trim whitespace
/// 2. Convert to lowercase using invariant culture
/// 3. Handle null/empty gracefully
/// 
/// Phase: 10.3.1 Hardening - Email Normalization
/// </summary>
public static class EmailNormalizer
{
    /// <summary>
    /// Normalizes an email address for storage and comparison.
    /// </summary>
    /// <param name="email">The email address to normalize.</param>
    /// <returns>Normalized email (trimmed, lowercase invariant) or empty string if null/whitespace.</returns>
    public static string Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        // CRITICAL: Use ToLowerInvariant() for culture-safe normalization
        // ToLower() can produce different results based on current culture
        return email.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Checks if two email addresses are equal after normalization.
    /// </summary>
    public static bool AreEqual(string? email1, string? email2)
    {
        return Normalize(email1) == Normalize(email2);
    }

    /// <summary>
    /// Validates and normalizes an email for storage.
    /// Throws if email is null or whitespace.
    /// </summary>
    /// <param name="email">The email to validate and normalize.</param>
    /// <returns>Normalized email.</returns>
    /// <exception cref="ArgumentException">Thrown when email is null or whitespace.</exception>
    public static string NormalizeRequired(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or whitespace.", nameof(email));

        return Normalize(email);
    }
}
