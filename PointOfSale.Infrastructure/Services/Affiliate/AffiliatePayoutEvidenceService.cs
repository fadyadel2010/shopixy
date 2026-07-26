using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services.Affiliate;

public sealed class AffiliatePayoutEvidenceService : IAffiliatePayoutEvidenceService
{
    public const string TrustedVerificationSource = "HMAC_RECONCILIATION";
    private readonly ApplicationDbContext _db;
    private readonly AffiliateSettlementOptions _options;
    private readonly TimeProvider _timeProvider;

    public AffiliatePayoutEvidenceService(
        ApplicationDbContext db,
        IOptions<AffiliateSettlementOptions> options,
        TimeProvider timeProvider)
    {
        _db = db;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<AffiliatePayoutTransferEvidence> RegisterVerifiedEvidenceAsync(
        Guid storeTenantId,
        string railNamespace,
        string paymentReference,
        decimal amount,
        string currency,
        DateTimeOffset verifiedAtUtc,
        string signature,
        CancellationToken ct = default)
    {
        if (storeTenantId == Guid.Empty)
            throw new ArgumentException("StoreTenantId is required.", nameof(storeTenantId));
        if (amount <= 0m)
            throw new ArgumentException("Evidence amount must be positive.", nameof(amount));

        var normalizedRail = AffiliatePayoutTransferEvidence.NormalizeRailNamespace(railNamespace);
        var normalizedReference = AffiliatePayoutTransferEvidence.NormalizePaymentReference(paymentReference);
        var normalizedCurrency = NormalizeCurrency(currency);
        EnsureFreshTimestamp(verifiedAtUtc);

        var canonicalPayload = BuildCanonicalPayload(
            storeTenantId,
            normalizedRail,
            normalizedReference,
            amount,
            normalizedCurrency,
            verifiedAtUtc);
        var fingerprint = VerifySignature(canonicalPayload, signature);

        var tenantCurrency = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == storeTenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(ct);

        if (!string.Equals(NormalizeCurrency(tenantCurrency), normalizedCurrency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EVIDENCE_CURRENCY_CONFLICT: Verified evidence currency must match the store base currency.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var existing = await _db.AffiliatePayoutTransferEvidence
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.RailNamespace == normalizedRail &&
                        item.NormalizedPaymentReference == normalizedReference,
                ct);

        if (existing != null)
        {
            if (existing.StoreTenantId == storeTenantId &&
                existing.ExpectedAmount == amount &&
                string.Equals(existing.Currency, normalizedCurrency, StringComparison.Ordinal) &&
                string.Equals(existing.VerificationFingerprint, fingerprint, StringComparison.Ordinal))
            {
                await tx.CommitAsync(ct);
                return existing;
            }

            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EVIDENCE_REPLAY_CONFLICT: This rail/reference already has different verified authority fields.");
        }

        var evidence = AffiliatePayoutTransferEvidence.Create(
            storeTenantId,
            normalizedRail,
            paymentReference,
            amount,
            normalizedCurrency,
            verifiedAtUtc.UtcDateTime,
            TrustedVerificationSource,
            fingerprint);

        _db.AffiliatePayoutTransferEvidence.Add(evidence);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return evidence;
    }

    public static string BuildCanonicalPayload(
        Guid storeTenantId,
        string normalizedRailNamespace,
        string normalizedPaymentReference,
        decimal amount,
        string normalizedCurrency,
        DateTimeOffset verifiedAtUtc) =>
        string.Join(
            '\n',
            storeTenantId.ToString("N"),
            normalizedRailNamespace,
            normalizedPaymentReference,
            amount.ToString("0.####", CultureInfo.InvariantCulture),
            normalizedCurrency,
            verifiedAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    private void EnsureFreshTimestamp(DateTimeOffset verifiedAtUtc)
    {
        var now = _timeProvider.GetUtcNow();
        var maxAge = TimeSpan.FromMinutes(_options.ExternalEvidenceMaxAgeMinutes);
        if (verifiedAtUtc < now - maxAge || verifiedAtUtc > now.AddMinutes(1))
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EVIDENCE_EXPIRED: Reconciliation evidence timestamp is outside the accepted verification window.");
        }
    }

    private string VerifySignature(string canonicalPayload, string signature)
    {
        if (string.IsNullOrWhiteSpace(_options.ExternalEvidenceHmacSecret) ||
            _options.ExternalEvidenceHmacSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EVIDENCE_VERIFIER_NOT_CONFIGURED: Trusted reconciliation evidence cannot be accepted.");
        }

        byte[] suppliedSignature;
        try
        {
            suppliedSignature = Convert.FromHexString(signature?.Trim() ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EVIDENCE_SIGNATURE_INVALID: Evidence signature must be a SHA-256 HMAC hex value.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ExternalEvidenceHmacSecret));
        var expectedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload));
        if (suppliedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EVIDENCE_SIGNATURE_INVALID: Evidence signature verification failed.");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
    }

    private static string NormalizeCurrency(string? currency)
    {
        var normalized = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 3)
            throw new ArgumentException("Currency must be an ISO 4217 code.", nameof(currency));
        return normalized;
    }
}
