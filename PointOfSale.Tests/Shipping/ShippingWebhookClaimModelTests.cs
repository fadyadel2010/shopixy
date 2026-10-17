using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Migrations;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

// Metadata / synthetic tests only. Never opens a PostgreSQL connection or runs SQL.
public sealed class ShippingWebhookClaimModelTests
{
    [Fact]
    public void Claims_DefaultDisabled()
        => Assert.False(new ShippingWebhookClaimOptions().Enabled);

    [Fact]
    public void Migration_IsAdditiveWithFencedKeyUniqueOwnerAndRestrictiveForeignKey()
    {
        var migration = new ShippingWebhookOwnerClaims { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
        Assert.Equal(2, migration.UpOperations.Count);
        var table = Assert.IsType<CreateTableOperation>(migration.UpOperations[0]);
        Assert.Equal("ShippingWebhookClaims", table.Name);
        Assert.Equal(new[] { "LogId", "Generation" }, table.PrimaryKey!.Columns);
        Assert.Equal(9, table.Columns.Count);
        Assert.Equal(2, table.CheckConstraints.Count);
        Assert.Equal(ReferentialAction.Restrict, Assert.Single(table.ForeignKeys).OnDelete);
        var index = Assert.IsType<CreateIndexOperation>(migration.UpOperations[1]);
        Assert.True(index.IsUnique); Assert.Equal(new[] { "LogId", "Owner" }, index.Columns);
        Assert.DoesNotContain(migration.UpOperations, op => op is UpdateDataOperation or SqlOperation or DropIndexOperation or AlterColumnOperation);
    }

    [Fact]
    public void ProposedSql_ContainsOnlyNewTableAndIndex_NoHistoricalOwnerBackfill()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused_offline_model;Username=unused;Password=unused").Options);
        var migration = new ShippingWebhookOwnerClaims { ActiveProvider = db.Database.ProviderName! };
        var statements = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.GetService<IDesignTimeModel>().Model);
        var sql = string.Join("\n", statements.Select(c => c.CommandText));
        Assert.Contains("CREATE TABLE \"ShippingWebhookClaims\"", sql);
        Assert.Contains("CREATE UNIQUE INDEX", sql);
        Assert.DoesNotContain("UPDATE ", sql); Assert.DoesNotContain("INSERT INTO", sql);
        Assert.DoesNotContain("ALTER TABLE \"ShippingWebhookLogs\"", sql);
    }

    [Fact]
    public async Task Protocol_RefusesInMemoryInsteadOfSimulatingRelationalOwnership()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ClaimModel-{Guid.NewGuid()}").Options, new TestTenantContext(Guid.NewGuid()));
        var protocol = new ShippingWebhookClaimProtocol(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => protocol.AcquireAsync(new ShippingWebhookLog(), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => protocol.FailAsync(new ShippingWebhookLog(), new ShippingWebhookClaim(), "test", 0, default));
    }
}
