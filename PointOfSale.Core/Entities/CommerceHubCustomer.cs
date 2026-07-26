namespace PointOfSale.Core.Entities;

/// <summary>
/// Global CommerceHub customer account.
/// Separate from POS customers and tenant-scoped storefront online customers.
/// </summary>
public class CommerceHubCustomer : BaseEntity
{
    public string Email { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public DateTime? TokensValidAfterUtc { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public DateTime? EmailVerifiedAtUtc { get; private set; }

    public ICollection<CommerceHubMarketplaceOrder> Orders { get; set; } = new List<CommerceHubMarketplaceOrder>();
    public ICollection<CustomerPayoutDetails> PayoutDetails { get; set; } = new List<CustomerPayoutDetails>();

    private CommerceHubCustomer()
    {
    }

    public static CommerceHubCustomer Create(string email, string name, string? phoneNumber, string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        return new CommerceHubCustomer
        {
            Id = Guid.NewGuid(),
            Email = NormalizeEmail(email),
            Name = name.Trim(),
            PhoneNumber = NormalizePhone(phoneNumber),
            PasswordHash = string.IsNullOrWhiteSpace(passwordHash) ? null : passwordHash,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateProfile(string name, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        Name = name.Trim();
        PhoneNumber = NormalizePhone(phoneNumber);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        Email = NormalizeEmail(email);
        UpdatedAt = DateTime.UtcNow;
    }

    public void RevokeAllSessions(DateTime cutoffUtc)
    {
        var normalizedCutoffUtc = cutoffUtc.Kind switch
        {
            DateTimeKind.Utc => cutoffUtc,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Utc),
            DateTimeKind.Local => cutoffUtc.ToUniversalTime(),
            _ => cutoffUtc.ToUniversalTime()
        };

        if (!TokensValidAfterUtc.HasValue || normalizedCutoffUtc > TokensValidAfterUtc.Value)
        {
            TokensValidAfterUtc = normalizedCutoffUtc;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void MarkEmailVerified()
    {
        IsEmailVerified = true;
        EmailVerifiedAtUtc = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkEmailUnverified()
    {
        IsEmailVerified = false;
        EmailVerifiedAtUtc = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? phoneNumber) =>
        string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
}
