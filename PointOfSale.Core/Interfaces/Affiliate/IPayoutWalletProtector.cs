namespace PointOfSale.Core.Interfaces.Affiliate;

/// <summary>
/// AF-16B: Abstraction for future encryption-at-rest of payout wallet numbers.
/// 
/// CURRENT IMPLEMENTATION: Pass-through (no encryption).
/// FUTURE (AF-17): AES encryption or ASP.NET Core Data Protection.
/// 
/// This interface allows switching to encryption without refactoring domain entities.
/// </summary>
public interface IPayoutWalletProtector
{
    /// <summary>
    /// Protects (encrypts) a plain-text wallet number before storage.
    /// Current implementation: returns plainText unchanged.
    /// </summary>
    string Protect(string plainText);

    /// <summary>
    /// Unprotects (decrypts) a cipher-text wallet number after retrieval.
    /// Current implementation: returns cipherText unchanged.
    /// </summary>
    string Unprotect(string cipherText);
}
