using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Storefront;

public sealed class PaymentMethodReadAuthorityTests
{
    [Fact]
    public async Task GetEnabledPaymentMethods_WhenSqlAuthorityIsDisabled_DoesNotReadOrExposeProjection()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "COD",
            DisplayName = "Cash on Delivery",
            IsEnabled = false,
            SupportsOffline = true,
            FeeType = "fixed",
            Version = 4,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var collection = new Mock<IMongoCollection<PaymentMethodDocument>>(MockBehavior.Strict);
        var database = new Mock<IMongoDatabase>();
        database
            .Setup(item => item.GetCollection<PaymentMethodDocument>("payment_methods", null))
            .Returns(collection.Object);
        var repository = new PaymentMethodReadRepository(
            database.Object,
            dbContext,
            new EphemeralDataProtectionProvider(),
            NullLogger<PaymentMethodReadRepository>.Instance);

        var result = await repository.GetEnabledPaymentMethodsAsync(tenantId, CancellationToken.None);

        Assert.Empty(result);
        collection.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByCode_WhenSqlAuthorityHasNoTenantRow_DoesNotTrustProjection()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var collection = new Mock<IMongoCollection<PaymentMethodDocument>>(MockBehavior.Strict);
        var database = new Mock<IMongoDatabase>();
        database
            .Setup(item => item.GetCollection<PaymentMethodDocument>("payment_methods", null))
            .Returns(collection.Object);
        var repository = new PaymentMethodReadRepository(
            database.Object,
            dbContext,
            new EphemeralDataProtectionProvider(),
            NullLogger<PaymentMethodReadRepository>.Instance);

        var result = await repository.GetByCodeAsync(tenantId, "MANUALWALLET", CancellationToken.None);

        Assert.Null(result);
        collection.VerifyNoOtherCalls();
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }
}
