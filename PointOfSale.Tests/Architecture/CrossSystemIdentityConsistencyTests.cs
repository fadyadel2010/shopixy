using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Architecture;

public sealed class CrossSystemIdentityConsistencyTests
{
    [Fact]
    public async Task CommerceHubOrder_WithStorefrontCustomer_MustLinkBothSubjectsToSameCanonicalIdentity()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);

        var onlineCustomerId = Guid.NewGuid();
        dbContext.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = onlineCustomerId,
            TenantId = tenantId,
            Email = "buyer@example.com",
            NormalizedEmail = "buyer@example.com",
            DisplayName = "Buyer"
        });
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await dbContext.SaveChangesAsync();
        }

        var commerceHubCustomer = CommerceHubCustomer.Create(
            "buyer@example.com",
            "Buyer",
            phoneNumber: null,
            passwordHash: "hash");

        dbContext.CommerceHubCustomers.Add(commerceHubCustomer);
        dbContext.CommerceHubMarketplaceOrders.Add(new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = commerceHubCustomer.Id,
            OnlineCustomerId = onlineCustomerId,
            OrderNumber = "MKT-IDENTITY-1",
            FullName = "Buyer",
            PhoneNumber = "01000000000",
            AddressLine = "Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "card",
            PaymentMethodName = "Card"
        });

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await dbContext.SaveChangesAsync();
        }

        var links = await dbContext.CanonicalIdentityLinks
            .IgnoreQueryFilters()
            .ToListAsync();

        var commerceHubLink = Assert.Single(links, link =>
            link.SubjectType == CanonicalIdentitySubjectType.CommerceHubCustomer &&
            link.SubjectId == commerceHubCustomer.Id);
        var storefrontLink = Assert.Single(links, link =>
            link.SubjectType == CanonicalIdentitySubjectType.OnlineCustomer &&
            link.SubjectId == onlineCustomerId);

        Assert.Equal(commerceHubLink.CanonicalIdentityId, storefrontLink.CanonicalIdentityId);
    }

    [Fact]
    public async Task CommerceHubOrder_WithConflictingExistingCanonicalLinks_MustFailClosed()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);

        var onlineCustomerId = Guid.NewGuid();
        var commerceHubCustomer = CommerceHubCustomer.Create(
            "buyer@example.com",
            "Buyer",
            phoneNumber: null,
            passwordHash: "hash");

        dbContext.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = onlineCustomerId,
            TenantId = tenantId,
            Email = "buyer@example.com",
            NormalizedEmail = "buyer@example.com",
            DisplayName = "Buyer"
        });
        dbContext.CommerceHubCustomers.Add(commerceHubCustomer);

        dbContext.CanonicalIdentityLinks.Add(CanonicalIdentityLink.Create(
            Guid.NewGuid(),
            CanonicalIdentitySubjectType.OnlineCustomer,
            onlineCustomerId,
            tenantId,
            "Storefront",
            "buyer@example.com"));
        dbContext.CanonicalIdentityLinks.Add(CanonicalIdentityLink.Create(
            Guid.NewGuid(),
            CanonicalIdentitySubjectType.CommerceHubCustomer,
            commerceHubCustomer.Id,
            null,
            "CommerceHub",
            "buyer@example.com"));

        await dbContext.SaveChangesAsync();

        dbContext.CommerceHubMarketplaceOrders.Add(new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = commerceHubCustomer.Id,
            OnlineCustomerId = onlineCustomerId,
            OrderNumber = "MKT-IDENTITY-CONFLICT",
            FullName = "Buyer",
            PhoneNumber = "01000000000",
            AddressLine = "Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "card",
            PaymentMethodName = "Card"
        });

        using var scope = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());

        Assert.Contains("Canonical identity conflict", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AffiliateUser_AccountAndLegacyPosUser_MustShareCanonicalIdentity()
    {
        var tenantId = Guid.NewGuid();
        var posUserId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);

        var account = AffiliateAccount.Create(
            tenantId,
            posUserId,
            "AFF123",
            "Affiliate");
        account.Id = Guid.NewGuid();

        var user = AffiliateUser.Create(
            tenantId,
            "affiliate@example.com",
            "hash",
            account.Id);
        user.Id = Guid.NewGuid();

        dbContext.AffiliateAccounts.Add(account);
        dbContext.AffiliateUsers.Add(user);

        await dbContext.SaveChangesAsync();

        var links = await dbContext.CanonicalIdentityLinks
            .IgnoreQueryFilters()
            .ToListAsync();

        var posUserLink = Assert.Single(links, link =>
            link.SubjectType == CanonicalIdentitySubjectType.PosUser &&
            link.SubjectId == posUserId);
        var accountLink = Assert.Single(links, link =>
            link.SubjectType == CanonicalIdentitySubjectType.AffiliateAccount &&
            link.SubjectId == account.Id);
        var affiliateUserLink = Assert.Single(links, link =>
            link.SubjectType == CanonicalIdentitySubjectType.AffiliateUser &&
            link.SubjectId == user.Id);

        Assert.Equal(posUserLink.CanonicalIdentityId, accountLink.CanonicalIdentityId);
        Assert.Equal(accountLink.CanonicalIdentityId, affiliateUserLink.CanonicalIdentityId);
    }

    [Fact]
    public void CanonicalIdentityAuthority_MustBeConfiguredAndGuarded()
    {
        var dbContextSource = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CanonicalIdentityLinkConfiguration.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260723214642_Baseline_Recovery3.cs"));

        Assert.Contains("DbSet<CanonicalIdentityLink>", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("EnsureCanonicalIdentityLinks", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("CanonicalIdentitySubjectType.OnlineCustomer", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("CanonicalIdentitySubjectType.CommerceHubCustomer", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("CanonicalIdentitySubjectType.AffiliateAccount", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("CanonicalIdentitySubjectType.StoreTenant", dbContextSource, StringComparison.Ordinal);

        Assert.Contains("builder.ToTable(\"CanonicalIdentityLinks\")", configuration, StringComparison.Ordinal);
        Assert.Contains("UX_CanonicalIdentityLinks_Tenant_Subject_Active", configuration, StringComparison.Ordinal);
        Assert.Contains("UX_CanonicalIdentityLinks_Global_Subject_Active", configuration, StringComparison.Ordinal);
        Assert.Contains("IX_CanonicalIdentityLinks_Tenant_Subject", configuration, StringComparison.Ordinal);

        Assert.Contains("name: \"CanonicalIdentityLinks\"", migration, StringComparison.Ordinal);
        Assert.Contains("CanonicalIdentityId = table.Column<Guid>(type: \"uuid\", nullable: false)", migration, StringComparison.Ordinal);

        Assert.Contains("\\\"TenantId\\\" IS NOT NULL AND \\\"IsDeleted\\\" = false AND \\\"RevokedAtUtc\\\" IS NULL", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("\\\"TenantId\\\" IS NULL AND \\\"IsDeleted\\\" = false AND \\\"RevokedAtUtc\\\" IS NULL", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("SetFilter(\"[TenantId] IS NOT NULL AND [IsDeleted] = 0 AND [RevokedAtUtc] IS NULL\")", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("SetFilter(\"[TenantId] IS NULL AND [IsDeleted] = 0 AND [RevokedAtUtc] IS NULL\")", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("SetFilter(\"\\\"TenantId\\\" IS NOT NULL AND \\\"IsDeleted\\\" = 0 AND \\\"RevokedAtUtc\\\" IS NULL\")", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("SetFilter(\"\\\"TenantId\\\" IS NULL AND \\\"IsDeleted\\\" = 0 AND \\\"RevokedAtUtc\\\" IS NULL\")", dbContextSource, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static string InfrastructurePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.Infrastructure" }
                .Concat(parts)
                .ToArray()));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.Infrastructure").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
