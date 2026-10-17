using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.CustomDomains;

/// <summary>
/// Tests for the Storefront Domain Management READ service (DOMAIN-3.1-B).
/// Verifies that the service correctly queries, filters, sorts, and maps
/// domain entities to DTO projections for all 9 screens (D01-D09).
///
/// Uses in-memory EF Core with TestTenantContext for tenant isolation.
/// </summary>
public class DomainReadServiceTests
{
    private static ApplicationDbContext CreateInMemoryContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DomainReadTest-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var tenantContext = new TestTenantContext(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }

    private static Domain CreateTestDomain(
        Guid tenantId,
        string host,
        DomainType type = DomainType.Alias,
        DomainLifecycleState state = DomainLifecycleState.Verified,
        SslCertificate? ssl = null)
    {
        return new Domain
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HostName = host,
            NormalizedHostName = host.ToLowerInvariant(),
            DomainType = type,
            LifecycleState = state,
            VerificationToken = "test-token-" + Guid.NewGuid().ToString("N")[..8],
            VerificationMethod = VerificationMethod.DnsTxt,
            RedirectType = RedirectType.None,
            CanonicalPolicy = CanonicalPolicy.CanonicalizeToPrimary,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            VerifiedAtUtc = state is DomainLifecycleState.Verified
                or DomainLifecycleState.SslPending
                or DomainLifecycleState.SslActive
                or DomainLifecycleState.Primary
                ? DateTime.UtcNow.AddDays(-9)
                : null,
        };
    }

    private static DomainReadService CreateService(ApplicationDbContext context)
    {
        return new DomainReadService(context, NullLogger<DomainReadService>.Instance);
    }

    [Fact]
    public async Task GetDomainListAsync_DoesNotDisguiseAQueryFailureAsAnEmptyPortfolio()
    {
        var tenantId = Guid.NewGuid();
        var context = CreateInMemoryContext(tenantId);
        var service = CreateService(context);
        await context.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.GetDomainListAsync(tenantId, 1, 20));
    }

    [Fact]
    public async Task GetDomainListAsync_ReturnsPagedDomains()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        context.Domains.AddRange(
            CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.Primary),
            CreateTestDomain(tenantId, "www.example.com", DomainType.Alias, DomainLifecycleState.Verified),
            CreateTestDomain(tenantId, "old.example.com", DomainType.Redirect, DomainLifecycleState.SslActive)
        );
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainListAsync(tenantId, 1, 20, null, null, null, "hostname", "asc");

        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(3, result.Items.Count);
        Assert.True(result.HasPreviousPage == false);
        Assert.True(result.HasNextPage == false);
    }

    [Fact]
    public async Task GetDomainListAsync_FiltersByType()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        context.Domains.AddRange(
            CreateTestDomain(tenantId, "primary.example.com", DomainType.Primary),
            CreateTestDomain(tenantId, "alias.example.com", DomainType.Alias),
            CreateTestDomain(tenantId, "redirect.example.com", DomainType.Redirect)
        );
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainListAsync(tenantId, 1, 20, null, DomainType.Alias, null, null, null);

        Assert.Single(result.Items);
        Assert.Equal("alias.example.com", result.Items[0].HostName);
    }

    [Fact]
    public async Task GetDomainListAsync_FiltersByLifecycleState()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        context.Domains.AddRange(
            CreateTestDomain(tenantId, "verified.example.com", DomainType.Alias, DomainLifecycleState.Verified),
            CreateTestDomain(tenantId, "draft.example.com", DomainType.Alias, DomainLifecycleState.Draft),
            CreateTestDomain(tenantId, "pending.example.com", DomainType.Alias, DomainLifecycleState.PendingVerification)
        );
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainListAsync(tenantId, 1, 20, null, null, DomainLifecycleState.Draft, null, null);

        Assert.Single(result.Items);
        Assert.Equal("draft.example.com", result.Items[0].HostName);
    }

    [Fact]
    public async Task GetDomainListAsync_SearchFiltersByHostName()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        context.Domains.AddRange(
            CreateTestDomain(tenantId, "shop.example.com"),
            CreateTestDomain(tenantId, "blog.example.com"),
            CreateTestDomain(tenantId, "shop.other.com")
        );
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainListAsync(tenantId, 1, 20, "shop", null, null, null, null);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetDomainListAsync_PaginatesCorrectly()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        for (int i = 0; i < 25; i++)
        {
            context.Domains.Add(CreateTestDomain(tenantId, $"domain{i}.example.com"));
        }
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainListAsync(tenantId, 2, 10, null, null, null, "hostname", "asc");

        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(10, result.Items.Count);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public async Task GetDomainListAsync_ExcludesOtherTenantDomains()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        context.Domains.AddRange(
            CreateTestDomain(tenantId, "mine.example.com"),
            CreateTestDomain(otherTenantId, "theirs.example.com")
        );
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainListAsync(tenantId, 1, 20, null, null, null, null, null);

        Assert.Single(result.Items);
        Assert.Equal("mine.example.com", result.Items[0].HostName);
    }

    [Fact]
    public async Task GetDomainListAsync_ReturnsEmptyPage_ForEmptyTenant()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var service = CreateService(context);
        var result = await service.GetDomainListAsync(tenantId, 1, 20, null, null, null, null, null);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_ReturnsCorrectCounts()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var primary = CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.Primary);
        var alias = CreateTestDomain(tenantId, "www.example.com", DomainType.Alias, DomainLifecycleState.Verified);
        var pending = CreateTestDomain(tenantId, "pending.example.com", DomainType.Alias, DomainLifecycleState.PendingVerification);
        var draft = CreateTestDomain(tenantId, "draft.example.com", DomainType.Alias, DomainLifecycleState.Draft);

        context.Domains.AddRange(primary, alias, pending, draft);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDashboardSummaryAsync(tenantId);

        Assert.Equal(4, result.TotalDomains);
        Assert.Equal(1, result.PendingVerification);
        Assert.Equal(1, result.DraftCount);
        Assert.NotNull(result.PrimaryDomain);
        Assert.Equal("shop.example.com", result.PrimaryDomain.HostName);
        Assert.NotEmpty(result.RecentDomains);
    }

    [Fact]
    public async Task GetDomainDetailAsync_ReturnsNull_ForNonExistentDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var service = CreateService(context);
        var result = await service.GetDomainDetailAsync(tenantId, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDomainDetailAsync_ReturnsDetail_ForExistingDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var domain = CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainDetailAsync(tenantId, domain.Id);

        Assert.NotNull(result);
        Assert.Equal("shop.example.com", result!.HostName);
        Assert.Equal(DomainType.Primary, result!.DomainType);
        Assert.True(result!.IsVerified);
    }

    [Fact]
    public async Task GetDomainVerificationAsync_ReturnsVerificationDetails()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var domain = CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainVerificationAsync(tenantId, domain.Id);

        Assert.NotNull(result);
        Assert.Equal(domain.Id, result!.DomainId);
        Assert.Equal("shop.example.com", result.HostName);
        Assert.Equal("shop.example.com", result.NormalizedHostName);
        Assert.Equal(VerificationMethod.DnsTxt, result.Method);
        Assert.Equal("TXT", result.DnsRecordType);
        Assert.Equal("_shopixy-verification.shop.example.com", result.DnsRecordName);
        Assert.Equal("_shopixy-verification", result.DnsRecordHost);
        Assert.Equal(domain.VerificationToken, result.DnsRecordValue);
        Assert.Null(result.HttpVerificationUrl);
        Assert.True(result!.VerifiedAtUtc.HasValue);
    }

    [Fact]
    public async Task GetDomainVerificationAsync_ReturnsCnameInstructions_WhenMethodIsCname()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var domain = CreateTestDomain(tenantId, "cname.example.com", DomainType.Primary, DomainLifecycleState.PendingVerification);
        domain.VerificationMethod = VerificationMethod.DnsCname;
        domain.VerificationToken = "shopixy-domain-verify-cname-token";
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainVerificationAsync(tenantId, domain.Id);

        Assert.NotNull(result);
        Assert.Equal(VerificationMethod.DnsCname, result!.Method);
        Assert.Equal("CNAME", result.DnsRecordType);
        Assert.Equal("_shopixy-verification.cname.example.com", result.DnsRecordName);
        Assert.Equal("_shopixy-verification", result.DnsRecordHost);
        Assert.Equal("verify-cname-token.domains.shopixy.com", result.DnsRecordValue);
        Assert.Null(result.HttpVerificationUrl);
    }

    [Fact]
    public async Task GetDomainVerificationAsync_ReturnsHttpInstructions_WhenMethodIsHttpFile()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var domain = CreateTestDomain(tenantId, "http.example.com", DomainType.Primary, DomainLifecycleState.PendingVerification);
        domain.VerificationMethod = VerificationMethod.HttpFile;
        domain.VerificationToken = "shopixy-domain-verify-http-token";
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainVerificationAsync(tenantId, domain.Id);

        Assert.NotNull(result);
        Assert.Equal(VerificationMethod.HttpFile, result!.Method);
        Assert.Null(result.DnsRecordType);
        Assert.Null(result.DnsRecordName);
        Assert.Null(result.DnsRecordHost);
        Assert.Equal("shopixy-domain-verify-http-token", result.DnsRecordValue);
        Assert.Equal(
            "https://http.example.com/.well-known/shopixy-domain-verification.txt",
            result.HttpVerificationUrl);
    }

    [Fact]
    public async Task GetDomainSslAsync_ReturnsPending_WhenNoCertificateExists()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var domain = CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainSslAsync(tenantId, domain.Id);

        Assert.NotNull(result);
        Assert.Equal(CertificateStatus.Pending, result!.Status);
    }

    [Fact]
    public async Task GetDomainSslAsync_ReturnsNull_ForNonExistentDomain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var service = CreateService(context);
        var result = await service.GetDomainSslAsync(tenantId, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDomainSslAsync_ReturnsCertificateDetails()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var domain = CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.SslActive);
        context.Domains.Add(domain);

        var cert = new SslCertificate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DomainId = domain.Id,
            Provider = ProviderType.LetsEncrypt,
            Status = CertificateStatus.Active,
            Thumbprint = "ABC123",
            NotBeforeUtc = DateTime.UtcNow.AddDays(-30),
            NotAfterUtc = DateTime.UtcNow.AddDays(60),
            CreatedAt = DateTime.UtcNow.AddDays(-30),
        };
        context.SslCertificates.Add(cert);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainSslAsync(tenantId, domain.Id);

        Assert.NotNull(result);
        Assert.Equal(CertificateStatus.Active, result!.Status);
        Assert.Equal("ABC123", result!.Thumbprint);
        Assert.False(result!.ExpiringSoon);
    }

    [Fact]
    public async Task GetDomainSslAsync_IgnoresCertificateWithoutExactTenantBinding()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);
        var domain = CreateTestDomain(tenantId, "strict-tenant.example.com", DomainType.Alias, DomainLifecycleState.SslActive);
        context.Domains.Add(domain);
        context.SslCertificates.Add(new SslCertificate
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Empty,
            DomainId = domain.Id,
            Provider = ProviderType.LetsEncrypt,
            Status = CertificateStatus.Active,
            Generation = 1,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context).GetDomainSslAsync(tenantId, domain.Id);

        Assert.NotNull(result);
        Assert.Equal(CertificateStatus.Pending, result!.Status);
        Assert.Null(result.CertificateId);
    }

    [Fact]
    public async Task GetDomainRedirectsAsync_ReturnsRedirectSources()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var primary = CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.Primary);
        var redirect = CreateTestDomain(tenantId, "redirect.example.com", DomainType.Redirect, DomainLifecycleState.SslActive);
        redirect.RedirectType = RedirectType.Permanent301;
        redirect.RedirectTargetDomainId = primary.Id;

        context.Domains.AddRange(primary, redirect);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainRedirectsAsync(tenantId, primary.Id, default);

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("redirect.example.com", result[0].Source);
        Assert.Equal("shop.example.com", result[0].Target);
        Assert.Equal(RedirectType.Permanent301, result[0].Type);
    }

    [Fact]
    public async Task GetDomainHistoryAsync_ReturnsAuditEntries()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var domain = CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);

        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainVerified",
            ActorType = ActorType.Merchant,
            AfterSnapshotJson = "{}",
            TimestampUtc = DateTime.UtcNow,
        };
        context.DomainAuditEntries.Add(auditEntry);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetDomainHistoryAsync(tenantId, domain.Id, 1, 50, default);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("DomainVerified", result.Items[0].Action);
    }

    [Fact]
    public async Task GetActivitySummaryAsync_ReturnsRecentActivity()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateInMemoryContext(tenantId);

        var domain = CreateTestDomain(tenantId, "shop.example.com", DomainType.Primary, DomainLifecycleState.Primary);
        context.Domains.Add(domain);

        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainVerified",
            ActorType = ActorType.System,
            AfterSnapshotJson = "{}",
            TimestampUtc = DateTime.UtcNow,
        };
        context.DomainAuditEntries.Add(auditEntry);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetActivitySummaryAsync(tenantId, 20, default);

        Assert.NotNull(result);
        Assert.Equal(1, result.RecentActivity.Count);
        Assert.Equal("DomainVerified", result.RecentActivity[0].EventType);
    }

    [Fact]
    public async Task GetDomainListAsync_ReturnsEmpty_ForEmptyTenantId()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid());

        var service = CreateService(context);
        var result = await service.GetDomainListAsync(Guid.Empty, 1, 20, null, null, null, null, null);

        Assert.Empty(result.Items);
    }
}
