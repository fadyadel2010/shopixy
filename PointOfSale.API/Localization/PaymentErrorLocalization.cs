using Microsoft.Extensions.Primitives;
using PointOfSale.Core.Payments;

namespace PointOfSale.API.Localization;

public static class PaymentErrorLocalization
{
    private const string FawryUnavailableArabic =
        "خدمة فوري باي غير متاحة لإعدادات هذا المتجر الحالية. يرجى اختيار طريقة دفع أخرى.";

    public static string GetFawryUnavailableMessage(StringValues acceptLanguage) =>
        MarketplaceLocalization.ResolveLanguage(null, acceptLanguage) == MarketplaceLocalization.Arabic
            ? FawryUnavailableArabic
            : "Fawry Pay is unavailable for this store's current configuration. Please choose another payment method.";
}
