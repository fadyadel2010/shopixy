using PointOfSale.Core.Interfaces.Affiliate;

namespace PointOfSale.Infrastructure.Services.Affiliate;

/// <summary>
/// AF-16B: Pass-through implementation of IPayoutWalletProtector.
/// 
/// NO ENCRYPTION: Returns plain text unchanged.
/// This is a placeholder for future AF-17 encryption implementation.
/// 
/// When AF-17 is implemented, replace this with:
/// - AesPayoutWalletProtector (AES-256-GCM)
/// - DataProtectionPayoutWalletProtector (ASP.NET Core Data Protection)
/// </summary>
public class PassThroughPayoutWalletProtector : IPayoutWalletProtector
{
    public string Protect(string plainText)
    {
        // AF-16B: No encryption yet - return as-is
        // AF-17: Implement AES encryption here
        return plainText;
    }

    public string Unprotect(string cipherText)
    {
        // AF-16B: No decryption yet - return as-is
        // AF-17: Implement AES decryption here
        return cipherText;
    }
}
