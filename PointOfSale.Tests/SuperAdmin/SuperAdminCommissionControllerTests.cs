using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.Controllers;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.SuperAdmin;

public sealed class SuperAdminCommissionControllerTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;

    public SuperAdminCommissionControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    [Fact]
    public async Task Create_ReturnsStandardizedCommissionRuleResponse_AndWritesAudit()
    {
        var auditService = new RecordingSuperAdminAuditService();
        var controller = CreateController(auditService);

        var actionResult = await controller.Create(
            new SuperAdminUpsertCommissionRuleRequest
            {
                ScopeType = CommissionRuleScopeType.Global,
                Rate = 0.10m,
                Priority = 100
            },
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminCommissionRuleDto>>(okResult.Value);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal(CommissionRuleScopeType.Global, payload.Data!.ScopeType);
        Assert.Equal(0.10m, payload.Data.Rate);
        Assert.True(payload.Data.IsActive);
        Assert.Equal(100, payload.Data.Priority);
        Assert.NotNull(payload.Meta);
        Assert.Equal("req-commission-123", payload.Meta!["requestId"]);
        Assert.Single(auditService.Actions);
        Assert.Equal("COMMISSION_CREATED", auditService.Actions[0]);
    }

    [Fact]
    public async Task Deactivate_WhenOnlyActiveGlobalRuleExists_ReturnsConflictError()
    {
        var rule = new CommissionRule
        {
            Id = Guid.NewGuid(),
            ScopeType = CommissionRuleScopeType.Global,
            Rate = 0.10m,
            IsActive = true,
            Priority = 1
        };

        _dbContext.CommissionRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        var auditService = new RecordingSuperAdminAuditService();
        var controller = CreateController(auditService);

        var beforeCall = DateTimeOffset.UtcNow;
        var actionResult = await controller.Deactivate(rule.Id, CancellationToken.None);
        var afterCall = DateTimeOffset.UtcNow;

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminErrorResponse>(objectResult.Value);

        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.False(payload.Success);
        Assert.Equal("COMMISSION_GLOBAL_REQUIRED", payload.Error.Code);
        
        // Assert CorrelationId is present and valid
        Assert.NotNull(payload.CorrelationId);
        Assert.NotEmpty(payload.CorrelationId);
        Assert.Equal("req-commission-123", payload.CorrelationId);
        
        // Assert Timestamp is present, valid, and within acceptable range
        Assert.NotEqual(default(DateTimeOffset), payload.Timestamp);
        Assert.True(payload.Timestamp >= beforeCall && payload.Timestamp <= afterCall,
            $"Timestamp {payload.Timestamp} should be between {beforeCall} and {afterCall}");
        
        Assert.Empty(auditService.Actions);
    }

    [Fact]
    public async Task Create_WhenDuplicateVendorPriorityExists_ReturnsConflictError()
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Vendor One",
            Status = TenantStatus.Active
        });
        _dbContext.CommissionRules.Add(new CommissionRule
        {
            Id = Guid.NewGuid(),
            ScopeType = CommissionRuleScopeType.Vendor,
            ScopeId = vendorId,
            Rate = 0.10m,
            IsActive = true,
            Priority = 50
        });
        await _dbContext.SaveChangesAsync();

        var auditService = new RecordingSuperAdminAuditService();
        var controller = CreateController(auditService);

        var beforeCall = DateTimeOffset.UtcNow;
        var actionResult = await controller.Create(
            new SuperAdminUpsertCommissionRuleRequest
            {
                ScopeType = CommissionRuleScopeType.Vendor,
                ScopeId = vendorId,
                Rate = 0.15m,
                Priority = 50
            },
            CancellationToken.None);
        var afterCall = DateTimeOffset.UtcNow;

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminErrorResponse>(objectResult.Value);

        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.Equal("COMMISSION_RULE_CONFLICT", payload.Error.Code);
        
        // Assert CorrelationId is present and valid
        Assert.NotNull(payload.CorrelationId);
        Assert.NotEmpty(payload.CorrelationId);
        Assert.Equal("req-commission-123", payload.CorrelationId);
        
        // Assert Timestamp is present, valid, and within acceptable range
        Assert.NotEqual(default(DateTimeOffset), payload.Timestamp);
        Assert.True(payload.Timestamp >= beforeCall && payload.Timestamp <= afterCall,
            $"Timestamp {payload.Timestamp} should be between {beforeCall} and {afterCall}");
        
        Assert.Empty(auditService.Actions);
    }

    [Fact]
    public async Task ResolvePreview_ReturnsDebugInfo()
    {
        var rule = new CommissionRule
        {
            Id = Guid.NewGuid(),
            ScopeType = CommissionRuleScopeType.Global,
            Rate = 0.17m,
            IsActive = true,
            Priority = 7
        };

        _dbContext.CommissionRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        var auditService = new RecordingSuperAdminAuditService();
        var controller = CreateController(auditService);

        var actionResult = await controller.ResolvePreview(null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminCommissionResolutionPreviewDto>>(okResult.Value);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal(0.17m, payload.Data!.Rate);
        Assert.Equal(CommissionCalculator.GlobalSource, payload.Data.Source);
        Assert.Equal(rule.Id, payload.Data.RuleId);
        Assert.Equal(7, payload.Data.Priority);
        Assert.Equal("MemoryCache", payload.Data.CacheStrategy);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _dbContext.Dispose();
    }

    private SuperAdminCommissionController CreateController(RecordingSuperAdminAuditService auditService)
    {
        var commissionService = new CommissionService(
            _dbContext,
            Options.Create(new CommerceHubPaymentsOptions
            {
                PlatformCommissionRate = 0.10m
            }),
            new CommissionServiceTelemetry(TimeSpan.FromMinutes(5)),
            NullLogger<CommissionService>.Instance,
            Mock.Of<IEventPublisher>());

        var managementService = new SuperAdminCommissionManagementService(_dbContext, commissionService);

        return new SuperAdminCommissionController(
            managementService,
            auditService,
            NullLogger<SuperAdminCommissionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };
    }

    private static DefaultHttpContext BuildHttpContext()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, "root@commercehub.test")
            },
            authenticationType: "SuperAdminJwt");

        return new DefaultHttpContext
        {
            TraceIdentifier = "req-commission-123",
            User = new ClaimsPrincipal(identity)
        };
    }

    private sealed class RecordingSuperAdminAuditService : ISuperAdminAuditService
    {
        public List<string> Actions { get; } = new();

        public Task LogActionAsync(
            string action,
            string entityType,
            string entityId,
            string description,
            bool success,
            object? details = null,
            string? reference = null,
            Guid? actorId = null,
            string? actorEmail = null,
            CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }
}
