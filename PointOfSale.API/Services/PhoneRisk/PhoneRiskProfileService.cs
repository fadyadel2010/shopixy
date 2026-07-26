using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.PhoneRisk;

public class PhoneRiskProfileService : IPhoneRiskProfileService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly ILogger<PhoneRiskProfileService> _logger;

    public PhoneRiskProfileService(
        ApplicationDbContext dbContext,
        IPhoneNumberNormalizer phoneNumberNormalizer,
        ILogger<PhoneRiskProfileService> logger)
    {
        _dbContext = dbContext;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _logger = logger;
    }

    public async Task<string> NormalizePhoneAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("Phone number is required.", nameof(phoneNumber));
        }

        var cleaned = CleanPhone(phoneNumber);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ArgumentException("Phone number is required.", nameof(phoneNumber));
        }

        try
        {
            return await _phoneNumberNormalizer.NormalizeAsync(cleaned, tenantId, cancellationToken);
        }
        catch (PhoneNumberNormalizationException ex)
        {
            var fallback = FallbackNormalize(cleaned);

            _logger.LogDebug(
                ex,
                "Falling back to separator-stripped phone normalization | TenantId: {TenantId} | Phone: {Phone} | Fallback: {Fallback}",
                tenantId,
                MaskPhone(cleaned),
                MaskPhone(fallback));

            return fallback;
        }
    }

    public async Task<AdminPhoneRiskStatusDto?> GetStatusAsync(Guid tenantId, string? phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var normalizedPhone = await NormalizePhoneAsync(tenantId, phoneNumber, cancellationToken);

        var profile = await _dbContext.PhoneRiskProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PhoneNumber == normalizedPhone, cancellationToken);

        if (profile == null)
        {
            return null;
        }

        var blockedByUser = await ResolveBlockedByUserNameAsync(profile.BlockedByUserId, cancellationToken);
        var globalSpamReports = await GetGlobalSpamReportsAsync(tenantId, normalizedPhone, cancellationToken);
        return MapStatus(profile, blockedByUser, globalSpamReports);
    }

    public async Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var normalizedPhone = await NormalizePhoneAsync(tenantId, phoneNumber, cancellationToken);

        return await _dbContext.PhoneRiskProfiles
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.PhoneNumber == normalizedPhone && x.IsBlocked, cancellationToken);
    }

    public async Task<int> GetGlobalSpamReportsAsync(Guid tenantId, string? phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return 0;
        }

        var normalizedPhone = await NormalizePhoneAsync(tenantId, phoneNumber, cancellationToken);

        return await _dbContext.GlobalPhoneReputations
            .AsNoTracking()
            .Where(x => x.PhoneNumber == normalizedPhone)
            .Select(x => x.SpamReports)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> HasReportedSpamAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.PhoneSpamReports
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.OrderId == orderId, cancellationToken);
    }

    public async Task<AdminPhoneRiskStatusDto> BlockAsync(
        Guid tenantId,
        string phoneNumber,
        string? reason,
        Guid? blockedByUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhone = await NormalizePhoneAsync(tenantId, phoneNumber, cancellationToken);
        var utcNow = DateTime.UtcNow;

        var profile = await _dbContext.PhoneRiskProfiles
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PhoneNumber == normalizedPhone, cancellationToken);

        if (profile == null)
        {
            profile = new PhoneRiskProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PhoneNumber = normalizedPhone,
                CreatedAtUtc = utcNow,
            };

            _dbContext.PhoneRiskProfiles.Add(profile);
        }

        profile.IsBlocked = true;
        profile.BlockedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        profile.BlockedAtUtc = utcNow;
        profile.UpdatedAtUtc = utcNow;

        if (blockedByUserId.HasValue)
        {
            profile.BlockedByUserId = blockedByUserId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var blockedByUser = await ResolveBlockedByUserNameAsync(profile.BlockedByUserId, cancellationToken);
        var globalSpamReports = await GetGlobalSpamReportsAsync(tenantId, normalizedPhone, cancellationToken);
        return MapStatus(profile, blockedByUser, globalSpamReports);
    }

    public async Task<AdminPhoneRiskStatusDto> UnblockAsync(
        Guid tenantId,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhone = await NormalizePhoneAsync(tenantId, phoneNumber, cancellationToken);
        var profile = await _dbContext.PhoneRiskProfiles
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PhoneNumber == normalizedPhone, cancellationToken);

        if (profile == null)
        {
            return new AdminPhoneRiskStatusDto
            {
                PhoneNumber = normalizedPhone,
                IsBlocked = false,
                GlobalSpamReports = await GetGlobalSpamReportsAsync(tenantId, normalizedPhone, cancellationToken),
            };
        }

        profile.IsBlocked = false;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var blockedByUser = await ResolveBlockedByUserNameAsync(profile.BlockedByUserId, cancellationToken);
        var globalSpamReports = await GetGlobalSpamReportsAsync(tenantId, normalizedPhone, cancellationToken);
        return MapStatus(profile, blockedByUser, globalSpamReports);
    }

    public async Task<AdminPhoneRiskStatusDto> ReportSpamAsync(
        Guid tenantId,
        Guid orderId,
        string phoneNumber,
        Guid? reportedByUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhone = await NormalizePhoneAsync(tenantId, phoneNumber, cancellationToken);
        var utcNow = DateTime.UtcNow;
        var createdReport = false;

        if (!await HasReportedSpamAsync(tenantId, orderId, cancellationToken))
        {
            _dbContext.PhoneSpamReports.Add(new PhoneSpamReport
            {
                Id = Guid.NewGuid(),
                PhoneNumber = normalizedPhone,
                TenantId = tenantId,
                OrderId = orderId,
                ReportedByUserId = reportedByUserId,
                CreatedAtUtc = utcNow,
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                createdReport = true;
            }
            catch (DbUpdateException)
            {
                // Unique(TenantId, OrderId) makes repeated button presses idempotent.
                _dbContext.ChangeTracker.Clear();
            }
        }

        var tenantSpamReports = await _dbContext.PhoneSpamReports
            .AsNoTracking()
            .CountAsync(x => x.TenantId == tenantId && x.PhoneNumber == normalizedPhone, cancellationToken);

        var globalSpamReports = await _dbContext.PhoneSpamReports
            .AsNoTracking()
            .CountAsync(x => x.PhoneNumber == normalizedPhone, cancellationToken);

        var profile = await _dbContext.PhoneRiskProfiles
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PhoneNumber == normalizedPhone, cancellationToken);

        if (profile == null)
        {
            profile = new PhoneRiskProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PhoneNumber = normalizedPhone,
                CreatedAtUtc = utcNow,
            };

            _dbContext.PhoneRiskProfiles.Add(profile);
        }

        profile.SpamReports = tenantSpamReports;
        profile.UpdatedAtUtc = utcNow;

        var globalReputation = await _dbContext.GlobalPhoneReputations
            .FirstOrDefaultAsync(x => x.PhoneNumber == normalizedPhone, cancellationToken);

        if (globalReputation == null)
        {
            globalReputation = new GlobalPhoneReputation
            {
                Id = Guid.NewGuid(),
                PhoneNumber = normalizedPhone,
                SpamReports = globalSpamReports,
                FirstReportedAtUtc = utcNow,
                LastReportedAtUtc = createdReport ? utcNow : utcNow,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow,
            };

            _dbContext.GlobalPhoneReputations.Add(globalReputation);
        }
        else
        {
            globalReputation.SpamReports = globalSpamReports;
            if (createdReport)
            {
                globalReputation.LastReportedAtUtc = utcNow;
            }
            globalReputation.UpdatedAtUtc = utcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var blockedByUser = await ResolveBlockedByUserNameAsync(profile.BlockedByUserId, cancellationToken);
        return MapStatus(profile, blockedByUser, globalSpamReports);
    }

    public async Task<AdminPagedResult<AdminBlockedPhoneListItemDto>> ListBlockedAsync(
        Guid tenantId,
        AdminBlockedPhoneQuery query,
        CancellationToken cancellationToken = default)
    {
        var blockedQuery = _dbContext.PhoneRiskProfiles
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsBlocked);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            var compactSearch = new string(search.Where(char.IsDigit).ToArray());

            blockedQuery = blockedQuery.Where(x =>
                x.PhoneNumber.Contains(search) ||
                (!string.IsNullOrWhiteSpace(compactSearch) && x.PhoneNumber.Contains(compactSearch)) ||
                (x.BlockedReason != null && x.BlockedReason.Contains(search)));
        }

        var totalCount = await blockedQuery.CountAsync(cancellationToken);

        var profiles = await blockedQuery
            .OrderByDescending(x => x.BlockedAtUtc ?? x.UpdatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var userIds = profiles
            .Where(x => x.BlockedByUserId.HasValue)
            .Select(x => x.BlockedByUserId!.Value)
            .Distinct()
            .ToList();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FirstName, x.LastName, x.Username })
            .ToListAsync(cancellationToken);

        var userNames = users.ToDictionary(
            x => x.Id,
            x =>
            {
                var fullName = string.Join(" ", new[] { x.FirstName, x.LastName }
                    .Where(v => !string.IsNullOrWhiteSpace(v))).Trim();

                return string.IsNullOrWhiteSpace(fullName) ? x.Username : fullName;
            });

        var items = profiles.Select(x => new AdminBlockedPhoneListItemDto
        {
            Id = x.Id,
            PhoneNumber = x.PhoneNumber,
            SpamReports = x.SpamReports,
            IsBlocked = x.IsBlocked,
            BlockedReason = x.BlockedReason,
            BlockedByUserId = x.BlockedByUserId,
            BlockedByUser = x.BlockedByUserId.HasValue && userNames.TryGetValue(x.BlockedByUserId.Value, out var userName)
                ? userName
                : null,
            BlockedAtUtc = x.BlockedAtUtc,
        }).ToList();

        _logger.LogInformation(
            "Listed blocked phones | TenantId: {TenantId} | Count: {Count} | Search: {Search}",
            tenantId,
            items.Count,
            query.Search ?? string.Empty);

        return new AdminPagedResult<AdminBlockedPhoneListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    private async Task<string?> ResolveBlockedByUserNameAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue)
        {
            return null;
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId.Value)
            .Select(x => new { x.FirstName, x.LastName, x.Username })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return null;
        }

        var fullName = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(v => !string.IsNullOrWhiteSpace(v))).Trim();
        return string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName;
    }

    private static AdminPhoneRiskStatusDto MapStatus(PhoneRiskProfile profile, string? blockedByUser, int globalSpamReports = 0) =>
        new()
        {
            PhoneNumber = profile.PhoneNumber,
            SpamReports = profile.SpamReports,
            GlobalSpamReports = globalSpamReports,
            IsBlocked = profile.IsBlocked,
            BlockedReason = profile.BlockedReason,
            BlockedByUserId = profile.BlockedByUserId,
            BlockedByUser = blockedByUser,
            BlockedAtUtc = profile.BlockedAtUtc,
        };

    private static string CleanPhone(string phoneNumber)
    {
        var cleaned = new string(phoneNumber
            .Trim()
            .Where(c => char.IsDigit(c) || c == '+')
            .ToArray());

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        var digits = new string(cleaned.Where(char.IsDigit).ToArray());
        if (cleaned.StartsWith('+'))
        {
            return "+" + digits;
        }

        return digits;
    }

    private static string FallbackNormalize(string cleanedPhone)
    {
        var normalized = CleanPhone(cleanedPhone);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Phone number is required.", nameof(cleanedPhone));
        }

        return normalized;
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "***";
        }

        var trimmed = phone.Trim();
        if (trimmed.Length <= 6)
        {
            return "***";
        }

        return $"{trimmed[..3]}***{trimmed[^2..]}";
    }
}
