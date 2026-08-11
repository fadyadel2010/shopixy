using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Tests.Architecture;
using PointOfSale.Tests.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PointOfSale.Tests.CustomDomains;

/// <summary>
/// DOMAIN-2C persistence validation tests.
///
/// Verifies that the Domain entity, Entity Configuration, and Repository
/// implementation match the DOMAIN-1.5 persistence freeze exactly.
///
/// Architecture certification tests (source-level) verify structural conformance
/// without requiring a live database.
/// In-memory EF Core tests verify query filter behavior, index enforcement,
/// and repository lookups.
/// </summary>
public class DomainPersistenceTests
{
    // ======================================================================
    // ARCHITECTURE CERTIFICATION TESTS (source-level conformance to DOMAIN-1.5)
    // ======================================================================

    [Fact]
    public void DomainEntity_MatchesDOMAIN15_Schema()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Domain.cs");

        // Required fields per DOMAIN-1.5 Section 4.1
        Assert.Contains("public Guid Id", entity);
        Assert.Contains("public Guid TenantId", entity);
        Assert.Contains("public string HostName", entity);
        Assert.Contains("public string NormalizedHostName", entity);
        Assert.Contains("public DomainType DomainType", entity);
        Assert.Contains("public DomainLifecycleState LifecycleState", entity);
        Assert.Contains("public Guid? SslCertificateId", entity);
        Assert.Contains("public string VerificationToken", entity);
        Assert.Contains("public VerificationMethod? VerificationMethod", entity);
        Assert.Contains("public RedirectType RedirectType", entity);
        Assert.Contains("public Guid? RedirectTargetDomainId", entity);
        Assert.Contains("public CanonicalPolicy CanonicalPolicy", entity);
        Assert.Contains("public DateTime CreatedAtUtc", entity);
        Assert.Contains("public DateTime? VerifiedAtUtc", entity);
        Assert.Contains("public DateTime? SslActivatedAtUtc", entity);
        Assert.Contains("public DateTime? UpdatedAtUtc", entity);
        Assert.Contains("public DateTime? DeletedAtUtc", entity);
        Assert.Contains("public bool IsDeleted", entity);
        Assert.Contains("public int Version", entity);
        Assert.Contains("public string DomainHistoryJson", entity);

        // Domain entity should NOT inherit BaseEntity (uses Utc-suffixed audit fields)
        Assert.DoesNotContain(": BaseEntity", entity);
    }

    [Fact]
    public void DomainEntity_ImplementsITenantOwned()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Domain.cs");
        Assert.Contains("ITenantOwned", entity);
    }

    [Fact]
    public void DomainEntity_HasLifecycleMethods()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Domain.cs");

        // DOMAIN-1.5 Section 5: Lifecycle method contracts
        Assert.Contains("MarkVerified", entity);
        Assert.Contains("MarkSslPending", entity);
        Assert.Contains("MarkSslActive", entity);
        Assert.Contains("MarkDisabled", entity);
        Assert.Contains("MarkArchived", entity);
        Assert.Contains("MarkDeleted", entity);
    }

    [Fact]
    public void DomainConfiguration_HasAllIndexes()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs");

        // DOMAIN-1.5 Section 10: indexes on Domains table
        Assert.Contains("IX_Domains_DomainType", config);
        Assert.Contains("IX_Domains_LifecycleState", config);
        Assert.Contains("UQ_Domains_NormalizedHostName", config); // unique index on NormalizedHostName
        Assert.Contains("IX_Domains_RedirectTargetDomainId", config);
        Assert.Contains("IX_Domains_SslCertificateId", config);
        Assert.Contains("UQ_Domains_TenantId_Primary", config); // filtered unique
        Assert.Contains("IX_Domains_TenantId_LifecycleState", config);
        Assert.Contains("IX_Domains_IsDeleted", config);

        // Audit Issue 4.4: IX_Domains_TenantId (non-unique) must exist
        Assert.Contains("IX_Domains_TenantId", config);
    }

    [Fact]
    public void SslCertificateConfiguration_HasAllIndexes()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/SslCertificateConfiguration.cs");

        // DOMAIN-1.5 Section 10.3: SSL indexes
        Assert.Contains("IX_SslCertificates_DomainId", config);
        Assert.Contains("IX_SslCertificates_Status", config);
        Assert.Contains("IX_SslCertificates_NotAfterUtc", config);
        Assert.Contains("IX_SslCertificates_NextRenewalUtc", config);
        Assert.Contains("IX_SslCertificates_IsDeleted", config);
        Assert.Contains("UQ_SslCertificates_DomainId_UniqueActive", config);
    }

    [Fact]
    public void DomainConfiguration_Has4CheckConstraints()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs");

        // DOMAIN-1.5 Section 4.1: 4 CHECK constraints
        Assert.Contains("CK_Domains_HostName_Length", config);
        Assert.Contains("CK_Domains_Primary_Host_Ssl", config);
        Assert.Contains("CK_Domains_Redirect_Cascade", config);
        Assert.Contains("CK_Domains_System_NoTenant", config);
    }

    [Fact]
    public void DomainConfiguration_VersionUsesXminRowVersion()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs");

        // CPD-11: Version mapped to PostgreSQL xmin system column
        Assert.Contains("HasColumnName(\"xmin\")", config);
        Assert.Contains("HasColumnType(\"xid\")", config);
        Assert.Contains("IsRowVersion()", config);
    }

    [Fact]
    public void SslCertificate_HasFilteredUnique_DomainId_Active()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/SslCertificateConfiguration.cs");

        // DOMAIN-1.5 Section 10.3: filtered unique on DomainId
        Assert.Contains("UQ_SslCertificates_DomainId_UniqueActive", config);
    }

    [Fact]
    public void DomainVerification_HasUniqueConstraint_DomainId_Method_Pending()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/DomainVerificationConfiguration.cs");

        // DOMAIN-1.5 Section 10.2: filtered unique on DomainId + Method
        Assert.Contains("UQ_DomainVerifications_DomainId_Method", config);
    }

    [Fact]
    public void DomainAuditEntry_MatchesDOMAIN15_Schema()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/DomainAuditEntry.cs");

        Assert.Contains("public Guid Id", entity);
        Assert.Contains("public Guid DomainId", entity);
        Assert.Contains("public Guid TenantId", entity);
        Assert.Contains("public string Action", entity);
        Assert.Contains("public ActorType ActorType", entity);
        Assert.Contains("public Guid? ActorUserId", entity);
        Assert.Contains("public string? BeforeSnapshotJson", entity);
        Assert.Contains("public string AfterSnapshotJson", entity);
        Assert.Contains("public string? TraceId", entity);
        Assert.Contains("public string? IpAddress", entity);
        Assert.Contains("public DateTime TimestampUtc", entity);
        Assert.Contains("ITenantOwned", entity);
    }

    [Fact]
    public void DomainAuditEntryConfiguration_HasIndexes()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/DomainAuditEntryConfiguration.cs");

        // DOMAIN-1.5 Section 10.4
        Assert.Contains("IX_DomainAuditEntries_Action", config);
        Assert.Contains("IX_DomainAuditEntries_DomainId", config);
        Assert.Contains("IX_DomainAuditEntries_TimestampUtc", config);
        Assert.Contains("IX_DomainAuditEntries_TraceId", config);
        Assert.Contains("IX_DomainAuditEntries_TenantId_TimestampUtc", config);
    }

    [Fact]
    public void Migration_HasAllDomainTablesAndConstraints()
    {
        // Find the DOMAIN-2C migration files (both .cs and .Designer.cs) across the repo
        var migrationPaths = System.IO.Directory
            .GetFiles("../../../..", "*DOMAIN_2C*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(migrationPaths);
        // Combine content from all DOMAIN-2C migration files
        var migrationContent = string.Join("\n", migrationPaths.Select(p => System.IO.File.ReadAllText(p)));

        // Tables
        Assert.Contains("b.ToTable(\"Domains\"", migrationContent);
        Assert.Contains("b.ToTable(\"DomainVerifications\"", migrationContent);
        Assert.Contains("b.ToTable(\"SslCertificates\"", migrationContent);
        Assert.Contains("b.ToTable(\"DomainAuditEntries\"", migrationContent);

        // Check constraints
        Assert.Contains("CK_Domains_HostName_Length", migrationContent);
        Assert.Contains("CK_Domains_Primary_Host_Ssl", migrationContent);
        Assert.Contains("CK_Domains_Redirect_Cascade", migrationContent);
        Assert.Contains("CK_Domains_System_NoTenant", migrationContent);

        // Key indexes
        Assert.Contains("UQ_Domains_NormalizedHostName", migrationContent);
        Assert.Contains("UQ_Domains_TenantId_Primary", migrationContent);
        Assert.Contains("UQ_SslCertificates_DomainId_UniqueActive", migrationContent);
        Assert.Contains("UQ_DomainVerifications_DomainId_Method", migrationContent);

        // Non-unique indexes (previously missing from snapshot)
        Assert.Contains("IX_Domains_TenantId", migrationContent);
        Assert.Contains("IX_SslCertificates_DomainId", migrationContent);

        // Version → xmin
        Assert.Contains(".HasColumnName(\"xmin\")", migrationContent);

        // Audit append-only guard
        Assert.Contains("prevent_domain_audit_entries_mutation", migrationContent);
        Assert.Contains("trg_domain_audit_entries_append_only", migrationContent);
        Assert.Contains("DomainAuditEntries records are append-only", migrationContent);
    }

    [Fact]
    public void DbContext_RegistersDomainDbSets()
    {
        var db = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("DbSet<Domain>", db);
        Assert.Contains("DbSet<DomainVerification>", db);
        Assert.Contains("DbSet<SslCertificate>", db);
        Assert.Contains("DbSet<DomainAuditEntry>", db);
    }

    [Fact]
    public void DbContext_HasDomainQueryFilter()
    {
        var db = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        // System and Development exempted from tenant filter (CPD-5)
        Assert.Contains("DomainType.System", db);
        Assert.Contains("DomainType.Development", db);
        Assert.Contains("!e.IsDeleted", db);
    }

    [Fact]
    public void DbContext_HasDomainAuditEntryQueryFilter()
    {
        var db = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        // CPD-4: DomainAuditEntry implements ITenantOwned — tenant isolation filter
        Assert.Contains("DomainAuditEntry", db);
        Assert.Contains("e.TenantId == CurrentTenantId", db);
        Assert.Contains("GuardAppendOnlyEvidence<DomainAuditEntry>()", db);
    }

    [Fact]
    public void DomainConfiguration_FkDirection_IsOnSslCertificate()
    {
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs");

        // FK direction: SslCertificate.DomainId → Domains.Id (NOT Domains.SslCertificateId → SslCertificates.Id)
        Assert.Contains("HasForeignKey<SslCertificate>", config);
        Assert.Contains("FK_SslCertificates_DomainId", config);
        Assert.Contains("s => s.DomainId", config);
    }

    [Fact]
    public void SslCertificateConfiguration_NoFkOnDomainsTable()
    {
        // The stale migration assumed FK_Domains_SslCertificateId existed.
        // After DOMAIN-2C fix, no FK constraint exists on Domains.SslCertificateId.
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs");
        var persistenceConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260805215917_DOMAIN_2C_0_CreateDomainTables.cs");

        // The migration source must NOT contain FK on Domains.SslCertificateId
        Assert.DoesNotContain("FK_Domains_SslCertificateId", persistenceConfig);
        // The config must reference FK on SslCertificate.DomainId
        Assert.Contains("FK_SslCertificates_DomainId", config);
    }

    [Fact]
    public void Migration_CreateDomainTables_HasCorrectFkDirection()
    {
        var migrationPath = System.IO.Directory
            .GetFiles("../../../..", "*DOMAIN_2C_0_CreateDomainTables*.cs", SearchOption.AllDirectories)
            .FirstOrDefault() ?? "";
        Assert.NotEmpty(migrationPath);
        var migrationContent = System.IO.File.ReadAllText(migrationPath);

        // FK should be on SslCertificates.DomainId → Domains.Id
        Assert.Contains("FK_SslCertificates_DomainId", migrationContent);
        Assert.Contains("principalTable: \"Domains\"", migrationContent);

        // The stale migration (215918) should be a no-op — no DropForeignKey calls
        var staleMigrationPath = System.IO.Directory
            .GetFiles("../../../..", "*DOMAIN_2C_DomainPersistence.cs", SearchOption.AllDirectories)
            .FirstOrDefault() ?? "";
        Assert.NotEmpty(staleMigrationPath);
        var staleContent = System.IO.File.ReadAllText(staleMigrationPath);
        Assert.DoesNotContain("DropForeignKey", staleContent);
        Assert.DoesNotContain("AddForeignKey", staleContent);
    }

    [Fact]
    public void Snapshot_HasDomainAndSslCertificateIndexes()
    {
        var snapshotPath = System.IO.Directory
            .GetFiles("../../../..", "ApplicationDbContextModelSnapshot.cs", SearchOption.AllDirectories)
            .FirstOrDefault() ?? "";
        Assert.NotEmpty(snapshotPath);
        var snapshotContent = System.IO.File.ReadAllText(snapshotPath);

        // Audit Issue 4.4: IX_Domains_TenantId must exist in snapshot
        Assert.Contains("IX_Domains_TenantId", snapshotContent);

        // Audit Issue 4.5: IX_SslCertificates_DomainId must exist in snapshot
        Assert.Contains("IX_SslCertificates_DomainId", snapshotContent);

        // FK direction: FK_SslCertificates_DomainId (not FK_Domains_SslCertificateId)
        Assert.Contains("FK_SslCertificates_DomainId", snapshotContent);
        Assert.DoesNotContain("FK_Domains_SslCertificateId", snapshotContent);
    }

    [Fact]
    public void Repository_Gate_SingleResponsibility_NoTenantResolution()
    {
        var repo = CertificationSource.ReadRepoFile("PointOfSale.Core/Interfaces/IDomainRepository.cs");

        // Repository interfaces must NOT reference StoreConfig or Theme.
        Assert.DoesNotContain("IStoreConfigRepository", repo);
        Assert.DoesNotContain("IStoreConfig", repo);
        Assert.DoesNotContain("IThemeProvider", repo);
        Assert.DoesNotContain("IThemeRepository", repo);
        Assert.DoesNotContain("ITenantResolver", repo);
        Assert.DoesNotContain("ITenantResolution", repo);
    }

    [Fact]
    public void Resolver_DoesNotLookupTenant()
    {
        var resolver = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs");

        // PersistenceDomainResolver must NOT have dependency injection of
        // tenant resolution services. It passes through Domain.TenantId only.
        Assert.DoesNotContain("ITenantResolver", resolver);
        Assert.DoesNotContain("ITenantResolution", resolver);
        Assert.DoesNotContain("TenantResolutionMiddleware", resolver);
        Assert.DoesNotContain("IStoreConfigRepository", resolver);
        Assert.DoesNotContain("IThemeProvider", resolver);
        Assert.DoesNotContain("IThemeRepository", resolver);
    }

    // ======================================================================
    // IN-MEMORY EF CORE TESTS (behavioral validation)
    // ======================================================================

    private static ApplicationDbContext CreateInMemoryContext(Guid tenantId, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName ?? $"DomainTest-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var tenantContext = new TestTenantContext(tenantId);
        var context = new ApplicationDbContext(options, tenantContext);
        return context;
    }

    private static Domain CreateDomain(Guid tenantId, string host, DomainType type = DomainType.Alias, DomainLifecycleState state = DomainLifecycleState.Verified)
    {
        return new Domain
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HostName = host,
            NormalizedHostName = host.ToLowerInvariant(),
            DomainType = type,
            LifecycleState = state,
            VerificationToken = "token",
            RedirectType = RedirectType.None,
            CanonicalPolicy = CanonicalPolicy.CanonicalizeToPrimary,
            IsDeleted = false
        };
    }

    [Fact]
    public async Task InMemory_FindByNormalizedHost_ReturnsMatchingDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var domain = CreateDomain(tenantId, "mybrand.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var result = await repo.FindByNormalizedHostAsync("mybrand.com", default);

        Assert.NotNull(result);
        Assert.Equal("mybrand.com", result!.NormalizedHostName);
        Assert.Equal(tenantId, result.TenantId);
    }

    [Fact]
    public async Task InMemory_FindByNormalizedHost_IgnoresTenantFilter_ForPreTenantHostResolution()
    {
        var ambientTenantId = Guid.NewGuid();
        var domainTenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(ambientTenantId);
        var domain = CreateDomain(domainTenantId, "cross-tenant-host.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var result = await repo.FindByNormalizedHostAsync("cross-tenant-host.com", default);

        Assert.NotNull(result);
        Assert.Equal(domainTenantId, result!.TenantId);
        Assert.False(result.IsDeleted);
    }

    [Fact]
    public async Task InMemory_FindByNormalizedHost_ReturnsNull_WhenNotFound()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid());
        var repo = new DomainRepository(context);

        var result = await repo.FindByNormalizedHostAsync("nonexistent.com", default);
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemory_QueryFilter_HidesDeletedDomains()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var active = CreateDomain(tenantId, "active.com", DomainType.Alias, DomainLifecycleState.Verified);
        var deleted = CreateDomain(tenantId, "deleted.com", DomainType.Primary, DomainLifecycleState.Deleted);
        deleted.IsDeleted = true;
        deleted.DeletedAtUtc = DateTime.UtcNow;

        context.Domains.Add(active);
        context.Domains.Add(deleted);
        await context.SaveChangesAsync();

        var all = await context.Domains.ToListAsync();
        Assert.Single(all);
        Assert.Equal("active.com", all[0].NormalizedHostName);
    }

    [Fact]
    public async Task InMemory_QueryFilter_HidesDeletedSystemDomains()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var activeSystem = CreateDomain(tenantId, "system-active.test", DomainType.System, DomainLifecycleState.Verified);
        var deletedSystem = CreateDomain(tenantId, "system-deleted.test", DomainType.System, DomainLifecycleState.Deleted);
        deletedSystem.IsDeleted = true;
        deletedSystem.DeletedAtUtc = DateTime.UtcNow;

        context.Domains.AddRange(activeSystem, deletedSystem);
        await context.SaveChangesAsync();

        var domains = await context.Domains.ToListAsync();

        Assert.Single(domains);
        Assert.Equal("system-active.test", domains[0].NormalizedHostName);
    }

    [Fact]
    public async Task InMemory_Resolver_ReturnsNotFound_ForUnknownHost()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid());
        var repo = new DomainRepository(context);
        var logger = NullLogger<PersistenceDomainResolver>.Instance;
        var resolver = new PersistenceDomainResolver(repo, logger);

        var result = await resolver.ResolveAsync("unknown.com", default);

        Assert.Equal(DomainResolutionStatus.NotFound, result.Status);
        Assert.False(result.CacheAttempted);
        Assert.Equal("unknown.com", result.NormalizedHost);
    }

    [Fact]
    public async Task InMemory_Resolver_ReturnsFound_ForVerifiedDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var domain = CreateDomain(tenantId, "mybrand.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var logger = NullLogger<PersistenceDomainResolver>.Instance;
        var resolver = new PersistenceDomainResolver(repo, logger);

        var result = await resolver.ResolveAsync("mybrand.com", default);

        Assert.Equal(DomainResolutionStatus.Found, result.Status);
        Assert.Equal("mybrand.com", result.NormalizedHost);
        Assert.Equal(domain.Id, result.DomainId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Null(result.RedirectType);
        Assert.False(result.CacheAttempted);
    }

    [Fact]
    public async Task InMemory_Resolver_ReturnsNotFound_ForNonServableState()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var domain = CreateDomain(tenantId, "mybrand.com", DomainType.Primary, DomainLifecycleState.Draft);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var logger = NullLogger<PersistenceDomainResolver>.Instance;
        var resolver = new PersistenceDomainResolver(repo, logger);

        var result = await resolver.ResolveAsync("mybrand.com", default);

        // Draft is not servable → NotFound
        Assert.Equal(DomainResolutionStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task InMemory_Resolver_ReturnsRedirect_ForRedirectDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var primary = CreateDomain(tenantId, "primary.com", DomainType.Primary, DomainLifecycleState.Primary);
        var redirect = CreateDomain(tenantId, "old.com", DomainType.Redirect, DomainLifecycleState.Verified);
        redirect.RedirectType = RedirectType.Permanent301;
        redirect.RedirectTargetDomainId = primary.Id;

        context.Domains.Add(primary);
        context.Domains.Add(redirect);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var logger = NullLogger<PersistenceDomainResolver>.Instance;
        var resolver = new PersistenceDomainResolver(repo, logger);

        var result = await resolver.ResolveAsync("old.com", default);

        Assert.Equal(DomainResolutionStatus.Found, result.Status);
        Assert.Equal(RedirectType.Permanent301, result.RedirectType);
        Assert.Equal("primary.com", result.RedirectTargetNormalizedHost);
    }

    [Fact]
    public async Task InMemory_Resolver_ReturnsFound_ForSslActiveDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var domain = CreateDomain(tenantId, "mybrand.com", DomainType.Alias, DomainLifecycleState.SslActive);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var logger = NullLogger<PersistenceDomainResolver>.Instance;
        var resolver = new PersistenceDomainResolver(repo, logger);

        var result = await resolver.ResolveAsync("mybrand.com", default);

        Assert.Equal(DomainResolutionStatus.Found, result.Status);
        Assert.Equal(tenantId, result.TenantId);
    }

    [Fact]
    public async Task InMemory_CanFindAliases_ForTenant()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var otherTenantId = Guid.NewGuid();

        var primary = CreateDomain(tenantId, "primary.com", DomainType.Primary, DomainLifecycleState.Primary);
        var alias = CreateDomain(tenantId, "alias1.com", DomainType.Alias, DomainLifecycleState.Verified);
        var otherTenantAlias = CreateDomain(otherTenantId, "other-tenant.com", DomainType.Alias, DomainLifecycleState.Verified);

        context.Domains.AddRange(primary, alias, otherTenantAlias);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var aliases = await repo.FindAliasDomainsAsync(tenantId, default);

        Assert.Single(aliases);
        Assert.Equal("alias1.com", aliases[0].NormalizedHostName);
    }

    [Fact]
    public async Task InMemory_NormalizedHostName_QueryIsCaseSensitive_InMemoryOnly()
    {
        // Note: The InMemory provider uses ordinal string comparison (case-sensitive).
        // PostgreSQL (Npgsql) with the domain's NormalizedHostName column would
        // use the database's collation (typically case-insensitive).
        // This test validates the InMemory behavior to document the difference.
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var domain = CreateDomain(tenantId, "mybrand.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var result = await repo.FindByNormalizedHostAsync("MYBRAND.COM", default);
        // InMemory is case-sensitive — "MYBRAND.COM" != "mybrand.com"
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemory_FindPrimaryDomain_ReturnsPrimaryDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var primary = CreateDomain(tenantId, "primary.com", DomainType.Primary, DomainLifecycleState.Primary);
        var alias = CreateDomain(tenantId, "alias.com", DomainType.Alias, DomainLifecycleState.Verified);

        context.Domains.AddRange(primary, alias);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var result = await repo.FindPrimaryDomainAsync(tenantId, default);

        Assert.NotNull(result);
        Assert.Equal(DomainType.Primary, result!.DomainType);
        Assert.Equal("primary.com", result.NormalizedHostName);
    }

    [Fact]
    public async Task InMemory_Resolver_ReturnsNull_RedirectType_ForNonRedirectDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var domain = CreateDomain(tenantId, "mybrand.com", DomainType.Primary, DomainLifecycleState.Primary);
        domain.RedirectType = RedirectType.None;
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var repo = new DomainRepository(context);
        var logger = NullLogger<PersistenceDomainResolver>.Instance;
        var resolver = new PersistenceDomainResolver(repo, logger);

        var result = await resolver.ResolveAsync("mybrand.com", default);

        Assert.Equal(DomainResolutionStatus.Found, result.Status);
        Assert.Null(result.RedirectType);
        Assert.Null(result.RedirectTargetNormalizedHost);
    }

    [Fact]
    public async Task InMemory_LifecycleMethods_TransitionStatesCorrectly()
    {
        var domain = CreateDomain(Guid.NewGuid(), "mybrand.com", DomainType.Alias, DomainLifecycleState.Draft);

        // Draft → verified → SSL active
        domain.MarkPendingVerification(VerificationMethod.DnsTxt, "verification-token");
        domain.MarkVerified();
        Assert.Equal(DomainLifecycleState.Verified, domain.LifecycleState);
        Assert.NotNull(domain.VerifiedAtUtc);

        domain.MarkSslPending();
        Assert.Equal(DomainLifecycleState.SslPending, domain.LifecycleState);

        domain.MarkSslActive();
        Assert.Equal(DomainLifecycleState.SslActive, domain.LifecycleState);
        Assert.NotNull(domain.SslActivatedAtUtc);

        // Disable
        domain.MarkDisabled();
        Assert.Equal(DomainLifecycleState.Disabled, domain.LifecycleState);

        // Soft-delete
        domain.MarkDeleted();
        Assert.True(domain.IsDeleted);
        Assert.NotNull(domain.DeletedAtUtc);
        Assert.Equal(DomainLifecycleState.Deleted, domain.LifecycleState);
    }

    [Fact]
    public void DomainResolutionResult_Found_HasDomainIdAndTenantId()
    {
        var domainId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var result = DomainResolutionResult.Found("mybrand.com", domainId, tenantId);

        Assert.Equal(DomainResolutionStatus.Found, result.Status);
        Assert.Equal("mybrand.com", result.NormalizedHost);
        Assert.Equal(domainId, result.DomainId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Null(result.RedirectType);
        Assert.Null(result.RedirectTargetNormalizedHost);
        Assert.False(result.CacheAttempted);
    }

    [Fact]
    public void DomainResolutionResult_NotFound_HasNullDomainFields()
    {
        var result = DomainResolutionResult.NotFound("unknown.com");

        Assert.Equal(DomainResolutionStatus.NotFound, result.Status);
        Assert.Equal("unknown.com", result.NormalizedHost);
        Assert.Null(result.DomainId);
        Assert.Null(result.TenantId);
        Assert.Null(result.RedirectType);
        Assert.Null(result.RedirectTargetNormalizedHost);
        Assert.False(result.CacheAttempted);
    }

    [Fact]
    public void DomainResolutionResult_PropertyCount_MatchesConstitutionalContract()
    {
        // DOMAIN-1.6: DomainResolutionResult exposes exactly 7 properties:
        // NormalizedHost, Status, CacheAttempted, DomainId, TenantId,
        // RedirectType, RedirectTargetNormalizedHost
        var props = typeof(DomainResolutionResult).GetProperties();
        Assert.Equal(7, props.Length);
    }

    [Fact]
    public async Task InMemory_DomainAuditEntry_TenantIsolation_QueryFilter()
    {
        // Audit Issue 4.3: DomainAuditEntry implements ITenantOwned but had no
        // tenant query filter. After fix, audit entries are tenant-isolated.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var databaseName = $"DomainAuditTenantFilter-{Guid.NewGuid()}";
        using var context = CreateInMemoryContext(tenantA, databaseName);

        var domainA = CreateDomain(tenantA, "a.com", DomainType.Primary, DomainLifecycleState.Primary);
        var domainB = CreateDomain(tenantB, "b.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.AddRange(domainA, domainB);
        await context.SaveChangesAsync();

        var auditA = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domainA.Id,
            TenantId = tenantA,
            Action = "DomainRegistered",
            ActorType = ActorType.Merchant,
            AfterSnapshotJson = "{}"
        };
        var auditB = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domainB.Id,
            TenantId = tenantB,
            Action = "DomainRegistered",
            ActorType = ActorType.Merchant,
            AfterSnapshotJson = "{}"
        };
        context.DomainAuditEntries.AddRange(auditA, auditB);
        await context.SaveChangesAsync();

        // Under tenantA context, auditA should be visible.
        // Under tenantB context, auditA should NOT be visible.
        var countForA = await context.DomainAuditEntries.CountAsync();
        Assert.Equal(1, countForA);

        // Switch to tenantB context
        using var contextB = CreateInMemoryContext(tenantB, databaseName);
        var countForB = await contextB.DomainAuditEntries.CountAsync();
        Assert.Equal(1, countForB);
        Assert.Equal(auditB.Id, await contextB.DomainAuditEntries.Select(entry => entry.Id).SingleAsync());
    }

    [Fact]
    public async Task InMemory_DomainAuditEntry_IsAppendOnly()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var domain = CreateDomain(tenantId, "audit-append-only.com", DomainType.Primary, DomainLifecycleState.Primary);
        var audit = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainRegistered",
            ActorType = ActorType.Merchant,
            AfterSnapshotJson = "{}"
        };

        context.Domains.Add(domain);
        context.DomainAuditEntries.Add(audit);
        await context.SaveChangesAsync();

        audit.Action = "DomainTampered";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void DomainEntity_CanonicalPolicy_DefaultIsUseAsPrimary()
    {
        // Audit Issue 5.2: CanonicalPolicy doc said default was CanonicalizeToPrimary,
        // but enum value 0 is UseAsPrimary. The default must be UseAsPrimary.
        var domain = new Domain { CanonicalPolicy = default };
        Assert.Equal((CanonicalPolicy)0, domain.CanonicalPolicy);
        Assert.Equal(CanonicalPolicy.UseAsPrimary, domain.CanonicalPolicy);
    }

    [Fact]
    public void DomainVerification_DoesNotHaveStaleQueryFilterComment()
    {
        // Audit Issue 5.1: DomainVerification comment said query filter includes
        // Domain.IsDeleted cascade, but actual filter is only !e.IsDeleted.
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/DomainVerification.cs");

        // The comment should NOT say "Domain.IsDeleted" in the query filter description
        Assert.DoesNotContain("Domain.IsDeleted", entity);
    }
}
