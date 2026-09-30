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
    public async Task GetActiveAccountForRecovery_LegacyRecoveryFlagIsNotAGate()
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
            Status = WhatsAppAccountStatus.Connected,
            Enabled = true,
            UseForAbandonedCartRecovery = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);

        var account = await service.GetActiveAccountForUseCaseAsync(
            tenantId,
            WhatsAppUseCase.AbandonedCartRecovery);

        Assert.NotNull(account);
        Assert.Equal(accountId, account.Id);
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

    [Fact]
    public async Task UpdateAccountAsync_TrackedPhoneChange_RequiresRelinking()
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
            DisplayName = "Main",
            PhoneNumber = "+201000000000",
            Status = WhatsAppAccountStatus.Connected,
            Enabled = true,
            LastConnectedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var tracked = await dbContext.WhatsAppAccounts.SingleAsync(x => x.Id == accountId);
        tracked.PhoneNumber = "+201111111111";
        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);

        var updated = await service.UpdateAccountAsync(tracked);

        Assert.Equal(WhatsAppAccountStatus.PendingQR, updated.Status);
        Assert.False(updated.Enabled);
        Assert.Null(updated.LastConnectedAtUtc);
    }

    [Fact]
    public async Task CreateAccountAsync_SameConfiguration_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Tenant",
            Slug = "tenant",
            Status = TenantStatus.Active
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);
        var first = await service.CreateAccountAsync(CreateAccount(tenantId, "+201000000000"));
        var replay = await service.CreateAccountAsync(CreateAccount(tenantId, "+201000000000"));

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, await dbContext.WhatsAppAccounts.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task CreateAccountAsync_DifferentSecondAccount_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Tenant",
            Slug = "tenant",
            Status = TenantStatus.Active
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);
        await service.CreateAccountAsync(CreateAccount(tenantId, "+201000000000"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAccountAsync(CreateAccount(tenantId, "+201111111111")));

        Assert.Contains("one WhatsApp account", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await dbContext.WhatsAppAccounts.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task CreateTemplateAsync_OtpWithoutCodeVariable_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTemplateAsync(
            new WhatsAppMessageTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Type = "OTP",
                Language = "EN",
                Content = "Your verification code is missing"
            }));

        Assert.Contains("{{otp_code}}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTemplateAsync_OtpWithStoreNameVariable_IsAccepted()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var service = new WhatsAppAccountService(dbContext, NullLogger<WhatsAppAccountService>.Instance);

        var template = await service.CreateTemplateAsync(new WhatsAppMessageTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = "OTP",
            Language = "EN",
            Content = "{{store_name}} verification code: {{otp_code}}"
        });

        Assert.Equal("Otp", template.Type);
        Assert.Equal("en", template.Language);
        Assert.Contains("{{store_name}}", template.Content, StringComparison.Ordinal);
    }

    private static WhatsAppAccount CreateAccount(Guid tenantId, string phoneNumber) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = "Main",
            PhoneNumber = phoneNumber,
            UseForOrderConfirmation = true,
            UseForOtpVerification = true
        };

    private static ApplicationDbContext CreateDbContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new TestTenantContext(tenantId));
}
