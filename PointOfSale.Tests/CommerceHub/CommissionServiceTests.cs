using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.CommerceHub;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommissionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public CommissionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ResolveCommission_ShouldPreferVendorThenCategoryThenGlobal()
    {
        var vendorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        _dbContext.CommissionRules.AddRange(
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Global,
                Rate = 0.10m,
                IsActive = true,
                Priority = 1
            },
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Category,
                ScopeId = categoryId,
                Rate = 0.15m,
                IsActive = true,
                Priority = 10
            },
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Vendor,
                ScopeId = vendorId,
                Rate = 0.20m,
                IsActive = true,
                Priority = 20
            });
        await _dbContext.SaveChangesAsync();

        var service = CreateService(staticFallbackRate: 0.08m);

        var vendorResolution = service.ResolveCommission(vendorId, categoryId);
        var categoryResolution = service.ResolveCommission(Guid.NewGuid(), categoryId);
        var globalResolution = service.ResolveCommission(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(0.20m, vendorResolution.Rate);
        Assert.Equal(CommissionRuleScopeType.Vendor, vendorResolution.ScopeType);
        Assert.False(vendorResolution.UsedLegacyFallback);
        Assert.Equal(CommissionCalculator.VendorSource, vendorResolution.Source);

        Assert.Equal(0.15m, categoryResolution.Rate);
        Assert.Equal(CommissionRuleScopeType.Category, categoryResolution.ScopeType);
        Assert.False(categoryResolution.UsedLegacyFallback);
        Assert.Equal(CommissionCalculator.CategorySource, categoryResolution.Source);

        Assert.Equal(0.10m, globalResolution.Rate);
        Assert.Equal(CommissionRuleScopeType.Global, globalResolution.ScopeType);
        Assert.False(globalResolution.UsedLegacyFallback);
        Assert.Equal(CommissionCalculator.GlobalSource, globalResolution.Source);
    }

    [Fact]
    public void ResolveCommission_ShouldFailClosedWhenNoRuleMatches()
    {
        var service = CreateService(staticFallbackRate: 0.08m);

        var exception = Assert.Throws<CommissionAuthorityException>(() => service.ResolveCommission(null, null));

        Assert.Equal("COMMISSION_AUTHORITY_MISSING", exception.Code);
    }

    [Fact]
    public async Task ResolveCommission_ShouldFailClosedWhenAuthorityIsAmbiguous()
    {
        var vendorId = Guid.NewGuid();

        _dbContext.CommissionRules.AddRange(
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Vendor,
                ScopeId = vendorId,
                Rate = 0.10m,
                IsActive = true,
                Priority = 50
            },
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Vendor,
                ScopeId = vendorId,
                Rate = 0.15m,
                IsActive = true,
                Priority = 50
            });
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        var exception = Assert.Throws<CommissionAuthorityException>(() => service.ResolveCommission(vendorId, null));

        Assert.Equal("COMMISSION_AUTHORITY_AMBIGUOUS", exception.Code);
    }

    [Fact]
    public async Task ResolveCommission_ShouldChooseHighestPriorityWithinSameScope()
    {
        var vendorId = Guid.NewGuid();

        _dbContext.CommissionRules.AddRange(
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Global,
                Rate = 0.10m,
                IsActive = true,
                Priority = 1
            },
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Vendor,
                ScopeId = vendorId,
                Rate = 0.12m,
                IsActive = true,
                Priority = 50
            },
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Vendor,
                ScopeId = vendorId,
                Rate = 0.18m,
                IsActive = true,
                Priority = 200
            });
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        var resolution = service.ResolveCommission(vendorId, null);

        Assert.Equal(0.18m, resolution.Rate);
        Assert.Equal(CommissionRuleScopeType.Vendor, resolution.ScopeType);
        Assert.Equal(200, resolution.Priority);
    }

    [Fact]
    public async Task ResolveCommission_ShouldPreferVendorOverHigherPriorityCategory()
    {
        var vendorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        _dbContext.CommissionRules.AddRange(
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Category,
                ScopeId = categoryId,
                Rate = 0.35m,
                IsActive = true,
                Priority = 999
            },
            new CommissionRule
            {
                Id = Guid.NewGuid(),
                ScopeType = CommissionRuleScopeType.Vendor,
                ScopeId = vendorId,
                Rate = 0.12m,
                IsActive = true,
                Priority = 1
            });
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        var resolution = service.ResolveCommission(vendorId, categoryId);

        Assert.Equal(0.12m, resolution.Rate);
        Assert.Equal(CommissionCalculator.VendorSource, resolution.Source);
        Assert.Equal(1, resolution.Priority);
    }

    [Fact]
    public async Task ResolveCommission_ShouldRefreshAfterManagementWriteInvalidatesCache()
    {
        var existingRule = new CommissionRule
        {
            Id = Guid.NewGuid(),
            ScopeType = CommissionRuleScopeType.Global,
            Rate = 0.10m,
            IsActive = true,
            Priority = 10
        };

        _dbContext.CommissionRules.Add(existingRule);
        await _dbContext.SaveChangesAsync();

        var service = CreateService(staticFallbackRate: 0.10m);
        var managementService = new SuperAdminCommissionManagementService(_dbContext, service);

        var initial = service.ResolveCommission(null, null);
        Assert.Equal(0.10m, initial.Rate);
        Assert.False(initial.UsedLegacyFallback);

        await managementService.UpdateRuleAsync(
            existingRule.Id,
            new SuperAdminUpsertCommissionRuleRequest
            {
                ScopeType = CommissionRuleScopeType.Global,
                Rate = 0.25m,
                Priority = 20
            },
            actorId: null,
            cancellationToken: CancellationToken.None);

        var refreshed = service.ResolveCommission(null, null);

        Assert.Equal(0.25m, refreshed.Rate);
        Assert.False(refreshed.UsedLegacyFallback);
        Assert.Equal(CommissionCalculator.GlobalSource, refreshed.Source);
    }

    [Fact]
    public async Task InvalidateRulesCache_ShouldPublishDistributedInvalidationEvent()
    {
        // Arrange
        var publishCompletionSource = new TaskCompletionSource<bool>();
        var mockEventPublisher = new Mock<IEventPublisher>();
        
        // Hook the mock to signal completion when PublishAsync is invoked
        mockEventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<CommissionRulesCacheInvalidatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => publishCompletionSource.TrySetResult(true))
            .Returns(Task.CompletedTask);
        
        var service = CreateService(eventPublisher: mockEventPublisher.Object);

        // Act
        service.InvalidateRulesCache();

        // Wait deterministically for the fire-and-forget Task.Run to complete
        // Use a reasonable timeout to avoid hanging tests if something goes wrong
        var completedInTime = await Task.WhenAny(
            publishCompletionSource.Task,
            Task.Delay(TimeSpan.FromSeconds(5))) == publishCompletionSource.Task;
        
        Assert.True(completedInTime, "PublishAsync should have been called within timeout");

        // Assert
        mockEventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<CommissionRulesCacheInvalidatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "InvalidateRulesCache should publish exactly one distributed invalidation event");

        mockEventPublisher.VerifyNoOtherCalls();
    }

    [Fact]
    public void InvalidateLocalRulesCache_ShouldNotPublishDistributedInvalidationEvent()
    {
        // Arrange
        var mockEventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(eventPublisher: mockEventPublisher.Object);

        // Act
        service.InvalidateLocalRulesCache();

        // Give any potential async operations a moment (though none should occur)
        Thread.Sleep(100);

        // Assert
        mockEventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<CommissionRulesCacheInvalidatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "InvalidateLocalRulesCache should NOT publish distributed invalidation events");

        mockEventPublisher.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private CommissionService CreateService(
        decimal staticFallbackRate = 0.10m,
        IEventPublisher? eventPublisher = null)
    {
        return new CommissionService(
            _dbContext,
            Options.Create(new CommerceHubPaymentsOptions
            {
                PlatformCommissionRate = staticFallbackRate
            }),
            new CommissionServiceTelemetry(TimeSpan.FromMinutes(5)),
            NullLogger<CommissionService>.Instance,
            eventPublisher ?? Mock.Of<IEventPublisher>());
    }
}
