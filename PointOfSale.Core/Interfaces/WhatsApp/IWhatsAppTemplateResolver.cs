using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces.WhatsApp;

public interface IWhatsAppTemplateResolver
{
    Task<string> ResolveOrderConfirmationAsync(
        Guid tenantId, 
        OrderConfirmationData data, 
        string language = "en",
        CancellationToken ct = default);
    
    Task<string> ResolveOtpAsync(
        Guid tenantId,
        string otpCode,
        string language = "en",
        CancellationToken ct = default);

    Task<string> ResolveAbandonedCartRecoveryAsync(
        Guid tenantId,
        AbandonedCartRecoveryMessageData data,
        string language = "en",
        CancellationToken ct = default);
}

public sealed class AbandonedCartRecoveryMessageData
{
    public string CustomerName { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string RecoveryLink { get; set; } = string.Empty;
    public int Sequence { get; set; }
}

public class OrderConfirmationData
{
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemsCount { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public sealed record WhatsAppSystemTemplate(
    Guid Id,
    string Type,
    string Language,
    string Content);

public static class WhatsAppSystemTemplates
{
    public static readonly IReadOnlyList<WhatsAppSystemTemplate> All =
    [
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000101"),
            "OrderConfirmation",
            "en",
            "Order confirmed\n\nHello {{customer_name}}, thank you for shopping with {{store_name}}.\n\nOrder: {{order_number}}\nItems: {{items_count}}\nTotal: {{currency}} {{total}}\nShipping: {{shipping_address}}\nPayment status: {{payment_status}}"),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000102"),
            "OrderConfirmation",
            "ar",
            "تم تأكيد الطلب\n\nمرحبًا {{customer_name}}، شكرًا لتسوقك من {{store_name}}.\n\nالطلب: {{order_number}}\nعدد المنتجات: {{items_count}}\nالإجمالي: {{total}} {{currency}}\nعنوان الشحن: {{shipping_address}}\nحالة الدفع: {{payment_status}}"),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000201"),
            "Otp",
            "en",
            "{{store_name}} verification code\n\nYour one-time verification code is:\n*{{otp_code}}*\n\nThis code expires in 5 minutes. For your security, do not share it with anyone.\n\nIf you did not request this code, you can safely ignore this message."),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000202"),
            "Otp",
            "ar",
            "رمز التحقق من {{store_name}}\n\nرمز التحقق لمرة واحدة هو:\n*{{otp_code}}*\n\nتنتهي صلاحية هذا الرمز خلال 5 دقائق. حفاظًا على أمانك، لا تشاركه مع أي شخص.\n\nإذا لم تطلب هذا الرمز، يمكنك تجاهل هذه الرسالة بأمان."),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000301"),
            "AbandonedCartRecoveryFirst",
            "en",
            "You left items in your cart at {{store_name}}. Complete your order here: {{recovery_link}}"),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000302"),
            "AbandonedCartRecoveryFirst",
            "ar",
            "تركت منتجات في سلتك لدى {{store_name}}. أكمل طلبك من هنا: {{recovery_link}}"),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000303"),
            "AbandonedCartRecoverySecond",
            "en",
            "A reminder from {{store_name}}: your cart is still available. Continue here: {{recovery_link}}"),
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000304"),
            "AbandonedCartRecoverySecond",
            "ar",
            "تذكير من {{store_name}}: سلتك ما زالت متاحة. تابع من هنا: {{recovery_link}}")
    ];

    public static string GetContent(string type, string language)
    {
        var canonicalType = type.Equals("OTP", StringComparison.OrdinalIgnoreCase) ? "Otp" : type;
        var canonicalLanguage = language.Equals("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";

        return All.Single(template =>
            template.Type == canonicalType && template.Language == canonicalLanguage).Content;
    }
}
