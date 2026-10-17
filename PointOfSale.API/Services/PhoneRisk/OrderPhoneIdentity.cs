namespace PointOfSale.API.Services.PhoneRisk;

internal static class OrderPhoneIdentity
{
    // Checkout enforcement uses the shipping phone saved on the order. A later
    // account/profile edit must not silently retarget this order's risk actions.
    internal static string? Resolve(string? checkoutPhone, string? customerPhone, string? accountPhone) =>
        !string.IsNullOrWhiteSpace(checkoutPhone) ? checkoutPhone :
        !string.IsNullOrWhiteSpace(customerPhone) ? customerPhone :
        !string.IsNullOrWhiteSpace(accountPhone) ? accountPhone : null;
}
