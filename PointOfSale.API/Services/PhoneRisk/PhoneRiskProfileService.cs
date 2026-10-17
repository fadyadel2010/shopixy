using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using System.Text.RegularExpressions;

namespace PointOfSale.API.Services.PhoneRisk;

public class PhoneRiskProfileService : IPhoneRiskProfileService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly IOrderVelocityService _orderVelocityService;
    private readonly ILogger<PhoneRiskProfileService> _logger;

    public PhoneRiskProfileService(
        ApplicationDbContext dbContext,
        IPhoneNumberNormalizer phoneNumberNormalizer,
        IOrderVelocityService orderVelocityService,
        ILogger<PhoneRiskProfileService> logger)
    {
        _dbContext = dbContext;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _orderVelocityService = orderVelocityService;
        _logger = logger;
    }

    public async Task<string> NormalizePhoneAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("Phone number is required.", nameof(phoneNumber));
        }

        var input = phoneNumber.Trim();
        if (input.Any(c => !char.IsDigit(c) && !char.IsWhiteSpace(c) && c is not '+' and not '-' and not '(' and not ')' and not '.') ||
            input.Count(c => c == '+') > 1 || (input.Contains('+') && !input.StartsWith('+')))
        {
            throw new ArgumentException("Phone number format is invalid.", nameof(phoneNumber));
        }
        var cleaned = CleanPhone(input);
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
            // A rejected number is not an alternative canonical identity.
            // Never turn validation failure into an unblocked lookup.
            _logger.LogDebug("Phone identity normalization rejected | TenantId: {TenantId}", tenantId);
            throw new ArgumentException("Enter a valid phone number for the configured country.", nameof(phoneNumber), ex);
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

    public async Task<AdminOrderCustomerTrustDto?> GetOrderCustomerTrustAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Where(x =>
                x.Id == orderId &&
                x.TenantId == tenantId &&
                x.Source == OrderSource.Ecommerce &&
                !x.IsDeleted)
            .Select(x => new
            {
                CustomerPhone = x.Customer != null ? x.Customer.Phone : null,
                OnlineCustomerPhone = x.OnlineCustomer != null ? x.OnlineCustomer.Phone : null,
                x.GuestPhone,
                x.IpAddress,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null)
        {
            return null;
        }

        var phoneNumber = OrderPhoneIdentity.Resolve(order.GuestPhone, order.CustomerPhone, order.OnlineCustomerPhone);
        var result = new AdminOrderCustomerTrustDto { OrderId = orderId };
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return result;
        }

        string normalizedPhone;
        try
        {
            normalizedPhone = await NormalizePhoneAsync(tenantId, phoneNumber, cancellationToken);
        }
        catch (ArgumentException)
        {
            // Legacy invalid data is unavailable evidence, never a safe identity
            // and never permission to mutate an alternative account phone.
            return result;
        }
        var phoneRisk = await GetStatusAsync(tenantId, phoneNumber, cancellationToken);
        var globalSpamReports = await GetGlobalSpamReportsAsync(tenantId, phoneNumber, cancellationToken);
        phoneRisk ??= new AdminPhoneRiskStatusDto
        {
            PhoneNumber = normalizedPhone,
            GlobalSpamReports = globalSpamReports,
        };

        // This regex is only a candidate prefilter, not identity authority. It
        // admits ASCII formatting variants and all non-ASCII candidates; the
        // shared country-aware normalizer makes the final equivalence decision.
        var suffix = normalizedPhone[^Math.Min(4, normalizedPhone.Length)..];
        var candidatePattern = string.Join("[^0-9]*", suffix.Select(c => Regex.Escape(c.ToString()))) + "[^0-9]*$";
        var historyCandidates = await _dbContext.Orders
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.Id != orderId &&
                x.Source == OrderSource.Ecommerce &&
                !x.IsDeleted)
            .Select(x => new
            {
                Phone = !string.IsNullOrWhiteSpace(x.GuestPhone) ? x.GuestPhone :
                    x.Customer != null && !string.IsNullOrWhiteSpace(x.Customer.Phone) ? x.Customer.Phone :
                    x.OnlineCustomer != null ? x.OnlineCustomer.Phone : null,
                x.Status,
            })
            .Where(x => x.Phone != null && (Regex.IsMatch(x.Phone, candidatePattern) || Regex.IsMatch(x.Phone, @"[^\x01-\x7F]")))
            .GroupBy(x => x.Phone)
            .Select(group => new
            {
                Phone = group.Key!,
                Total = group.Count(),
                Cancelled = group.Count(x => x.Status == OrderStatus.Cancelled),
            })
            .ToListAsync(cancellationToken);

        var previousOrdersCount = 0;
        var cancelledOrdersCount = 0;
        foreach (var candidate in historyCandidates)
        {
            try
            {
                if (await NormalizePhoneAsync(tenantId, candidate.Phone, cancellationToken) != normalizedPhone) continue;
                previousOrdersCount += candidate.Total;
                cancelledOrdersCount += candidate.Cancelled;
            }
            catch (ArgumentException)
            {
                // A malformed legacy phone is not evidence about this identity.
            }
        }

        var velocity = await _orderVelocityService.GetCurrentAssessmentAsync(
            order.IpAddress,
            normalizedPhone,
            cancellationToken) ?? new OrderVelocityAssessment();
        var (riskLevel, riskSignals) = AssessRisk(
            phoneRisk.SpamReports,
            globalSpamReports,
            previousOrdersCount,
            cancelledOrdersCount,
            velocity);

        result.PhoneNumber = normalizedPhone;
        result.PreviousOrdersCount = previousOrdersCount;
        result.CancelledOrdersCount = cancelledOrdersCount;
        result.HasReportedSpam = await HasReportedSpamAsync(tenantId, orderId, cancellationToken);
        result.RiskLevel = riskLevel;
        result.RiskSignals = riskSignals;
        result.PhoneRisk = phoneRisk;
        return result;
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
        Guid? reportedByUserId,
        CancellationToken cancellationToken = default)
    {
        var phoneNumber = await ResolveOrderPhoneAsync(tenantId, orderId, cancellationToken);
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

    private async Task<string> ResolveOrderPhoneAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var orderPhone = await _dbContext.Orders
            .AsNoTracking()
            .Where(x =>
                x.Id == orderId &&
                x.TenantId == tenantId &&
                x.Source == OrderSource.Ecommerce &&
                !x.IsDeleted)
            .Select(x => new
            {
                CustomerPhone = x.Customer != null ? x.Customer.Phone : null,
                OnlineCustomerPhone = x.OnlineCustomer != null ? x.OnlineCustomer.Phone : null,
                x.GuestPhone,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var phoneNumber = OrderPhoneIdentity.Resolve(orderPhone?.GuestPhone, orderPhone?.CustomerPhone, orderPhone?.OnlineCustomerPhone);

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("The order does not have an available customer phone number.", nameof(orderId));
        }

        return phoneNumber;
    }

    private static (string Level, List<string> Signals) AssessRisk(
        int tenantSpamReports,
        int globalSpamReports,
        int previousOrdersCount,
        int cancelledOrdersCount,
        OrderVelocityAssessment velocity)
    {
        var score = 0;
        var signals = new List<string>();

        if (globalSpamReports >= 5)
        {
            return ("CRITICAL_RISK", new List<string> { "SEVERE_GLOBAL_SPAM" });
        }

        if (globalSpamReports >= 3)
        {
            score += 10;
            signals.Add("GLOBAL_SPAM");
        }

        if (tenantSpamReports >= 1)
        {
            score += 5;
            signals.Add("TENANT_SPAM");
        }

        var cancellationRate = previousOrdersCount > 0
            ? cancelledOrdersCount / (double)previousOrdersCount
            : 0;
        if (previousOrdersCount >= 3 && cancellationRate > 0.6)
        {
            score += 7;
            signals.Add("HIGH_CANCELLATION_RATE");
        }

        if (previousOrdersCount == 0)
        {
            score += 2;
            signals.Add("FIRST_ORDER");
        }

        if (velocity.IpVelocityTriggered)
        {
            score += 5;
            signals.Add("IP_VELOCITY");
        }

        if (velocity.PhoneVelocityTriggered)
        {
            score += 5;
            signals.Add("PHONE_VELOCITY");
        }

        return (score switch
        {
            < 5 => "SAFE",
            <= 12 => "SUSPICIOUS",
            _ => "HIGH_RISK",
        }, signals);
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
