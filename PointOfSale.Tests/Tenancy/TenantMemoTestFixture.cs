using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Tenancy;

internal sealed class TenantMemoTestFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private bool _schemaCreated;

    public CommandCounter Counter { get; } = new();

    public TestDbContext CreateContext()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(Counter)
            .Options;
        var context = new TestDbContext(options);
        if (!_schemaCreated)
        {
            context.Database.EnsureCreated();
            _schemaCreated = true;
        }

        return context;
    }

    public static Tenant SeedTenant(
        ApplicationDbContext dbContext,
        string slug,
        bool active = true)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            ContactEmail = $"{slug}@example.com",
            IsActive = active,
            Status = active ? TenantStatus.Active : TenantStatus.Suspended,
            Currency = "EGP",
            Country = "EG",
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.Tenants.Add(tenant);
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
        return tenant;
    }

    public void Dispose() => _connection.Dispose();

    internal sealed class TestDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var constraint in entityType.GetCheckConstraints().ToArray())
                {
                    entityType.RemoveCheckConstraint(constraint.Name);
                }

                foreach (var property in entityType.GetProperties()
                             .Where(property => string.Equals(
                                 property.GetColumnName(),
                                 "xmin",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("INTEGER");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                }
            }
        }
    }

    internal sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return base.ReaderExecuting(command, eventData, result);
        }
    }
}
