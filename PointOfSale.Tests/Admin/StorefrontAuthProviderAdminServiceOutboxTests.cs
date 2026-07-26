using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.Admin;

public sealed class StorefrontAuthProviderAdminServiceOutboxTests
{
    [Fact]
    public async Task UpdateProviderSettingsAsync_WhenEventStagingFails_DoesNotPersistCredentials()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();
        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(service => service.PublishAsync(
                It.IsAny<StorefrontAuthProviderCredentialsUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        await using (var context = CreateContext(databaseName, databaseRoot, tenantId))
        {
            var service = CreateService(context, publisher.Object);

            var result = await service.UpdateProviderSettingsAsync(
                tenantId,
                AuthProvider.Google,
                new UpdateProviderSettingsRequest
                {
                    ClientId = "google-client",
                    ClientSecret = "google-secret"
                },
                Guid.NewGuid());

            Assert.False(result.Success);
            Assert.Equal("UPDATE_FAILED", result.Error);
        }

        await using var verificationContext = CreateContext(databaseName, databaseRoot, tenantId);
        Assert.Empty(await verificationContext.StorefrontAuthProviderSettings.ToListAsync());
    }

    [Fact]
    public async Task EnableProviderAsync_WhenEventStagingFails_LeavesProviderDisabled()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var seedContext = CreateContext(databaseName, databaseRoot, tenantId))
        {
            seedContext.StorefrontAuthProviderSettings.Add(CreateConfiguredProvider(tenantId, isEnabled: false));
            await seedContext.SaveChangesAsync();
        }

        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(service => service.PublishAsync(
                It.IsAny<StorefrontAuthProviderEnabledEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        await using (var context = CreateContext(databaseName, databaseRoot, tenantId))
        {
            var service = CreateService(context, publisher.Object);

            var result = await service.EnableProviderAsync(
                tenantId,
                AuthProvider.Google,
                Guid.NewGuid());

            Assert.False(result.Success);
            Assert.Equal("ENABLE_FAILED", result.Error);
        }

        await using var verificationContext = CreateContext(databaseName, databaseRoot, tenantId);
        var provider = await verificationContext.StorefrontAuthProviderSettings.SingleAsync();
        Assert.False(provider.IsEnabled);
    }

    [Fact]
    public async Task DisableProviderAsync_WhenEventStagingFails_LeavesProviderEnabled()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var seedContext = CreateContext(databaseName, databaseRoot, tenantId))
        {
            seedContext.StorefrontAuthProviderSettings.Add(CreateConfiguredProvider(tenantId, isEnabled: true));
            await seedContext.SaveChangesAsync();
        }

        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(service => service.PublishAsync(
                It.IsAny<StorefrontAuthProviderDisabledEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        await using (var context = CreateContext(databaseName, databaseRoot, tenantId))
        {
            var service = CreateService(context, publisher.Object);

            var result = await service.DisableProviderAsync(
                tenantId,
                AuthProvider.Google,
                Guid.NewGuid(),
                "security incident");

            Assert.False(result.Success);
            Assert.Equal("DISABLE_FAILED", result.Error);
        }

        await using var verificationContext = CreateContext(databaseName, databaseRoot, tenantId);
        var provider = await verificationContext.StorefrontAuthProviderSettings.SingleAsync();
        Assert.True(provider.IsEnabled);
    }

    [Fact]
    public async Task RotateSecretAsync_WhenEventStagingFails_LeavesSecretUnchanged()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();
        const string originalSecret = "original-encrypted-secret";

        await using (var seedContext = CreateContext(databaseName, databaseRoot, tenantId))
        {
            seedContext.StorefrontAuthProviderSettings.Add(CreateConfiguredProvider(
                tenantId,
                isEnabled: true,
                encryptedSecret: originalSecret));
            await seedContext.SaveChangesAsync();
        }

        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(service => service.PublishAsync(
                It.IsAny<StorefrontAuthProviderSecretRotatedEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        await using (var context = CreateContext(databaseName, databaseRoot, tenantId))
        {
            var service = CreateService(context, publisher.Object);

            var result = await service.RotateSecretAsync(
                tenantId,
                AuthProvider.Google,
                "new-secret",
                Guid.NewGuid());

            Assert.False(result.Success);
            Assert.Equal("ROTATION_FAILED", result.Error);
        }

        await using var verificationContext = CreateContext(databaseName, databaseRoot, tenantId);
        var provider = await verificationContext.StorefrontAuthProviderSettings.SingleAsync();
        Assert.Equal(originalSecret, provider.ClientSecretEncrypted);
    }

    private static ApplicationDbContext CreateContext(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot,
        Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static StorefrontAuthProviderAdminService CreateService(
        ApplicationDbContext context,
        IEventPublisher eventPublisher)
    {
        return new StorefrontAuthProviderAdminService(
            context,
            new EphemeralDataProtectionProvider(),
            eventPublisher,
            NullLogger<StorefrontAuthProviderAdminService>.Instance);
    }

    private static StorefrontAuthProviderSettings CreateConfiguredProvider(
        Guid tenantId,
        bool isEnabled,
        string encryptedSecret = "encrypted-secret")
    {
        return new StorefrontAuthProviderSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = AuthProvider.Google,
            IsEnabled = isEnabled,
            ClientId = "google-client",
            ClientSecretEncrypted = encryptedSecret,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
