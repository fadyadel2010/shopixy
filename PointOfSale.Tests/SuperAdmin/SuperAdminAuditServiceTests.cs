using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.SuperAdmin;

public class SuperAdminAuditServiceTests
{
    [Fact]
    public async Task LogActionAsync_Persists_Audit_Item_With_RequestId_Correlation()
    {
        var services = new ServiceCollection();
        var databaseRoot = SharedInMemoryDatabaseRoot.Instance;
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        using var provider = services.BuildServiceProvider();
        var logger = new RecordingLogger<SuperAdminAuditService>();
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "req-audit-123"
            }
        };
        httpContextAccessor.HttpContext!.Request.Path = "/api/super-admin/finance/overview";
        httpContextAccessor.HttpContext.Request.Method = HttpMethods.Get;

        var service = new SuperAdminAuditService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            httpContextAccessor,
            logger);

        var superAdminId = Guid.NewGuid();
        await service.LogActionAsync(
            action: "FINANCE_OVERVIEW_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-overview",
            description: "Viewed finance overview.",
            success: true,
            details: new { section = "overview" },
            actorId: superAdminId,
            actorEmail: "root@commercehub.test");

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(logger.Exceptions.Count == 0, string.Join(Environment.NewLine, logger.Exceptions));
        var auditItem = Assert.Single(await dbContext.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(superAdminId, auditItem.UserId);
        Assert.Equal("req-audit-123", auditItem.ReferenceNumber);
        Assert.Equal("SuperAdminAPI", auditItem.Source);

        using var metadata = JsonDocument.Parse(auditItem.AfterDataJson!);
        Assert.Equal("req-audit-123", metadata.RootElement.GetProperty("RequestId").GetString());
        Assert.Equal(superAdminId, metadata.RootElement.GetProperty("ActorId").GetGuid());
        Assert.Equal("/api/super-admin/finance/overview", metadata.RootElement.GetProperty("Endpoint").GetString());
        Assert.Equal("GET", metadata.RootElement.GetProperty("Method").GetString());
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<Exception> Exceptions { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception != null)
            {
                Exceptions.Add(exception);
            }
        }
    }

}
