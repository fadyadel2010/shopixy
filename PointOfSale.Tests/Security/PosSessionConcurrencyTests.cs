using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data.Configurations;

namespace PointOfSale.Tests.Security;

public sealed class PosSessionConcurrencyTests
{
    [Fact]
    public async Task ConcurrentSessionRotations_RejectStaleWriterInsteadOfResurrectingSession()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SessionBindingDbContext>()
            .UseSqlite(connection)
            .Options;
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var setup = new SessionBindingDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Set<Tenant>().Add(new Tenant
            {
                Id = tenantId,
                Name = "Session concurrency tenant",
                Status = TenantStatus.Active,
                IsActive = true
            });
            setup.Users.Add(new User
            {
                Id = userId,
                TenantId = tenantId,
                Username = $"admin-{userId:N}",
                Email = $"admin-{userId:N}@example.test",
                PasswordHash = "hash",
                FirstName = "Concurrent",
                LastName = "Session",
                Role = UserRole.Admin,
                IsActive = true,
                RefreshToken = "initial-session-binding",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(30),
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        await using var winner = new SessionBindingDbContext(options);
        await using var staleWriter = new SessionBindingDbContext(options);
        var winnerUser = await winner.Users.SingleAsync(user => user.Id == userId);
        var staleUser = await staleWriter.Users.SingleAsync(user => user.Id == userId);

        winnerUser.RefreshToken = "winning-rotated-binding";
        await winner.SaveChangesAsync();

        staleUser.RefreshToken = "stale-resurrection-binding";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleWriter.SaveChangesAsync());

        await using var verify = new SessionBindingDbContext(options);
        Assert.Equal("winning-rotated-binding", (await verify.Users.SingleAsync()).RefreshToken);
    }

    private sealed class SessionBindingDbContext(DbContextOptions<SessionBindingDbContext> options)
        : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            new UserConfiguration().Configure(modelBuilder.Entity<User>());
        }
    }
}
