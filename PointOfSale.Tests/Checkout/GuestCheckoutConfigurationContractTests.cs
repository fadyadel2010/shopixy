using PointOfSale.API.Services.Checkout;

namespace PointOfSale.Tests.Checkout;

public sealed class GuestCheckoutConfigurationContractTests
{
    [Fact]
    public void GuestDisabledFailure_UsesStableAdmissionContract()
    {
        var exception = new CheckoutGuestDisabledException();

        Assert.Equal("GUEST_CHECKOUT_DISABLED", exception.ErrorCode);
        Assert.Equal("StorefrontAvailability", exception.Step);
    }

    [Fact]
    public void CheckoutService_UsesSqlGuestCheckoutAuthorityBeforeAndDuringCommit()
    {
        var source = File.ReadAllText(RepositoryFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs"));

        Assert.Contains("config.Behavior.EnableGuestCheckout", source, StringComparison.Ordinal);
        Assert.Contains("!customerId.HasValue && !isAffiliateCheckout && enableGuestCheckout != true", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "!string.IsNullOrWhiteSpace(request.AffiliateReferralCode)"));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
