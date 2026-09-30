using Microsoft.Extensions.Configuration;
using PointOfSale.API.Configuration;

namespace PointOfSale.Tests.Security;

public sealed class SensitiveConfigurationValidatorTests
{
    [Fact]
    public void ValidateOrThrow_WhenAbandonedCartTokenSecretIsMissing_FailsStartup()
    {
        var settings = CreateValidSettings();
        settings.Remove("AbandonedCart:TokenSecret");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => SensitiveConfigurationValidator.ValidateOrThrow(configuration));

        Assert.Contains("AbandonedCart:TokenSecret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateOrThrow_WhenRequiredR2ValuesAreConfiguredWithoutPublicUrl_DoesNotFailStartup()
    {
        var settings = CreateValidSettings();
        settings["R2_ACCOUNT_ID"] = "0123456789abcdef0123456789abcdef";
        settings["R2_ACCESS_KEY_ID"] = "0123456789abcdef0123456789abcdef";
        settings["R2_SECRET_ACCESS_KEY"] = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        settings["R2_BUCKET"] = "shopixy-test";
        settings["R2_SERVICE_URL"] = "https://0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        SensitiveConfigurationValidator.ValidateOrThrow(configuration);
    }

    private static Dictionary<string, string?> CreateValidSettings() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=pos;Username=postgres;Password=postgres;",
        ["BaileysService:BaseUrl"] = "https://baileys.example.test",
        ["BaileysService:ApiKey"] = "baileys-secret-key-0000000000000001",
        ["JwtSettings:Secret"] = "jwt-secret-key-000000000000000000000001",
        ["SuperAdminJwt:Secret"] = "super-admin-secret-00000000000000001",
        ["StorefrontJwt:Secret"] = "storefront-secret-key-0000000000000001",
        ["CustomerJwt:Secret"] = "customer-secret-key-00000000000000001",
        ["AffiliateJwt:Secret"] = "affiliate-secret-key-0000000000000001",
        ["CommerceHub:OrderAccessSigningKey"] = "order-access-signing-key-000000000001",
        ["AbandonedCart:TokenSecret"] = "abandoned-cart-token-secret-00000000001",
        ["RabbitMQ:Host"] = "rabbitmq.example.test",
        ["RabbitMQ:VirtualHost"] = "/",
        ["RabbitMQ:Username"] = "rabbit",
        ["RabbitMQ:Password"] = "rabbitmq-password-000000000000000001",
        ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
        ["MongoDB:DatabaseName"] = "PointOfSaleTests",
        ["MongoDB:ProductsCollectionName"] = "products",
        ["Redis:Enabled"] = "false",
        ["Elasticsearch:Enabled"] = "false",
        ["Smtp:Enabled"] = "false",
        ["CommerceAI:Enabled"] = "false",
        ["CommerceHub:Payments:Paymob:Enabled"] = "false",
        ["CommerceHub:Payments:Stripe:Enabled"] = "false"
    };
}
