using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.API.Security;

/// <summary>
/// Provides security-related utility methods for cryptographic operations.
/// </summary>
public static class SecurityUtilities
{
    /// <summary>
    /// Performs a constant-time string comparison to prevent timing side-channel attacks.
    /// </summary>
    /// <param name="left">The first string to compare.</param>
    /// <param name="right">The second string to compare.</param>
    /// <returns>
    /// <c>true</c> if both strings are non-empty and equal; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This method uses <see cref="CryptographicOperations.FixedTimeEquals"/> to ensure
    /// that the comparison time does not depend on the content of the strings, preventing
    /// timing attacks that could leak information about secret values.
    /// </remarks>
    public static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
