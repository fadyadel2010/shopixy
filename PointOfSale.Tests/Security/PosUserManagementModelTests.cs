using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Security;

/// <summary>
/// Wave 1 authority proofs from EF model metadata (provider-agnostic) plus
/// SQLite-enforced behavior (unique indexes, FK, transactions, OCC).
/// Native PostgreSQL enforcement (xmin bump, partial-index races,
/// FOR UPDATE) runs on the SOL PostgreSQL lane; the migration carries
/// fail-closed preflight guards for deploy time.
/// </summary>
public sealed class PosUserManagementModelTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public PosUserManagementModelTests()
    {
        _connection.Open();
        _connection.CreateFunction<string?, string?>("btrim", value => value?.Trim());
        _connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (value, characters) => value?.Trim(characters?.ToCharArray() ?? []));
        _connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void NpgsqlModel_MapsUserAuthority()
    {
        var model = NpgsqlModel();

        var user = model.FindEntityType(typeof(User))!;
        var usernameIndex = Assert.Single(
            user.GetIndexes().Where(i => i.Properties.Select(p => p.Name).SequenceEqual(["NormalizedUsername"])));
        Assert.True(usernameIndex.IsUnique);
        var emailIndex = Assert.Single(
            user.GetIndexes().Where(i => i.Properties.Select(p => p.Name).SequenceEqual(["NormalizedEmail"])));
        Assert.True(emailIndex.IsUnique);

        var ownerIndex = Assert.Single(
            user.GetIndexes().Where(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["TenantId"])));
        Assert.NotNull(ownerIndex.GetFilter());
        Assert.Contains("IsDeleted", ownerIndex.GetFilter(), StringComparison.Ordinal);
        Assert.Contains("\"Role\" = 0", ownerIndex.GetFilter(), StringComparison.Ordinal);
        Assert.Contains(user.GetIndexes(), index =>
            !index.IsUnique &&
            index.GetDatabaseName() == "IX_Users_TenantId" &&
            index.Properties.Select(property => property.Name).SequenceEqual(["TenantId"]));

        var tenantFk = Assert.Single(
            user.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType == typeof(Tenant)));
        Assert.Equal(DeleteBehavior.Restrict, tenantFk.DeleteBehavior);

        var rowVersion = user.FindProperty(nameof(User.RowVersion))!;
        Assert.Equal("xmin", rowVersion.GetColumnName());
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
    }

    [Fact]
    public void NpgsqlModel_MapsTokenAuthority()
    {
        var model = NpgsqlModel();

        var token = model.FindEntityType(typeof(PosUserSecurityToken))!;
        var hashIndex = Assert.Single(
            token.GetIndexes().Where(i => i.Properties.Select(p => p.Name).SequenceEqual(["TokenHash"])));
        Assert.True(hashIndex.IsUnique);

        var composite = Assert.Single(token.GetIndexes().Where(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "UserId", "Purpose", "ExpiresAtUtc"])));
        Assert.False(composite.IsUnique);

        var userFk = Assert.Single(
            token.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType == typeof(User)));
        Assert.Equal(DeleteBehavior.Restrict, userFk.DeleteBehavior);

        Assert.NotNull(token.GetQueryFilter());
        Assert.NotNull(model.FindEntityType(typeof(User))!.GetQueryFilter());
    }

    [Fact]
    public async Task SqliteDdl_ContainsTokenChecksPartialOwnerIndexAndTenantFk()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new TestDb(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        // CHECK constraints are PostgreSQL-portable comparisons, so the
        // SQLite DDL must contain them verbatim (PG-dialect checks are the
        // only ones stripped by TestDb).
        var tokenDdl = await db.Database
            .SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE name = 'PosUserSecurityTokens'")
            .SingleAsync();
        Assert.Contains("CK_PosUserSecurityTokens_ExpiryAfterCreation", tokenDdl, StringComparison.Ordinal);
        Assert.Contains("CK_PosUserSecurityTokens_ConsumedNotBeforeCreation", tokenDdl, StringComparison.Ordinal);
        Assert.Contains("CK_PosUserSecurityTokens_RevokedNotBeforeCreation", tokenDdl, StringComparison.Ordinal);

        var ownerIndexDdl = await db.Database
            .SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE name = 'IX_Users_TenantId_SoleOwner'")
            .SingleAsync();
        Assert.Contains("UNIQUE", ownerIndexDdl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Role", ownerIndexDdl, StringComparison.Ordinal);

        var fks = await db.Database
            .SqlQueryRaw<ForeignKeyRow>("SELECT \"table\" AS TableName, \"from\" AS FromColumn, \"to\" AS ToColumn, on_delete AS OnDelete FROM pragma_foreign_key_list('Users')")
            .ToListAsync();
        var tenantFk = Assert.Single(fks.Where(fk =>
            fk.TableName == "Tenants" && fk.FromColumn == "TenantId" && fk.ToColumn == "Id"));
        Assert.Equal("RESTRICT", tenantFk.OnDelete);
    }

    [Fact]
    public void SqliteModel_UsesClientBumpedRowVersionColumn()
    {
        var tenantContext = new TenantContext();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new ApplicationDbContext(options, tenantContext);

        var rowVersion = db.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.RowVersion))!;
        Assert.Equal("RowVersion", rowVersion.GetColumnName());
        Assert.True(rowVersion.IsConcurrencyToken);
    }

    [Fact]
    public async Task Sqlite_EnforcesNormalizedUniquenessAndTenantFk()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new TestDb(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "T",
            IsActive = true,
            Status = TenantStatus.Active,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        db.Users.Add(UserRow(Guid.NewGuid(), tenantId, "SameName", "same@example.com"));
        await db.SaveChangesAsync();

        // Case-insensitive duplicate username fails at the database level.
        db.Users.Add(UserRow(Guid.NewGuid(), tenantId, "samename", "other@example.com"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // Orphan TenantId fails at the database level (Restrict FK).
        db.Users.Add(UserRow(Guid.NewGuid(), Guid.NewGuid(), "orphan", "orphan@example.com"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sqlite_ConcurrentUserWrites_RejectStaleWriter()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        var userId = Guid.NewGuid();
        using (var setup = new TestDb(options, tenantContext))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "T",
                IsActive = true,
                Status = TenantStatus.Active,
                Currency = "EGP",
                CreatedAtUtc = DateTime.UtcNow,
            });
            setup.Users.Add(UserRow(userId, tenantId, "racer", "racer@example.com"));
            await setup.SaveChangesAsync();
        }

        using var winner = new TestDb(options, tenantContext);
        using var stale = new TestDb(options, tenantContext);
        var winnerRow = await winner.Users.SingleAsync(u => u.Id == userId);
        var staleRow = await stale.Users.SingleAsync(u => u.Id == userId);

        winnerRow.FirstName = "Winner";
        await winner.SaveChangesAsync();

        staleRow.FirstName = "Stale";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());

        using var verify = new TestDb(options, tenantContext);
        Assert.Equal("Winner", (await verify.Users.SingleAsync(u => u.Id == userId)).FirstName);
    }

    [Fact]
    public async Task PasswordHashChange_RevokesRefreshAuthority()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new PrivilegedRefreshTokenRevocationInterceptor(
                NullLogger<PrivilegedRefreshTokenRevocationInterceptor>.Instance))
            .Options;

        var userId = Guid.NewGuid();
        using (var setup = new TestDb(options, tenantContext))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "T",
                IsActive = true,
                Status = TenantStatus.Active,
                Currency = "EGP",
                CreatedAtUtc = DateTime.UtcNow,
            });
            var user = UserRow(userId, tenantId, "pwduser", "pwd@example.com");
            user.RefreshToken = "live-binding";
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
        }

        using (var mutate = new TestDb(options, tenantContext))
        {
            var row = await mutate.Users.SingleAsync(u => u.Id == userId);
            row.PasswordHash = "rotated-hash";
            await mutate.SaveChangesAsync();
        }

        using (var verify = new TestDb(options, tenantContext))
        {
            var row = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
            Assert.Null(row.RefreshToken);
            Assert.Null(row.RefreshTokenExpiryTime);
            Assert.NotNull(await verify.AuditLogs.IgnoreQueryFilters()
                .SingleOrDefaultAsync(a => a.ActionType == "PRIVILEGED_REFRESH_REVOKED"));
        }
    }

    [Fact]
    public async Task AuthorityChange_StillAuditsRevocationWhenServiceClearsBindingFirst()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new PrivilegedRefreshTokenRevocationInterceptor(
                NullLogger<PrivilegedRefreshTokenRevocationInterceptor>.Instance))
            .Options;
        var userId = Guid.NewGuid();

        using (var setup = new TestDb(options, tenantContext))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "T",
                IsActive = true,
                Status = TenantStatus.Active,
                Currency = "EGP",
                CreatedAtUtc = DateTime.UtcNow,
            });
            var user = UserRow(userId, tenantId, "role-user", "role@example.com");
            user.RefreshToken = "live-binding";
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
        }

        using (var mutate = new TestDb(options, tenantContext))
        {
            var user = await mutate.Users.SingleAsync(row => row.Id == userId);
            user.Role = UserRole.Manager;
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            await mutate.SaveChangesAsync();
        }

        using var verify = new TestDb(options, tenantContext);
        Assert.NotNull(await verify.AuditLogs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.ActionType == "PRIVILEGED_REFRESH_REVOKED"));
    }

    private static IModel NpgsqlModel()
    {
        using var db = NpgsqlContext();
        return db.Model;
    }

    private sealed class ForeignKeyRow
    {
        public string TableName { get; set; } = string.Empty;
        public string FromColumn { get; set; } = string.Empty;
        public string ToColumn { get; set; } = string.Empty;
        public string OnDelete { get; set; } = string.Empty;
    }

    private static ApplicationDbContext NpgsqlContext()
    {
        var tenantContext = new TenantContext();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused-model-only;Username=u;Password=p")
            .Options;
        return new ApplicationDbContext(options, tenantContext);
    }

    private static User UserRow(Guid id, Guid tenantId, string username, string email) => new()
    {
        Id = id,
        TenantId = tenantId,
        Username = username,
        Email = email,
        PasswordHash = "hash",
        FirstName = "F",
        LastName = "L",
        Role = UserRole.Cashier,
        IsActive = true,
        InvitationAcceptedAtUtc = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };

    private sealed class TestDb(DbContextOptions<ApplicationDbContext> options, TenantContext tenantContext)
        : ApplicationDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var check in entityType.GetCheckConstraints().ToList())
                {
                    if (check.Sql.Contains('~') ||
                        check.Sql.Contains("E'", StringComparison.Ordinal) ||
                        check.Sql.Contains("::", StringComparison.Ordinal) ||
                        check.Sql.Contains("regexp_replace", StringComparison.OrdinalIgnoreCase))
                    {
                        ((IMutableEntityType)entityType).RemoveCheckConstraint(check.Name);
                    }
                }
            }
        }
    }
}
