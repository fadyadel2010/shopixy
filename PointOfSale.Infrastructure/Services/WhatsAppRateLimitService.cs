using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Implementation of WhatsApp rate limiting service.
/// Phase: Abandoned Cart Hardening
/// 
/// USES PostgreSQL ON CONFLICT for atomic operations.
/// </summary>
public class WhatsAppRateLimitService : IWhatsAppRateLimitService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WhatsAppRateLimitService> _logger;

    public WhatsAppRateLimitService(
        ApplicationDbContext context,
        ILogger<WhatsAppRateLimitService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> CanSendMessageAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(tenantId, phoneNumber, ct);
        return status.CanSend;
    }

    public async Task RecordMessageSentAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var windowDuration = TimeSpan.FromHours(WhatsAppRateLimit.WindowHours);
        var windowStart = now;

        // Use atomic upsert with window reset logic
        var sql = @"
            INSERT INTO ""WhatsAppRateLimits"" (
                ""Id"", ""TenantId"", ""PhoneNumber"", ""MessageCount"", 
                ""WindowStartUtc"", ""LastMessageAtUtc"", ""OptedOut""
            ) VALUES (
                @p0, @p1, @p2, 1, @p3, @p3, FALSE
            )
            ON CONFLICT (""TenantId"", ""PhoneNumber"") DO UPDATE SET
                ""MessageCount"" = CASE 
                    WHEN ""WhatsAppRateLimits"".""WindowStartUtc"" < @p4 THEN 1
                    ELSE ""WhatsAppRateLimits"".""MessageCount"" + 1
                END,
                ""WindowStartUtc"" = CASE 
                    WHEN ""WhatsAppRateLimits"".""WindowStartUtc"" < @p4 THEN @p3
                    ELSE ""WhatsAppRateLimits"".""WindowStartUtc""
                END,
                ""LastMessageAtUtc"" = @p3";

        var windowExpiry = now.AddHours(-WhatsAppRateLimit.WindowHours);

        await _context.Database.ExecuteSqlRawAsync(
            sql,
            Guid.NewGuid(), tenantId, phoneNumber, now, windowExpiry);

        _logger.LogDebug(
            "[WhatsApp Rate Limit] Recorded message | TenantId: {TenantId} | Phone: {Phone}",
            tenantId, MaskPhone(phoneNumber));
    }

    public async Task OptOutAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var sql = @"
            INSERT INTO ""WhatsAppRateLimits"" (
                ""Id"", ""TenantId"", ""PhoneNumber"", ""MessageCount"", 
                ""WindowStartUtc"", ""LastMessageAtUtc"", ""OptedOut"", ""OptedOutAtUtc""
            ) VALUES (
                @p0, @p1, @p2, 0, @p3, @p3, TRUE, @p3
            )
            ON CONFLICT (""TenantId"", ""PhoneNumber"") DO UPDATE SET
                ""OptedOut"" = TRUE,
                ""OptedOutAtUtc"" = @p3";

        await _context.Database.ExecuteSqlRawAsync(
            sql,
            Guid.NewGuid(), tenantId, phoneNumber, now);

        _logger.LogInformation(
            "[WhatsApp Rate Limit] Phone opted out | TenantId: {TenantId} | Phone: {Phone}",
            tenantId, MaskPhone(phoneNumber));
    }

    public async Task ResetOptOutAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default)
    {
        var record = await _context.Set<WhatsAppRateLimit>()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.PhoneNumber == phoneNumber, ct);

        if (record == null) return;

        record.ResetOptOut();
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[WhatsApp Rate Limit] Opt-out reset | TenantId: {TenantId} | Phone: {Phone}",
            tenantId, MaskPhone(phoneNumber));
    }

    public async Task<RateLimitStatus> GetStatusAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default)
    {
        var record = await _context.Set<WhatsAppRateLimit>()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.PhoneNumber == phoneNumber, ct);

        if (record == null)
        {
            // No record = never messaged = can send
            return new RateLimitStatus(
                CanSend: true,
                MessageCount: 0,
                RemainingMessages: WhatsAppRateLimit.MaxMessagesPerWindow,
                OptedOut: false,
                WindowResetAtUtc: null
            );
        }

        if (record.OptedOut)
        {
            return new RateLimitStatus(
                CanSend: false,
                MessageCount: record.MessageCount,
                RemainingMessages: 0,
                OptedOut: true,
                WindowResetAtUtc: null
            );
        }

        var now = DateTime.UtcNow;
        var windowExpiry = record.WindowStartUtc.AddHours(WhatsAppRateLimit.WindowHours);

        // Check if window has expired
        if (now >= windowExpiry)
        {
            // Window expired, effectively reset
            return new RateLimitStatus(
                CanSend: true,
                MessageCount: 0,
                RemainingMessages: WhatsAppRateLimit.MaxMessagesPerWindow,
                OptedOut: false,
                WindowResetAtUtc: null
            );
        }

        var remaining = WhatsAppRateLimit.MaxMessagesPerWindow - record.MessageCount;
        var canSend = remaining > 0;

        return new RateLimitStatus(
            CanSend: canSend,
            MessageCount: record.MessageCount,
            RemainingMessages: Math.Max(0, remaining),
            OptedOut: false,
            WindowResetAtUtc: windowExpiry
        );
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4)
            return "***";
        return phone.Substring(0, 3) + "****" + phone.Substring(phone.Length - 2);
    }
}
