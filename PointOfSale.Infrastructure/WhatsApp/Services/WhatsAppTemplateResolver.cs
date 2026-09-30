using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

public class WhatsAppTemplateResolver : IWhatsAppTemplateResolver
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WhatsAppTemplateResolver> _logger;

    public WhatsAppTemplateResolver(
        ApplicationDbContext context,
        ILogger<WhatsAppTemplateResolver> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> ResolveOrderConfirmationAsync(
        Guid tenantId, 
        OrderConfirmationData data, 
        string language = "en",
        CancellationToken ct = default)
    {
        var template = await GetTemplateAsync(tenantId, "OrderConfirmation", language, ct);
        string content;

        if (template != null)
        {
            content = template.Content;
        }
        else
        {
            content = WhatsAppSystemTemplates.GetContent("OrderConfirmation", language);
        }

        return ReplaceVariables(content, new Dictionary<string, string>
        {
            { "order_number", data.OrderNumber },
            { "customer_name", data.CustomerName },
            { "total", data.Total.ToString("N2") },
            { "currency", data.Currency },
            { "items_count", data.ItemsCount.ToString() },
            { "store_name", string.IsNullOrEmpty(data.StoreName) ? "us" : data.StoreName },
            { "shipping_address", data.ShippingAddress },
            { "payment_status", data.PaymentStatus }
        });
    }

    public async Task<string> ResolveOtpAsync(
        Guid tenantId,
        string otpCode,
        string language = "en",
        CancellationToken ct = default)
    {
        var template = await GetTemplateAsync(tenantId, "Otp", language, ct);
        var storeName = await GetStoreNameAsync(tenantId, language, ct);
        string content;

        if (template != null)
        {
            content = template.Content;
        }
        else
        {
            content = WhatsAppSystemTemplates.GetContent("Otp", language);
        }

        return ReplaceVariables(content, new Dictionary<string, string>
        {
            { "otp_code", otpCode },
            { "store_name", storeName }
        });
    }

    public async Task<string> ResolveAbandonedCartRecoveryAsync(
        Guid tenantId,
        AbandonedCartRecoveryMessageData data,
        string language = "en",
        CancellationToken ct = default)
    {
        var type = data.Sequence == 2
            ? "AbandonedCartRecoverySecond"
            : "AbandonedCartRecoveryFirst";
        var template = await GetTemplateAsync(tenantId, type, language, ct);
        var content = template?.Content ?? WhatsAppSystemTemplates.GetContent(type, language);

        return ReplaceVariables(content, new Dictionary<string, string>
        {
            { "customer_name", string.IsNullOrWhiteSpace(data.CustomerName) ? "Customer" : data.CustomerName },
            { "store_name", data.StoreName },
            { "recovery_link", data.RecoveryLink }
        });
    }

    private async Task<string> GetStoreNameAsync(Guid tenantId, string language, CancellationToken ct)
    {
        var storeName = await _context.StoreConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(config => config.TenantId == tenantId)
            .Select(config => config.StoreName)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(storeName))
        {
            storeName = await _context.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.Id == tenantId)
                .Select(tenant => tenant.Name)
                .FirstOrDefaultAsync(ct);
        }

        return string.IsNullOrWhiteSpace(storeName)
            ? language.Equals("ar", StringComparison.OrdinalIgnoreCase) ? "متجرنا" : "Our store"
            : storeName.Trim();
    }

    private async Task<WhatsAppMessageTemplate?> GetTemplateAsync(
        Guid tenantId, 
        string type, 
        string language, 
        CancellationToken ct)
    {
        // 1. Try Tenant-specific with language
        var canonicalType = type.Equals("OTP", StringComparison.OrdinalIgnoreCase) ? "Otp" : type;
        var template = await _context.WhatsAppMessageTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => 
                t.TenantId == tenantId && 
                (t.Type == canonicalType || (canonicalType == "Otp" && t.Type == "OTP")) &&
                t.Language == language && 
                t.IsActive, ct);

        if (template != null) return template;

        // 2. Try Tenant-specific default language (en)
        if (language != "en")
        {
             template = await _context.WhatsAppMessageTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => 
                    t.TenantId == tenantId && 
                    (t.Type == canonicalType || (canonicalType == "Otp" && t.Type == "OTP")) &&
                    t.Language == "en" && 
                    t.IsActive, ct);
             
             if (template != null) return template;
        }

        // 3. Try System Default (IsDefault = true) - assuming TenantId is optional or separate query
        // Implement if system-wide defaults are stored in DB. For now, we rely on code fallback.
        
        return null;
    }

    private static string ReplaceVariables(string template, Dictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
        {
            template = template.Replace("{{" + key + "}}", value);
        }
        return template;
    }
}
