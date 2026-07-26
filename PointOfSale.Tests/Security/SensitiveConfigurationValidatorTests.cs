using Microsoft.Extensions.Configuration;
using PointOfSale.API.Configuration;

namespace PointOfSale.Tests.Security;

public sealed class SensitiveConfigurationValidatorTests
{
    [Fact]
    public void ValidateOrThrow_WhenAbandonedCartTokenSecretIsMissing_FailsStartup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
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
                ["AbandonedCart:N8nWebhookUrl"] = "https://n8n.example.test/webhook/recovery",
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
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => SensitiveConfigurationValidator.ValidateOrThrow(configuration));

        Assert.Contains("AbandonedCart:TokenSecret", exception.Message, StringComparison.Ordinal);
    }
}
