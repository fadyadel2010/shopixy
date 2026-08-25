using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MassTransit;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Governance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Events;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.SuperAdmin;

public sealed class UserGovernanceServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserGovernanceService _service;
    private readonly Mock<IEventPublisher> _publishEndpoint;
    private readonly Guid _superAdminId;
    private const string SuperAdminEmail = "owner@commercehub.test";

    public UserGovernanceServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _publishEndpoint = new Mock<IEventPublisher>();
        _publishEndpoint.SetReturnsDefault(Task.CompletedTask);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        var autoHealingQueue = new RecordingAutoHealingQueue();
        var vendorVisibilityService = new CommerceHubVendorVisibilityService(
            _dbContext,
            _publishEndpoint.Object,
            autoHealingQueue,
            Mock.Of<PointOfSale.Core.Interfaces.Marketplace.IMarketplaceEventOutboxService>(),
            NullLogger<CommerceHubVendorVisibilityService>.Instance);

        _service = new UserGovernanceService(
            _dbContext,
            httpContextAccessor,
            new PointOfSale.API.Services.CommerceHub.Finance.CommerceHubVendorDebtService(
                _dbContext,
                Options.Create(new CommerceHubVendorDebtOptions())),
            vendorVisibilityService,
            new RecordingGovernanceCacheInvalidationService(),
            NullLogger<UserGovernanceService>.Instance);

        var superAdmin = SuperAdminUser.Create(
            SuperAdminEmail,
            "hash",
            isSystem: false,
            DateTime.UtcNow);
        _superAdminId = superAdmin.Id;

        _dbContext.SuperAdmins.Add(superAdmin);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task ApplyActionAsync_WhenCustomerSuspended_ShouldBlockCheckout_AndWriteAudit()
    {
        var customer = CommerceHubCustomer.Create("customer@test.local", "Checkout Customer", "01000000001", "hash");
        _dbContext.CommerceHubCustomers.Add(customer);
        await _dbContext.SaveChangesAsync();

        var details = await _service.ApplyActionAsync(
            customer.Id,
            UserGovernanceRole.Customer,
            UserGovernanceStatus.Suspended,
            "Refund abuse investigation.",
            _superAdminId,
            SuperAdminEmail);

        var exception = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.EnsureCustomerCheckoutAllowedAsync(customer.Id));

        var auditLog = await _dbContext.UserGovernanceAuditLogs.SingleAsync();

        Assert.Equal("Suspended", details.Status);
        Assert.Equal("CUSTOMER_SUSPENDED", exception.Code);
        Assert.Equal("SUSPEND", auditLog.ActionType);
        Assert.Equal(customer.Id, auditLog.TargetUserId);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenCustomerBanned_ShouldBlockLogin()
    {
        var customer = CommerceHubCustomer.Create("banned@test.local", "Banned Customer", "01000000002", "hash");
        _dbContext.CommerceHubCustomers.Add(customer);
        await _dbContext.SaveChangesAsync();

        await _service.ApplyActionAsync(
            customer.Id,
            UserGovernanceRole.Customer,
            UserGovernanceStatus.Banned,
            "Confirmed fraud ring membership.",
            _superAdminId,
            SuperAdminEmail);

        var exception = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.EnsureCustomerLoginAllowedAsync(customer.Id));

        Assert.Equal("CUSTOMER_BANNED", exception.Code);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenVendorSuspended_ShouldHidePublishedListings_AndBlockNewOrders()
    {
        var vendor = await CreateVendorAsync("Suspended Visibility Vendor", "suspended-visibility@test.local");
        var listing = await CreatePublishedListingAsync(vendor.Id);

        var details = await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Suspended,
            "Chargeback review in progress.",
            _superAdminId,
            SuperAdminEmail);

        var exception = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.EnsureVendorOrderAcceptanceAllowedAsync(vendor.Id));

        var persistedListing = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == listing.Id);

        Assert.Equal("Suspended", details.Status);
        Assert.Equal("VENDOR_SUSPENDED", exception.Code);
        Assert.False(persistedListing.IsPublished);
        Assert.True(persistedListing.RestorePublicationOnActivate);
        _publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(
                It.IsAny<CommerceHubListingUnpublishedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenVendorBanned_ShouldBlockLogin_AndHidePublishedListings()
    {
        var vendor = await CreateVendorAsync("Banned Vendor", "banned-vendor@test.local");
        var listing = await CreatePublishedListingAsync(vendor.Id);

        await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Banned,
            "Confirmed platform fraud.",
            _superAdminId,
            SuperAdminEmail);

        var exception = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.EnsureTenantLoginAllowedAsync(vendor.Id));

        var persistedListing = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == listing.Id);

        Assert.Equal("VENDOR_BANNED", exception.Code);
        Assert.False(persistedListing.IsPublished);
        Assert.True(persistedListing.RestorePublicationOnActivate);
        _publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(
                It.IsAny<CommerceHubListingUnpublishedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenVendorSuspended_ShouldWriteVisibilitySnapshotMetadata()
    {
        var vendor = await CreateVendorAsync("Snapshot Vendor", "snapshot-vendor@test.local");
        var listing = await CreatePublishedListingAsync(vendor.Id);

        await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Suspended,
            "Marketplace risk review.",
            _superAdminId,
            SuperAdminEmail);

        var auditLog = await _dbContext.UserGovernanceAuditLogs.SingleAsync();
        using var metadata = JsonDocument.Parse(auditLog.Metadata!);

        var visibility = metadata.RootElement.GetProperty("visibility");
        var listingSnapshot = visibility.GetProperty("listingSnapshot");
        var snapshotItem = listingSnapshot[0];
        var cacheInvalidation = metadata.RootElement.GetProperty("cacheInvalidation");

        Assert.Equal(1, visibility.GetProperty("hiddenCount").GetInt32());
        Assert.Equal(1, visibility.GetProperty("searchVerificationEnqueuedCount").GetInt32());
        Assert.Equal(listing.Id, snapshotItem.GetProperty("listingId").GetGuid());
        Assert.True(snapshotItem.GetProperty("wasPublished").GetBoolean());
        Assert.Equal(vendor.Id, cacheInvalidation.GetProperty("vendorId").GetGuid());
        Assert.Equal(1, cacheInvalidation.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task ApplyActionAsync_WhenVendorReactivated_ShouldRestorePreviouslyPublishedListings()
    {
        var vendor = await CreateVendorAsync("Reactivated Vendor", "reactivated-vendor@test.local");
        var listing = await CreatePublishedListingAsync(vendor.Id);

        await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Suspended,
            "Temporary review hold.",
            _superAdminId,
            SuperAdminEmail);

        _publishEndpoint.Invocations.Clear();

        await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Active,
            "Review cleared.",
            _superAdminId,
            SuperAdminEmail);

        var persistedListing = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == listing.Id);

        Assert.True(persistedListing.IsPublished);
        Assert.False(persistedListing.RestorePublicationOnActivate);
        _publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(
                It.IsAny<CommerceHubListingUpdatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenVendorReactivated_ShouldNotRestoreListingWithoutCurrentProductPublicationAuthority()
    {
        var vendor = await CreateVendorAsync("Stale Restore Vendor", "stale-restore@test.local");
        var listing = await CreatePublishedListingAsync(vendor.Id);

        await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Suspended,
            "Temporary review hold.",
            _superAdminId,
            SuperAdminEmail);

        var product = await _dbContext.Products
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == listing.ProductId);
        product.IsOnlineVisible = false;
        product.PublishStatus = PublishStatus.Draft;
        await _dbContext.SaveChangesAsync();

        _publishEndpoint.Invocations.Clear();

        await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Active,
            "Vendor identity restored, product remains unpublished.",
            _superAdminId,
            SuperAdminEmail);

        var persistedListing = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == listing.Id);

        Assert.False(persistedListing.IsPublished);
        Assert.False(persistedListing.RestorePublicationOnActivate);
        _publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(
                It.IsAny<CommerceHubListingUpdatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenVendorReactivated_ShouldRestoreOnlyListingsThatWerePreviouslyPublished()
    {
        var vendor = await CreateVendorAsync("Selective Restore Vendor", "selective-restore@test.local");
        var publishedListing = await CreatePublishedListingAsync(vendor.Id);
        var unpublishedListing = await CreatePublishedListingAsync(vendor.Id, isPublished: false);

        await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Suspended,
            "Temporary suspension.",
            _superAdminId,
            SuperAdminEmail);

        _publishEndpoint.Invocations.Clear();

        await _service.ApplyActionAsync(
            vendor.Id,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.Active,
            "Vendor cleared.",
            _superAdminId,
            SuperAdminEmail);

        var listings = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == vendor.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var restoredListing = listings.Single(x => x.Id == publishedListing.Id);
        var untouchedListing = listings.Single(x => x.Id == unpublishedListing.Id);

        Assert.True(restoredListing.IsPublished);
        Assert.False(restoredListing.RestorePublicationOnActivate);
        Assert.False(untouchedListing.IsPublished);
        Assert.False(untouchedListing.RestorePublicationOnActivate);
    }

    [Fact]
    public async Task GetUserDetailsAsync_WhenVendorDebtExceedsThreshold_ShouldSetHighRisk_AndAutoFinancialLock()
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Debt Locked Vendor",
            ContactEmail = "vendor@test.local",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        });

        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                CommerceHubFinancialTransactionType.Commission,
                CommerceHubFinancialTransactionDirection.Debit,
                1200m,
                "EGP",
                CommerceHubFinancialReferenceType.Order,
                Guid.NewGuid(),
                "Outstanding COD commission debt.",
                createdBy: _superAdminId));

        await _dbContext.SaveChangesAsync();

        var details = await _service.GetUserDetailsAsync(vendorId, UserGovernanceRole.Vendor);
        var payoutException = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.EnsureVendorPayoutAllowedAsync(vendorId));

        Assert.NotNull(details);
        Assert.Equal("High", details!.Risk.RiskLevel);
        Assert.Equal("FinanciallyLocked", details.Status);
        Assert.Equal("VENDOR_FINANCIAL_LOCKED", payoutException.Code);
    }

    [Fact]
    public async Task EnsureRegistrationAllowedAsync_WhenEmailIsBlacklisted_ShouldReject()
    {
        _dbContext.UserGovernanceBlacklistEntries.Add(new UserGovernanceBlacklistEntry
        {
            Id = Guid.NewGuid(),
            Type = UserGovernanceBlacklistType.Email,
            ValueNormalized = "blocked@commercehub.test",
            ValueDisplay = "blocked@commercehub.test",
            Reason = "Known fraud pattern.",
            IsActive = true,
            CreatedByAdminId = _superAdminId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.EnsureRegistrationAllowedAsync(
                "Blocked@CommerceHub.Test",
                "01000000003",
                null,
                null));

        Assert.Equal("BLACKLISTED_REGISTRATION", exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenVendorFinancialLocked_ShouldBlockPayout_AndWriteAudit()
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Manual Review Vendor",
            ContactEmail = "manual-lock@test.local",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var details = await _service.ApplyActionAsync(
            vendorId,
            UserGovernanceRole.Vendor,
            UserGovernanceStatus.FinanciallyLocked,
            "Manual finance review.",
            _superAdminId,
            SuperAdminEmail);

        var exception = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.EnsureVendorPayoutAllowedAsync(vendorId));

        var profile = await _dbContext.UserGovernanceProfiles.SingleAsync();
        var auditLog = await _dbContext.UserGovernanceAuditLogs.SingleAsync();

        Assert.Equal("FinanciallyLocked", details.Status);
        Assert.Equal(UserGovernanceStatus.FinanciallyLocked, profile.Status);
        Assert.Equal("VENDOR_FINANCIAL_LOCKED", exception.Code);
        Assert.Equal("FINANCIAL_LOCK", auditLog.ActionType);
        Assert.Equal("Manual finance review.", auditLog.Reason);
        Assert.Equal(vendorId, auditLog.TargetUserId);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenSuperAdminAttemptsToBanSelf_ShouldReject()
    {
        var exception = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.ApplyActionAsync(
                _superAdminId,
                UserGovernanceRole.Admin,
                UserGovernanceStatus.Banned,
                "Improper self-ban attempt.",
                _superAdminId,
                SuperAdminEmail));

        Assert.Equal("SELF_BAN_FORBIDDEN", exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task ApplyActionAsync_WhenSuperAdminSuspended_ShouldRevokeCurrentPlatformSessionAndBlockLogin()
    {
        var superAdmin = await _dbContext.SuperAdmins.SingleAsync(item => item.Id == _superAdminId);
        superAdmin.SetRefreshToken("hash", DateTime.UtcNow.AddDays(1));
        var tokenVersionBefore = superAdmin.TokenVersion;
        await _dbContext.SaveChangesAsync();

        await _service.ApplyActionAsync(
            _superAdminId,
            UserGovernanceRole.Admin,
            UserGovernanceStatus.Suspended,
            "Operational suspension test.",
            Guid.NewGuid(),
            "reviewer@commercehub.test");

        var persisted = await _dbContext.SuperAdmins.SingleAsync(item => item.Id == _superAdminId);
        Assert.Null(persisted.RefreshTokenHash);
        Assert.Null(persisted.RefreshTokenExpiresAtUtc);
        Assert.True(persisted.TokenVersion > tokenVersionBefore);

        var exception = await Assert.ThrowsAsync<UserGovernanceException>(() =>
            _service.EnsureAdminLoginAllowedAsync(_superAdminId));
        Assert.Equal("SUPER_ADMIN_SUSPENDED", exception.Code);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<Tenant> CreateVendorAsync(string name, string email)
    {
        var vendor = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            ContactEmail = email,
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Tenants.Add(vendor);
        _dbContext.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = vendor.Id,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Active,
            IsKycVerified = true,
            KycVerifiedAtUtc = DateTime.UtcNow,
            KycVerifiedByAdminId = _superAdminId,
            KycVerificationReason = "Test vendor verification.",
            IsMarketplaceApproved = true,
            MarketplaceApprovedAtUtc = DateTime.UtcNow,
            MarketplaceApprovedByAdminId = _superAdminId,
            MarketplaceApprovalReason = "Test marketplace approval.",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        return vendor;
    }

    private async Task<CommerceHubListing> CreatePublishedListingAsync(Guid vendorId, bool isPublished = true)
    {
        var categoryId = Guid.NewGuid();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            Name = "Governed Product",
            CategoryId = categoryId,
            PurchasePrice = 50m,
            SalePrice = 120m,
            OnlinePrice = 110m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            Unit = "Piece",
            CreatedAt = DateTime.UtcNow
        };

        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            ProductId = product.Id,
            Price = 120m,
            OnlinePrice = 110m,
            CostPrice = 60m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var listing = CommerceHubListing.Create(
            vendorId,
            product.Id,
            variant.Id,
            categoryId,
            null,
            "{\"brand\":\"governed\"}");

        if (!isPublished)
        {
            listing.Unpublish();
        }

        _dbContext.CommerceHubCategories.Add(new CommerceHubCategory
        {
            Id = categoryId,
            Name = "Governed Category",
            Slug = $"governed-{categoryId:N}",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        _dbContext.Products.Add(product);
        _dbContext.ProductVariants.Add(variant);
        _dbContext.CommerceHubListings.Add(listing);
        await _dbContext.SaveChangesAsync();

        return listing;
    }

    private sealed class RecordingAutoHealingQueue : ICommerceHubAutoHealingQueue
    {
        public int Enqueue(CommerceHubAutoHealingTask task) => task.ListingIds.Count;
    }

    private sealed class RecordingGovernanceCacheInvalidationService : ICommerceHubGovernanceCacheInvalidationService
    {
        private long _version;

        public ValueTask<string> GetVisibilityVersionTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_version.ToString());

        public Task<CommerceHubGovernanceCacheInvalidationResult> InvalidateVisibilityAsync(
            Guid? vendorId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            _version++;
            return Task.FromResult(new CommerceHubGovernanceCacheInvalidationResult
            {
                VendorId = vendorId,
                Version = _version,
                UsedRedis = false,
                UsedFallback = true,
                FailureReason = "Test double"
            });
        }
    }
}
