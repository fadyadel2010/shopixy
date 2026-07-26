using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.WhatsApp.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Infrastructure;

public sealed class WhatsAppAccountServiceTenantScopeTests
{
    [Fact]
    public async Task UpdateAccountAsync_MismatchedTenantId_RejectsCrossTenantMutation()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.WhatsAppAccounts.Add(new WhatsAppAccount
        {
            Id = accountId,
            TenantId = foreignTenantId,
            DisplayName = "Foreign",
            PhoneNumber = "+201000000000",
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAccountAsync(new WhatsAppAccount
        {
            Id = accountId,
            TenantId = tenantId,
            DisplayName = "Mutated",
            PhoneNumber = "+201111111111"
        }));

        var stored = await dbContext.WhatsAppAccounts.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == accountId);
        Assert.Equal("Foreign", stored.DisplayName);
        Assert.Equal(foreignTenantId, stored.TenantId);
    }

    [Fact]
    public async Task UpdateTemplateAsync_MismatchedTenantId_RejectsCrossTenantMutation()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.WhatsAppMessageTemplates.Add(new WhatsAppMessageTemplate
        {
            Id = templateId,
            TenantId = foreignTenantId,
            Type = "OrderConfirmation",
            Language = "en",
            Content = "Foreign",
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateTemplateAsync(new WhatsAppMessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Type = "OrderConfirmation",
            Language = "en",
            Content = "Mutated"
        }));

        var stored = await dbContext.WhatsAppMessageTemplates.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == templateId);
        Assert.Equal("Foreign", stored.Content);
        Assert.Equal(foreignTenantId, stored.TenantId);
    }

    [Fact]
    public async Task UpdateAccountAsync_PersistsAllUseCaseFlags()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Tenant",
            Slug = "tenant",
            Status = TenantStatus.Active
        });
        dbContext.WhatsAppAccounts.Add(new WhatsAppAccount
        {
            Id = accountId,
            TenantId = tenantId,
            DisplayName = "Account",
            PhoneNumber = "+201000000000",
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);

        await service.UpdateAccountAsync(new WhatsAppAccount
        {
            Id = accountId,
            TenantId = tenantId,
            DisplayName = "Updated",
            PhoneNumber = "+201000000000",
            UseForOrderConfirmation = true,
            UseForOtpVerification = true,
            UseForPaymentApproval = true,
            UseForAbandonedCartRecovery = true
        });

        var stored = await dbContext.WhatsAppAccounts.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == accountId);
        Assert.True(stored.UseForOrderConfirmation);
        Assert.True(stored.UseForOtpVerification);
        Assert.True(stored.UseForPaymentApproval);
        Assert.True(stored.UseForAbandonedCartRecovery);
    }

    [Fact]
    public async Task ToggleAccountStatusAsync_DisableTransitionsLifecycleStatus()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.WhatsAppAccounts.Add(new WhatsAppAccount
        {
            Id = accountId,
            TenantId = tenantId,
            DisplayName = "Account",
            PhoneNumber = "+201000000000",
            Status = WhatsAppAccountStatus.Connected,
            Enabled = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);

        await service.ToggleAccountStatusAsync(tenantId, accountId, enabled: false);

        var stored = await dbContext.WhatsAppAccounts.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == accountId);
        Assert.False(stored.Enabled);
        Assert.Equal(WhatsAppAccountStatus.Disabled, stored.Status);
        Assert.NotNull(stored.LastDisconnectedAtUtc);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new TestTenantContext(tenantId));
}
