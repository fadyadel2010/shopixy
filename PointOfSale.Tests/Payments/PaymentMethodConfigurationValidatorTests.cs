using PointOfSale.Core.Payments;

namespace PointOfSale.Tests.Payments;

public sealed class PaymentMethodConfigurationValidatorTests
{
    [Fact]
    public void Cod_DoesNotRequireCredentialConfiguration()
    {
        var result = PaymentMethodConfigurationValidator.Validate("COD", null);

        Assert.False(result.RequiresConfiguration);
        Assert.True(result.IsConfigurationValid);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void ProviderAliases_AreNormalizedBeforeValidation()
    {
        var stripe = PaymentMethodConfigurationValidator.Validate("STRIPE", new Dictionary<string, string>
        {
            ["publishableKey"] = "pk_test_valid",
            ["apiKey"] = "sk_test_valid",
            ["webhookSecret"] = "whsec_test_valid"
        });
        var fawry = PaymentMethodConfigurationValidator.Validate("FAWRY", new Dictionary<string, string>
        {
            ["merchantCode"] = "merchant",
            ["securityKey"] = "security",
            ["webhookSecret"] = "webhook"
        });

        Assert.True(stripe.IsConfigurationValid);
        Assert.Equal("sk_test_valid", stripe.NormalizedConfiguration["secretKey"]);
        Assert.True(fawry.IsConfigurationValid);
        Assert.Equal("security", fawry.NormalizedConfiguration["apiKey"]);
    }

    [Theory]
    [InlineData("pk_live_valid", "sk_test_valid", "whsec_valid")]
    [InlineData("pk_test_valid", "sk_live_valid", "whsec_valid")]
    [InlineData("publishable", "sk_test_valid", "whsec_valid")]
    [InlineData("pk_test_valid", "secret", "whsec_valid")]
    [InlineData("pk_test_valid", "rk_test_valid", "webhook")]
    public void Stripe_RejectsInvalidOrMixedEnvironmentCredentials(
        string publishableKey,
        string secretKey,
        string webhookSecret)
    {
        var result = PaymentMethodConfigurationValidator.Validate("STRIPE", new Dictionary<string, string>
        {
            ["publishableKey"] = publishableKey,
            ["secretKey"] = secretKey,
            ["webhookSecret"] = webhookSecret
        });

        Assert.False(result.IsConfigurationValid);
    }

    [Fact]
    public void ManualWallet_RequiresTenantWalletNumber()
    {
        var missing = PaymentMethodConfigurationValidator.Validate("MANUALWALLET", new Dictionary<string, string>());
        var valid = PaymentMethodConfigurationValidator.Validate("MANUALWALLET", new Dictionary<string, string>
        {
            ["walletNumber"] = "01099998888"
        });

        Assert.False(missing.IsConfigurationValid);
        Assert.Contains("Manual wallet number is required.", missing.ValidationErrors);
        Assert.True(valid.IsConfigurationValid);
        Assert.Equal("01099998888", valid.PublicConfiguration["walletNumber"]);
    }

    [Fact]
    public void Paymob_AcceptsAndNormalizesMultipleIntegrationIds()
    {
        var result = PaymentMethodConfigurationValidator.Validate("PAYMOB", new Dictionary<string, string>
        {
            ["publicKey"] = "egy_pk_test_valid",
            ["secretKey"] = "egy_sk_test_valid",
            ["integrationId"] = "3153058, 3153060;3153058",
            ["hmacSecret"] = "hmac-valid"
        });

        Assert.True(result.IsConfigurationValid);
        Assert.Equal("3153058,3153060", result.NormalizedConfiguration["integrationId"]);
        Assert.True(PaymentMethodConfigurationValidator.TryParsePaymobIntegrationIds(
            result.NormalizedConfiguration["integrationId"],
            out var integrationIds));
        Assert.Equal([3153058, 3153060], integrationIds);
    }

    [Fact]
    public void Paymob_RejectsMalformedIntegrationIdList()
    {
        var result = PaymentMethodConfigurationValidator.Validate("PAYMOB", new Dictionary<string, string>
        {
            ["publicKey"] = "egy_pk_test_valid",
            ["secretKey"] = "egy_sk_test_valid",
            ["integrationId"] = "3153058, wallet",
            ["hmacSecret"] = "hmac-valid"
        });

        Assert.False(result.IsConfigurationValid);
        Assert.Contains(
            "Gateway integration IDs must be one or more positive integers separated by commas.",
            result.ValidationErrors);
    }
}
