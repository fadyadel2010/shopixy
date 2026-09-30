using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using System.Text.RegularExpressions;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

public class WhatsAppAccountService : IWhatsAppAccountService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WhatsAppAccountService> _logger;

    public WhatsAppAccountService(
        ApplicationDbContext context,
        ILogger<WhatsAppAccountService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<WhatsAppAccount?> GetTenantAccountAsync(Guid tenantId, Guid accountId, CancellationToken ct = default)
    {
        return await _context.WhatsAppAccounts
            .Include(x => x.Tenant)
            .FirstOrDefaultAsync(x => x.Id == accountId && x.TenantId == tenantId, ct);
    }

    public async Task<List<WhatsAppAccount>> GetAccountsAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.WhatsAppAccounts
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<WhatsAppAccount> CreateAccountAsync(WhatsAppAccount account, CancellationToken ct = default)
    {
        if (!await TenantLineageAuthority.IsActiveTenantAsync(_context, account.TenantId, ct))
        {
            throw new InvalidOperationException("Active tenant authority is required.");
        }

        account.DisplayName = account.DisplayName.Trim();
        account.PhoneNumber = account.PhoneNumber.Trim();

        var existing = await _context.WhatsAppAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == account.TenantId, ct);
        if (existing != null)
        {
            if (existing.PhoneNumber == account.PhoneNumber &&
                existing.DisplayName == account.DisplayName &&
                existing.UseForOrderConfirmation == account.UseForOrderConfirmation &&
                existing.UseForOtpVerification == account.UseForOtpVerification &&
                existing.UseForPaymentApproval == account.UseForPaymentApproval &&
                existing.UseForAbandonedCartRecovery == account.UseForAbandonedCartRecovery)
            {
                return existing;
            }

            throw new InvalidOperationException("Only one WhatsApp account is allowed per store.");
        }

        await EnsureUseCaseOwnershipAsync(account, ct);
        account.CreatedAtUtc = DateTime.UtcNow;
        _context.WhatsAppAccounts.Add(account);
        await _context.SaveChangesAsync(ct);
        
        return account;
    }

    public async Task<WhatsAppAccount> UpdateAccountAsync(WhatsAppAccount account, CancellationToken ct = default)
    {
        var existing = await _context.WhatsAppAccounts
            .FirstOrDefaultAsync(x => x.Id == account.Id && x.TenantId == account.TenantId, ct);
        if (existing == null) throw new KeyNotFoundException($"WhatsApp account {account.Id} not found");

        await EnsureUseCaseOwnershipAsync(account, ct);
        var phoneChanged = !string.Equals(
            _context.Entry(existing).Property(x => x.PhoneNumber).OriginalValue,
            account.PhoneNumber,
            StringComparison.Ordinal);

        existing.DisplayName = account.DisplayName.Trim();
        existing.UseForOrderConfirmation = account.UseForOrderConfirmation;
        existing.UseForOtpVerification = account.UseForOtpVerification;
        existing.UseForPaymentApproval = account.UseForPaymentApproval;
        existing.UseForAbandonedCartRecovery = account.UseForAbandonedCartRecovery;
        existing.Status = account.Status;
        existing.Enabled = account.Enabled;
        existing.LastConnectedAtUtc = account.LastConnectedAtUtc;
        existing.LastDisconnectedAtUtc = account.LastDisconnectedAtUtc;
        existing.StatusReason = account.StatusReason;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        
        // Note: Sensitive fields like PhoneNumber should strictly be controlled if changing them resets status
        // If phone number changes, we must reset status to PendingQR
        if (phoneChanged)
        {
            existing.PhoneNumber = account.PhoneNumber.Trim();
            existing.Status = WhatsAppAccountStatus.PendingQR;
            existing.LastConnectedAtUtc = null;
            existing.Enabled = false; 
        }

        await _context.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteAccountAsync(Guid tenantId, Guid accountId, CancellationToken ct = default)
    {
        var account = await _context.WhatsAppAccounts
            .FirstOrDefaultAsync(x => x.Id == accountId && x.TenantId == tenantId, ct);
        if (account != null)
        {
            _context.WhatsAppAccounts.Remove(account);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task ToggleAccountStatusAsync(Guid tenantId, Guid accountId, bool enabled, CancellationToken ct = default)
    {
        var account = await _context.WhatsAppAccounts
            .FirstOrDefaultAsync(x => x.Id == accountId && x.TenantId == tenantId, ct);
        if (account == null) throw new KeyNotFoundException($"WhatsApp account {accountId} not found");

        if (enabled && account.Status != WhatsAppAccountStatus.Connected)
        {
            throw new InvalidOperationException("Only a transport-verified connected account can be enabled.");
        }

        if (enabled)
        {
            await EnsureUseCaseOwnershipAsync(account, ct);
        }

        account.Enabled = enabled;
        if (!enabled)
        {
            account.Status = WhatsAppAccountStatus.Disabled;
            account.LastDisconnectedAtUtc = DateTime.UtcNow;
            account.StatusReason = "Manually disabled by admin";
        }

        account.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<WhatsAppAccount?> GetActiveAccountForUseCaseAsync(Guid tenantId, WhatsAppUseCase useCase, CancellationToken ct = default)
    {
        // CRITICAL: Must ignore global query filters because this method is called from MassTransit consumers
        // which run without a tenant context. The global filter (TenantId == CurrentTenantId) would return
        // null when CurrentTenantId is null (background jobs/consumers).
        // We explicitly filter by tenantId parameter instead.
        var query = _context.WhatsAppAccounts
            .IgnoreQueryFilters()
            .Where(x =>
                x.TenantId == tenantId &&
                x.Status == WhatsAppAccountStatus.Connected &&
                x.Enabled &&
                x.Tenant.Status == TenantStatus.Active &&
                x.Tenant.DeletedAtUtc == null);

        switch (useCase)
        {
            case WhatsAppUseCase.OrderConfirmation:
                query = query.Where(x => x.UseForOrderConfirmation);
                break;
            case WhatsAppUseCase.OtpVerification:
                query = query.Where(x => x.UseForOtpVerification);
                break;
            case WhatsAppUseCase.PaymentApproval:
                query = query.Where(x => x.UseForPaymentApproval);
                break;
            case WhatsAppUseCase.AbandonedCartRecovery:
                // Smart Cart Recovery is the single feature-level opt-in. An enabled,
                // connected tenant account is sufficient at the transport boundary.
                break;
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    // ==================================================================================
    // Template Management
    // ==================================================================================

    public async Task<List<WhatsAppMessageTemplate>> GetTemplatesAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.WhatsAppMessageTemplates
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.Type)
            .ThenBy(t => t.Language)
            .ToListAsync(ct);
    }

    public async Task<WhatsAppMessageTemplate?> GetTenantTemplateAsync(Guid tenantId, Guid templateId, CancellationToken ct = default)
    {
        return await _context.WhatsAppMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId, ct);
    }

    public async Task<WhatsAppMessageTemplate> CreateTemplateAsync(WhatsAppMessageTemplate template, CancellationToken ct = default)
    {
        NormalizeAndValidateTemplate(template);

        // Check for duplicate (same tenant, type, language)
        var existing = await _context.WhatsAppMessageTemplates
            .FirstOrDefaultAsync(t => 
                t.TenantId == template.TenantId && 
                t.Type == template.Type && 
                t.Language == template.Language, ct);
        
        if (existing != null)
        {
            throw new InvalidOperationException($"Template for type '{template.Type}' and language '{template.Language}' already exists.");
        }

        _context.WhatsAppMessageTemplates.Add(template);
        await _context.SaveChangesAsync(ct);
        
        _logger.LogInformation("Created WhatsApp template {Type}/{Language} for Tenant {TenantId}", 
            template.Type, template.Language, template.TenantId);
        
        return template;
    }

    public async Task<WhatsAppMessageTemplate> UpdateTemplateAsync(WhatsAppMessageTemplate template, CancellationToken ct = default)
    {
        var existing = await _context.WhatsAppMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == template.Id && t.TenantId == template.TenantId, ct);
        if (existing == null) throw new KeyNotFoundException($"Template {template.Id} not found");

        NormalizeAndValidateTemplate(template);

        existing.Type = template.Type;
        existing.Language = template.Language;
        existing.Content = template.Content;
        existing.IsActive = template.IsActive;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        var duplicate = await _context.WhatsAppMessageTemplates
            .AnyAsync(
                t => t.Id != template.Id &&
                     t.TenantId == template.TenantId &&
                     t.Type == template.Type &&
                     t.Language == template.Language,
                ct);
        if (duplicate)
        {
            throw new InvalidOperationException(
                $"Template for type '{template.Type}' and language '{template.Language}' already exists.");
        }

        await _context.SaveChangesAsync(ct);
        
        _logger.LogInformation("Updated WhatsApp template {TemplateId} ({Type}/{Language})", 
            template.Id, template.Type, template.Language);
        
        return existing;
    }

    public async Task DeleteTemplateAsync(Guid tenantId, Guid templateId, CancellationToken ct = default)
    {
        var template = await _context.WhatsAppMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId, ct);
        if (template != null)
        {
            _context.WhatsAppMessageTemplates.Remove(template);
            await _context.SaveChangesAsync(ct);
            
            _logger.LogInformation("Deleted WhatsApp template {TemplateId}", templateId);
        }
    }

    private async Task EnsureUseCaseOwnershipAsync(WhatsAppAccount candidate, CancellationToken ct)
    {
        if (!candidate.Enabled)
        {
            return;
        }

        var conflict = await _context.WhatsAppAccounts
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.Id != candidate.Id &&
                     x.TenantId == candidate.TenantId &&
                     x.Enabled &&
                     x.Status == WhatsAppAccountStatus.Connected &&
                     ((candidate.UseForOrderConfirmation && x.UseForOrderConfirmation) ||
                      (candidate.UseForOtpVerification && x.UseForOtpVerification) ||
                      (candidate.UseForPaymentApproval && x.UseForPaymentApproval)),
                ct);

        if (conflict)
        {
            throw new InvalidOperationException(
                "Another connected account already owns one or more selected WhatsApp use cases.");
        }
    }

    private static void NormalizeAndValidateTemplate(WhatsAppMessageTemplate template)
    {
        template.Type = template.Type.Trim().Equals("OTP", StringComparison.OrdinalIgnoreCase)
            ? "Otp"
            : template.Type.Trim();
        template.Language = template.Language.Trim().ToLowerInvariant();
        template.Content = template.Content.Trim();

        var allowedVariables = template.Type switch
        {
            "OrderConfirmation" => new HashSet<string>(StringComparer.Ordinal)
            {
                "order_number", "customer_name", "total", "currency", "items_count", "store_name",
                "shipping_address", "payment_status"
            },
            "Otp" => new HashSet<string>(StringComparer.Ordinal) { "otp_code", "store_name" },
            _ => throw new InvalidOperationException("Unsupported WhatsApp template type.")
        };

        var variables = Regex.Matches(template.Content, @"\{\{([a-z0-9_]+)\}\}")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var contentWithoutValidVariables = Regex.Replace(
            template.Content,
            @"\{\{[a-z0-9_]+\}\}",
            string.Empty);

        if (variables.Any(variable => !allowedVariables.Contains(variable)) ||
            contentWithoutValidVariables.Contains("{{", StringComparison.Ordinal) ||
            contentWithoutValidVariables.Contains("}}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Template contains unsupported or malformed variables.");
        }

        var requiredVariables = template.Type == "Otp"
            ? new[] { "otp_code" }
            : new[] { "order_number", "total" };
        if (requiredVariables.Any(variable => !variables.Contains(variable)))
        {
            throw new InvalidOperationException(
                $"Template is missing required variables: {string.Join(", ", requiredVariables.Where(variable => !variables.Contains(variable)).Select(variable => $"{{{{{variable}}}}}"))}.");
        }
    }
}
