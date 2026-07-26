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
            // Default Fallback
            content = "📦 *Order Confirmed!* \n\n" +
                      "Hello {{customer_name}}, thank you for shopping with {{store_name}}!\n\n" +
                      "Order: *{{order_number}}*\n" +
                      "Total: *{{currency}} {{total}}* ({{items_count}} items)\n\n" +
                      "We verify your order and will notify you when it ships.";
        }

        return ReplaceVariables(content, new Dictionary<string, string>
        {
            { "order_number", data.OrderNumber },
            { "customer_name", data.CustomerName },
            { "total", data.Total.ToString("N2") },
            { "currency", data.Currency },
            { "items_count", data.ItemsCount.ToString() },
            { "store_name", string.IsNullOrEmpty(data.StoreName) ? "us" : data.StoreName }
        });
    }

    public async Task<string> ResolveOtpAsync(
        Guid tenantId,
        string otpCode,
        string language = "en",
        CancellationToken ct = default)
    {
        var template = await GetTemplateAsync(tenantId, "OTP", language, ct);
        string content;

        if (template != null)
        {
            content = template.Content;
        }
        else
        {
            // Default Fallback
            content = "🔐 Your verification code is: *{{otp_code}}*\n\n" +
                      "Do not share this code with anyone. It expires in 5 minutes.";
        }

        return ReplaceVariables(content, new Dictionary<string, string>
        {
            { "otp_code", otpCode }
        });
    }

    private async Task<WhatsAppMessageTemplate?> GetTemplateAsync(
        Guid tenantId, 
        string type, 
        string language, 
        CancellationToken ct)
    {
        // 1. Try Tenant-specific with language
        var template = await _context.WhatsAppMessageTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => 
                t.TenantId == tenantId && 
                t.Type == type && 
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
                    t.Type == type && 
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
