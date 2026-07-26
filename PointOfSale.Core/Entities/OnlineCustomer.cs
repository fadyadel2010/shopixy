using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Authentication provider for online customer accounts.
/// </summary>
public enum AuthProvider
{
    /// <summary>
    /// Customer registered with email/password.
    /// </summary>
    Local = 0,

    /// <summary>
    /// Customer signed in via Google OAuth.
    /// </summary>
    Google = 1,

    /// <summary>
    /// Customer signed in via Facebook OAuth.
    /// </summary>
    Facebook = 2
}

/// <summary>
/// Online Customer entity for Storefront authentication.
/// 
/// ARCHITECTURAL RULES:
/// - This is COMPLETELY SEPARATE from POS Customer entity
/// - NO shared tables, NO shared authentication, NO shared JWT
/// - Managed in PostgreSQL (Write Model - Source of Truth)
/// - Projected to MongoDB (Read Model) for Storefront
/// - TenantId is MANDATORY - no cross-tenant access
/// 
/// SOFT DELETE POLICY:
/// - OnlineCustomers are NEVER hard-deleted
/// - Deactivation sets IsActive = false
/// - Data is retained for legal/audit/order-history purposes
/// - There is NO Delete() method or endpoint intentionally
/// 
/// EMAIL NORMALIZATION:
/// - Emails MUST be normalized before storage (Trim + ToLowerInvariant)
/// - Use EmailNormalizer.Normalize() for all email operations
/// - Unique constraint on (TenantId, Email) enforces tenant-scoped uniqueness
/// 
/// TIMESTAMP OWNERSHIP:
/// - CreatedAtUtc: Set once on registration (PostgreSQL)
/// - UpdatedAtUtc: Set on any update (PostgreSQL)
/// - LastLoginAtUtc: Set ONLY by login service (PostgreSQL)
/// - MongoDB mirrors these values - NEVER computes them
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// Phase: 10.3.1 Hardening - Correctness & Security
/// </summary>
public class OnlineCustomer : ITenantOwned
{
    /// <summary>
    /// Unique identifier for the online customer.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant that owns this customer account.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// All online customers MUST belong to exactly one tenant.
    /// Guid.Empty is rejected at all layers.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Customer email address - unique per tenant.
    /// Used as the primary login identifier for Local auth.
    /// 
    /// NORMALIZATION RULES:
    /// - MUST be normalized using EmailNormalizer.Normalize() before storage
    /// - This means: Trim() + ToLowerInvariant()
    /// - Database has unique index on (TenantId, Email)
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Normalized email for efficient case-insensitive lookups.
    /// Computed as EmailNormalizer.Normalize(Email).
    /// </summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>
    /// Hashed password for Local authentication.
    /// NULL for social login providers (Google, Facebook).
    /// NEVER store plain text passwords.
    /// NEVER expose in events, logs, or APIs.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Authentication provider used by this customer.
    /// </summary>
    public AuthProvider Provider { get; set; } = AuthProvider.Local;

    /// <summary>
    /// External user ID from the social login provider.
    /// NULL for Local authentication.
    /// NEVER expose in events, logs, or APIs (security risk).
    /// </summary>
    public string? ProviderUserId { get; set; }

    /// <summary>
    /// Customer's display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Customer's phone number (optional).
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Whether the customer account is active.
    /// 
    /// SECURITY RULES:
    /// - IsActive = false means customer CANNOT log in
    /// - Login attempts MUST check this flag
    /// - JWT issuance MUST be blocked for inactive accounts
    /// - Existing JWTs should be rejected (Phase 10.3.2)
    /// 
    /// This is the ONLY way to "delete" a customer (soft delete).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether the customer's email has been verified.
    /// Required for certain actions (checkout, etc).
    /// </summary>
    public bool EmailVerified { get; set; } = false;

    /// <summary>
    /// When the email was verified.
    /// NULL if not verified.
    /// </summary>
    public DateTime? EmailVerifiedAtUtc { get; set; }

    /// <summary>
    /// Whether the customer's phone has been verified via WhatsApp OTP.
    /// </summary>
    public bool PhoneVerified { get; set; } = false;

    /// <summary>
    /// When the phone was verified.
    /// NULL if not verified.
    /// </summary>
    public DateTime? PhoneVerifiedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the account was created.
    /// Set once on registration. Never modified.
    /// PostgreSQL is the source of truth.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the last successful login.
    /// NULL if never logged in.
    /// 
    /// OWNERSHIP RULES:
    /// - ONLY set by login service in PostgreSQL
    /// - NEVER computed by consumers or MongoDB
    /// - Event carries this value FROM PostgreSQL
    /// - MongoDB mirrors the value idempotently
    /// </summary>
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp of the last update.
    /// Set on any modification to the record.
    /// PostgreSQL is the source of truth.
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// Durable cutoff timestamp for token validation.
    /// Any access token issued before this timestamp MUST be rejected.
    /// 
    /// SECURITY CRITICAL:
    /// - Set when password is reset or changed
    /// - Set when all tokens are revoked for security reasons
    /// - JWT validation MUST check token.iat against this value
    /// - This provides a durable revocation mechanism that works even when
    ///   the blacklist store (Redis/PostgreSQL) is unavailable
    /// 
    /// NULL means no cutoff is enforced (all tokens valid based on expiry only).
    /// </summary>
    public DateTime? TokensValidAfterUtc { get; set; }
}
