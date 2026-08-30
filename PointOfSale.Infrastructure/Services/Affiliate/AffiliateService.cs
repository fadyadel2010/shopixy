using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.Core.DTOs.Affiliate;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Services.Affiliate;

/// <summary>
/// AF-1 Domain Service + AF-2.5 Observability + AF-19 Global Marketplace: Write-side implementation for the Affiliate System.
///
/// CRITICAL RULES:
/// - Commission is recorded ONLY when OrderItem.PaidAmount > 0
/// - No Order → No Commission
/// - No Attribution → No Commission
/// - Idempotent: catches Postgres unique-violation (23505) as success
///
/// AF-19 GLOBAL MARKETPLACE:
/// - Affiliates are global (no tenant binding)
/// - ReferralCode is globally unique
/// - StoreTenantId tracks which store owes the commission
/// - Affiliate can earn from multiple stores
///
/// AF-2.5 OBSERVABILITY:
/// - All skip/success/idempotent paths emit metrics via AffiliateTelemetry
/// - CorrelationId flows from incoming events to outgoing events (set by consumer scope)
/// - No PII logged (no customer names, emails, or payment card data)
/// </summary>
public class AffiliateService : IAffiliateService
{
    private const int AttributionRecoveryBatchSize = 25;

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<AffiliateService> _logger;
    private readonly IAffiliatePayoutService _payoutService;
    private readonly IAffiliateSettlementOrchestrator _orchestrator;
    private readonly AffiliateSettlementOptions _settlementOptions;

    public AffiliateService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        AffiliateTelemetry telemetry,
        ILogger<AffiliateService> logger,
        IAffiliatePayoutService payoutService,
        IAffiliateSettlementOrchestrator orchestrator,
        IOptions<AffiliateSettlementOptions> settlementOptions)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _telemetry = telemetry;
        _logger = logger;
        _payoutService = payoutService;
        _orchestrator = orchestrator;
        _settlementOptions = settlementOptions.Value;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    // ═══════════════════════════════════════════════════════════════════
    // REGISTRATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-19: GLOBAL MARKETPLACE - Registration no longer requires tenant context.
    /// TenantId parameter kept for backward compatibility but becomes audit-only.
    /// </summary>
    public async Task<AffiliateAccount> RegisterAffiliateAccountAsync(
        Guid userId, string referralCode, string displayName,
        CancellationToken ct = default)
    {
        var tenantId = TenantId; // AF-19: Legacy, kept for audit trail only

        // Guard: user must exist
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId, ct);
        if (!userExists)
            throw new InvalidOperationException($"User {userId} not found");

        // AF-19: GLOBAL MARKETPLACE - Remove tenant check, affiliates are now global
        // Guard: user must not already have an affiliate account
        var existing = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.UserId == userId, ct);
        if (existing)
            throw new InvalidOperationException($"User {userId} already has an affiliate account");

        // AF-19: GLOBAL MARKETPLACE - Check referral code globally (not per-tenant)
        var codeNormalized = referralCode.ToUpperInvariant();

        var codeTaken = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.ReferralCode == codeNormalized, ct);
        if (codeTaken)
            throw new InvalidOperationException($"Referral code '{codeNormalized}' is already taken globally");

        var account = AffiliateAccount.Create(tenantId, userId, referralCode, displayName);
        _context.AffiliateAccounts.Add(account);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[AF-19:GlobalAffiliate] Affiliate account {AccountId} created for User {UserId} with global code {Code}",
            account.Id, userId, account.ReferralCode);

        return account;
    }

    public async Task ActivateAffiliateAsync(Guid affiliateAccountId, CancellationToken ct = default)
    {
        var account = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == affiliateAccountId && !a.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Affiliate account {affiliateAccountId} not found");

        account.Activate();
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Affiliate {AccountId} activated", affiliateAccountId);
    }

    public async Task SuspendAffiliateAsync(Guid affiliateAccountId, string reason, CancellationToken ct = default)
    {
        var account = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == affiliateAccountId && !a.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Affiliate account {affiliateAccountId} not found");

        var nowUtc = DateTime.UtcNow;
        account.Suspend(reason);
        await RevokeAffiliateAccountSessionsAsync(affiliateAccountId, nowUtc, ct);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Affiliate {AccountId} suspended: {Reason}", affiliateAccountId, reason);
    }

    public async Task CloseAffiliateAsync(Guid affiliateAccountId, string reason, CancellationToken ct = default)
    {
        var account = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == affiliateAccountId && !a.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Affiliate account {affiliateAccountId} not found");

        var nowUtc = DateTime.UtcNow;
        account.Close(reason);
        await RevokeAffiliateAccountSessionsAsync(affiliateAccountId, nowUtc, ct);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Affiliate {AccountId} closed: {Reason}", affiliateAccountId, reason);
    }

    private async Task RevokeAffiliateAccountSessionsAsync(
        Guid affiliateAccountId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var users = await _context.AffiliateUsers
            .IgnoreQueryFilters()
            .Where(user => user.AffiliateAccountId == affiliateAccountId && !user.IsDeleted)
            .ToListAsync(ct);

        foreach (var user in users)
        {
            user.RevokeSessions(nowUtc);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // PRODUCT POLICY
    // ═══════════════════════════════════════════════════════════════════

    public async Task<AffiliateProductPolicy> EnableAffiliateForProductAsync(
        Guid productId, decimal commissionRate, decimal? maxPerItem = null,
        CancellationToken ct = default)
    {
        var tenantId = TenantId;

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, ct);
        if (product == null)
            throw new InvalidOperationException($"Product {productId} not found");

        // Upsert: check if policy already exists
        var existing = await _context.AffiliateProductPolicies
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.TenantId == tenantId, ct);

        // Product lifecycle gates entry/re-entry only. Editing an already-enabled
        // Affiliate policy must not become a hidden Product lifecycle mutation rule.
        if (existing == null || !existing.IsEnabled)
        {
            await EnsureProductEligibleForAffiliateAsync(product, ct);
        }

        if (existing != null)
        {
            existing.UpdateRate(commissionRate, maxPerItem);
            
            // AF-30A: Enforce category requirement before enabling
            if (existing.AffiliateCategoryId == null)
            {
                _logger.LogWarning(
                    "[AF-30A:CategoryRequired] Cannot enable affiliate for Product {ProductId} without category",
                    productId);
                throw new InvalidOperationException(
                    "Affiliate category is required. Assign a category before enabling affiliate marketing.");
            }
            
            // Validate the entire category lineage, not only the selected node.
            var category = await GetSelectableAffiliateCategoryAsync(
                existing.AffiliateCategoryId.Value,
                ct);
            
            existing.Enable();
            await PublishAffiliateProductPolicyChangedAsync(existing, category, ct);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[AF-30A] Affiliate policy updated for Product {ProductId}",
                productId);
            return existing;
        }

        // AF-30A: New policies cannot be created without category
        _logger.LogWarning(
            "[AF-30A:CategoryRequired] Cannot create affiliate policy for Product {ProductId} without category",
            productId);
        throw new InvalidOperationException(
            "Affiliate category is required. Use UpdateCategoryForProductAsync() first to assign a category.");
    }

    public async Task<AffiliatePolicyWithCategoryDetails> UpdateCategoryForProductAsync(
        Guid productId, Guid? categoryId, CancellationToken ct = default)
    {
        var tenantId = TenantId;

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Product {productId} not found");

        var existing = await _context.AffiliateProductPolicies
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.TenantId == tenantId, ct);

        if (existing == null)
            throw new InvalidOperationException($"Affiliate policy for Product {productId} not found");

        // AF-30A: Prevent removing category while affiliate is enabled
        if (existing.IsEnabled && categoryId == null)
        {
            _logger.LogWarning(
                "[AF-30A:CategoryRequired] Cannot remove category from Product {ProductId} while affiliate is enabled",
                productId);
            throw new InvalidOperationException(
                "Cannot remove affiliate category while affiliate marketing is enabled. " +
                "Disable affiliate first or assign a different category.");
        }

        // AF-30: Validate category if provided and fetch details for event snapshot
        CategoryDetails? categoryDetails = null;
        if (categoryId.HasValue)
        {
            var category = await GetSelectableAffiliateCategoryAsync(categoryId.Value, ct);

            // Fetch parent details if subcategory
            string? parentName = null;
            string? parentSlug = null;
            if (category.ParentId.HasValue)
            {
                var parent = await _context.AffiliateCategories
                    .Where(c => c.Id == category.ParentId.Value)
                    .Select(c => new { c.Name, c.Slug })
                    .FirstOrDefaultAsync(ct);

                if (parent != null)
                {
                    parentName = parent.Name;
                    parentSlug = parent.Slug;
                }
            }

            categoryDetails = new CategoryDetails
            {
                CategoryId = category.Id,
                CategoryName = category.Name,
                CategorySlug = category.Slug,
                ParentCategoryName = parentName,
                ParentCategorySlug = parentSlug
            };
        }

        existing.UpdateCategory(categoryId);
        await PublishAffiliateProductPolicyChangedAsync(existing, categoryDetails, ct);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[AF-30] Affiliate category updated for Product {ProductId}",
            productId);

        return new AffiliatePolicyWithCategoryDetails
        {
            Policy = existing,
            CategoryDetails = categoryDetails
        };
    }

    // AF-30A: Atomic enable with category assignment
    public async Task<AffiliatePolicyWithCategoryDetails> EnableAffiliateWithCategoryAsync(
        Guid productId, decimal commissionRate, Guid categoryId, decimal? maxPerItem = null,
        CancellationToken ct = default)
    {
        var tenantId = TenantId;
        var ownsTransaction = _context.Database.CurrentTransaction == null;
        await using IDbContextTransaction? transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        try
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, ct)
                ?? throw new InvalidOperationException($"Product {productId} not found");

            var existing = await _context.AffiliateProductPolicies
                .FirstOrDefaultAsync(p => p.ProductId == productId && p.TenantId == tenantId, ct);

            // Product eligibility gates only entry/re-entry into Affiliate participation.
            // Affiliate-owned edits to an enabled policy must not depend on Product state.
            if (existing == null || !existing.IsEnabled)
            {
                await EnsureProductEligibleForAffiliateAsync(product, ct);
            }

            var category = await GetSelectableAffiliateCategoryAsync(categoryId, ct);
            var categoryDetails = await BuildCategoryDetailsAsync(category, ct);

            if (existing != null)
            {
                existing.UpdateRate(commissionRate, maxPerItem);
                existing.UpdateCategory(categoryId);
                existing.Enable();
                await PublishAffiliateProductPolicyChangedAsync(existing, categoryDetails, ct);
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[AF-30A] Affiliate enabled for Product {ProductId}",
                    productId);

                if (ownsTransaction && transaction != null)
                {
                    await transaction.CommitAsync(ct);
                }

                return new AffiliatePolicyWithCategoryDetails
                {
                    Policy = existing,
                    CategoryDetails = categoryDetails
                };
            }

            // Create new policy with category
            var policy = AffiliateProductPolicy.Create(tenantId, productId, commissionRate, maxPerItem, categoryId);
            _context.AffiliateProductPolicies.Add(policy);
            await PublishAffiliateProductPolicyChangedAsync(policy, categoryDetails, ct);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[AF-30A] Affiliate policy created for Product {ProductId}",
                productId);

            if (ownsTransaction && transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            return new AffiliatePolicyWithCategoryDetails
            {
                Policy = policy,
                CategoryDetails = categoryDetails
            };
        }
        catch (Exception ex)
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.RollbackAsync(ct);
            }

            if (IsAffiliatePolicyWriteConflict(ex))
            {
                throw new DbUpdateConcurrencyException(
                    "Affiliate policy changed concurrently. Reload and retry the operation.", ex);
            }

            throw;
        }
    }

    public async Task<AffiliateProductPolicy?> DisableAffiliateForProductAsync(
        Guid productId, CancellationToken ct = default)
    {
        var tenantId = TenantId;
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Product {productId} not found");

        var existing = await _context.AffiliateProductPolicies
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.TenantId == tenantId, ct);

        if (existing == null)
        {
            // GAP FIX: No persisted policy exists, so there's nothing to disable.
            // Do NOT publish an event with provisional values (CommissionRate=0m, null category).
            // Projections already reflect the absence of affiliate enablement.
            _logger.LogInformation(
                "Affiliate disable skipped for Product {ProductId} — no policy exists",
                productId);
            return null;
        }

        existing.Disable();
        await PublishAffiliateProductPolicyChangedAsync(existing, categoryDetails: null, ct);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Affiliate policy disabled for Product {ProductId}",
            productId);
        return existing;
    }

    public async Task<AffiliateProductPolicy> UpdateTemplateForProductAsync(
        Guid productId, string templateType, CancellationToken ct = default)
    {
        var tenantId = TenantId;

        var existing = await _context.AffiliateProductPolicies
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.TenantId == tenantId, ct);

        if (existing == null)
            throw new InvalidOperationException($"Affiliate policy for Product {productId} not found");

        existing.UpdateTemplate(templateType);
        if (!await _context.Products.AnyAsync(p => p.Id == productId && p.TenantId == tenantId, ct))
        {
            throw new InvalidOperationException($"Product {productId} not found");
        }

        await PublishAffiliateProductPolicyChangedAsync(existing, categoryDetails: null, ct);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Affiliate landing template updated to {TemplateType} for Product {ProductId}",
            existing.TemplateType, productId);

        return existing;
    }

    // ═══════════════════════════════════════════════════════════════════
    // ATTRIBUTION
    // ═══════════════════════════════════════════════════════════════════

    private async Task EnsureProductEligibleForAffiliateAsync(Product product, CancellationToken ct)
    {
        if (product.IsDeleted)
            throw new InvalidOperationException("Deleted products cannot be affiliate eligible.");

        if (!product.IsActive)
            throw new InvalidOperationException("Inactive products cannot be affiliate eligible.");

        if (product.PublishStatus != PublishStatus.Published)
            throw new InvalidOperationException("Only published products can be affiliate eligible.");

        if (!product.IsOnlineVisible)
            throw new InvalidOperationException("Hidden products cannot be affiliate eligible.");

        if (!await HasPositiveAffiliatePriceAsync(product, ct))
            throw new InvalidOperationException("Products must have a positive storefront price before affiliate eligibility can be enabled.");

        if (!await HasAvailableAffiliateInventoryAsync(product, ct))
            throw new InvalidOperationException("Products must be in stock or allow backorders before affiliate eligibility can be enabled.");
    }

    private async Task<bool> HasPositiveAffiliatePriceAsync(Product product, CancellationToken ct)
    {
        if (product.HasVariants)
        {
            return await _context.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .WhereCurrentSemanticOwner()
                .AnyAsync(variant =>
                    variant.TenantId == product.TenantId &&
                    variant.ProductId == product.Id &&
                    !variant.IsDeleted &&
                    variant.IsActive &&
                    ((variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0m) ||
                     (!variant.OnlinePrice.HasValue && variant.Price > 0m) ||
                     (variant.OnlinePrice.HasValue && variant.OnlinePrice.Value == 0m && variant.Price > 0m)),
                    ct);
        }

        return await _context.ProductUnits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(unit =>
                unit.ProductId == product.Id &&
                !unit.IsDeleted &&
                unit.IsDefault &&
                unit.SellingPrice.HasValue &&
                unit.SellingPrice.Value > 0m,
                ct);
    }

    private async Task<bool> HasAvailableAffiliateInventoryAsync(Product product, CancellationToken ct)
    {
        if (!product.TrackStock || product.AllowBackorder)
        {
            return true;
        }

        return await _context.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(stock =>
                stock.TenantId == product.TenantId &&
                !stock.IsDeleted &&
                stock.Variant != null &&
                stock.Variant.TenantId == product.TenantId &&
                stock.Variant.ProductId == product.Id &&
                !stock.Variant.IsDeleted &&
                stock.Variant.IsActive &&
                stock.Variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                stock.Variant.SemanticSupersededAtUtc == null &&
                stock.Variant.SemanticReplacementVariantId == null &&
                (product.HasVariants || stock.Variant.IsDefault) &&
                stock.Warehouse != null &&
                stock.Warehouse.TenantId == product.TenantId &&
                !stock.Warehouse.IsDeleted &&
                stock.Warehouse.IsActive &&
                stock.Quantity - stock.ReservedQuantity > 0m,
                ct);
    }

    private async Task PublishAffiliateProductPolicyChangedAsync(
        AffiliateProductPolicy policy,
        AffiliateCategory? category,
        CancellationToken ct)
    {
        var categoryDetails = category == null
            ? null
            : await BuildCategoryDetailsAsync(category, ct);

        await PublishAffiliateProductPolicyChangedAsync(policy, categoryDetails, ct);
    }

    private async Task PublishAffiliateProductPolicyChangedAsync(
        AffiliateProductPolicy policy,
        CategoryDetails? categoryDetails,
        CancellationToken ct)
    {
        if (!policy.IsEnabled)
        {
            await _eventPublisher.PublishAsync(new AffiliateProductPolicyChangedEvent
            {
                TenantId = policy.TenantId,
                ProductId = policy.ProductId,
                IsEnabled = false,
                CommissionRate = policy.CommissionRate,
                MaxCommissionPerItem = policy.MaxCommissionPerItem,
                TemplateType = policy.TemplateType,
                AffiliateCategoryId = null,
                AffiliateCategoryName = null,
                AffiliateCategorySlug = null,
                AffiliateParentCategoryName = null,
                AffiliateParentCategorySlug = null
            }, ct);
            return;
        }

        categoryDetails ??= await LoadCategoryDetailsAsync(policy.AffiliateCategoryId, ct);

        await _eventPublisher.PublishAsync(new AffiliateProductPolicyChangedEvent
        {
            TenantId = policy.TenantId,
            ProductId = policy.ProductId,
            IsEnabled = policy.IsEnabled,
            CommissionRate = policy.CommissionRate,
            MaxCommissionPerItem = policy.MaxCommissionPerItem,
            TemplateType = policy.TemplateType,
            AffiliateCategoryId = categoryDetails?.CategoryId ?? policy.AffiliateCategoryId,
            AffiliateCategoryName = categoryDetails?.CategoryName,
            AffiliateCategorySlug = categoryDetails?.CategorySlug,
            AffiliateParentCategoryName = categoryDetails?.ParentCategoryName,
            AffiliateParentCategorySlug = categoryDetails?.ParentCategorySlug
        }, ct);
    }

    private async Task<AffiliateCategory> GetSelectableAffiliateCategoryAsync(
        Guid categoryId,
        CancellationToken ct)
    {
        var category = await _context.AffiliateCategories
            .FirstOrDefaultAsync(candidate => candidate.Id == categoryId && !candidate.IsDeleted, ct);

        if (category == null)
        {
            throw new InvalidOperationException($"Affiliate category {categoryId} not found");
        }

        if (!category.IsActive)
        {
            throw new InvalidOperationException($"Affiliate category {categoryId} is not active");
        }

        if (category.Level == 1 && category.ParentId == null)
        {
            return category;
        }

        if (category.Level == 2 && category.ParentId.HasValue)
        {
            var liveRootExists = await _context.AffiliateCategories.AnyAsync(
                parent => parent.Id == category.ParentId.Value &&
                          !parent.IsDeleted &&
                          parent.IsActive &&
                          parent.Level == 1 &&
                          parent.ParentId == null,
                ct);

            if (liveRootExists)
            {
                return category;
            }
        }

        throw new InvalidOperationException(
            $"Affiliate category {categoryId} is not available because its hierarchy is inactive or invalid");
    }

    private async Task<CategoryDetails?> LoadCategoryDetailsAsync(Guid? categoryId, CancellationToken ct)
    {
        if (!categoryId.HasValue)
        {
            return null;
        }

        var category = await _context.AffiliateCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId.Value, ct);

        return category == null
            ? null
            : await BuildCategoryDetailsAsync(category, ct);
    }

    private async Task<CategoryDetails> BuildCategoryDetailsAsync(AffiliateCategory category, CancellationToken ct)
    {
        string? parentName = null;
        string? parentSlug = null;

        if (category.ParentId.HasValue)
        {
            var parent = await _context.AffiliateCategories
                .Where(c => c.Id == category.ParentId.Value)
                .Select(c => new { c.Name, c.Slug })
                .FirstOrDefaultAsync(ct);

            if (parent != null)
            {
                parentName = parent.Name;
                parentSlug = parent.Slug;
            }
        }

        return new CategoryDetails
        {
            CategoryId = category.Id,
            CategoryName = category.Name,
            CategorySlug = category.Slug,
            ParentCategoryName = parentName,
            ParentCategorySlug = parentSlug
        };
    }

    public async Task<AffiliateAttribution> AttributeOrderToAffiliateAsync(
        Guid orderId, string referralCode, AttributionSource source,
        Guid? expectedAffiliateAccountId = null,
        Guid? promotionLinkId = null,
        CancellationToken ct = default)
    {
        // CRITICAL: Use IgnoreQueryFilters — called from MassTransit consumer (no HTTP tenant context).
        // Derive TenantId from the Order entity itself, same pattern as RecordCommissionForOrderAsync.
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found");

        var tenantId = order.TenantId;

        // Guard: check for existing attribution
        var existingAttribution = await _context.AffiliateAttributions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.OrderId == orderId && !a.IsDeleted, ct);

        var codeNormalized = referralCode.Trim().ToUpperInvariant();

        if (expectedAffiliateAccountId.HasValue &&
            order.AffiliateAccountId != expectedAffiliateAccountId.Value)
        {
            throw new InvalidOperationException(
                $"Affiliate attribution authority mismatch for Order {orderId}: event affiliate {expectedAffiliateAccountId.Value} does not match order affiliate {order.AffiliateAccountId?.ToString() ?? "none"}.");
        }

        if (!string.IsNullOrWhiteSpace(order.AffiliateReferralCode) &&
            !string.Equals(order.AffiliateReferralCode.Trim(), codeNormalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Affiliate attribution authority mismatch for Order {orderId}: event referral code does not match order referral code.");
        }

        if (string.IsNullOrWhiteSpace(order.AffiliateReferralCode) && expectedAffiliateAccountId.HasValue)
        {
            throw new InvalidOperationException(
                $"Affiliate attribution authority mismatch for Order {orderId}: event affiliate is present but order has no referral code snapshot.");
        }

        if (promotionLinkId.HasValue && order.PromotionLinkId != promotionLinkId.Value)
        {
            throw new InvalidOperationException(
                $"Affiliate attribution authority mismatch for Order {orderId}: event promotion link does not match order promotion link.");
        }

        if (order.PromotionLinkId.HasValue && promotionLinkId != order.PromotionLinkId.Value)
        {
            throw new InvalidOperationException(
                $"Affiliate attribution authority mismatch for Order {orderId}: order promotion link was not preserved by the attribution event.");
        }

        if (existingAttribution != null)
        {
            // Idempotent: same affiliate → return existing
            // BUT: validate against order snapshot to prevent paying wrong affiliate
            if (existingAttribution.ReferralCode == codeNormalized &&
                (!expectedAffiliateAccountId.HasValue || existingAttribution.AffiliateAccountId == expectedAffiliateAccountId.Value) &&
                (!promotionLinkId.HasValue || existingAttribution.PromotionLinkId == promotionLinkId.Value))
            {
                // Additional validation: verify existingAttribution matches order snapshot
                if (order.AffiliateAccountId.HasValue && existingAttribution.AffiliateAccountId != order.AffiliateAccountId.Value)
                {
                    throw new InvalidOperationException(
                        $"Attribution conflict for Order {orderId}: existing attribution affiliate {existingAttribution.AffiliateAccountId} does not match order snapshot affiliate {order.AffiliateAccountId.Value}");
                }
                
                if (!string.IsNullOrWhiteSpace(order.AffiliateReferralCode) &&
                    !string.Equals(existingAttribution.ReferralCode, order.AffiliateReferralCode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Attribution conflict for Order {orderId}: existing attribution referral code {existingAttribution.ReferralCode} does not match order snapshot referral code {order.AffiliateReferralCode}");
                }
                
                if (order.PromotionLinkId.HasValue && existingAttribution.PromotionLinkId != order.PromotionLinkId.Value)
                {
                    throw new InvalidOperationException(
                        $"Attribution conflict for Order {orderId}: existing attribution promotion link {existingAttribution.PromotionLinkId} does not match order snapshot promotion link {order.PromotionLinkId.Value}");
                }
                
                _logger.LogInformation(
                    "Order {OrderId} already attributed to {Code} — idempotent",
                    orderId, codeNormalized);
                return existingAttribution;
            }
            // Different affiliate → reject
            throw new InvalidOperationException(
                $"Order {orderId} is already attributed to affiliate {existingAttribution.ReferralCode}");
        }

        // AF-19: GLOBAL MARKETPLACE - Lookup affiliate by ReferralCode globally (no tenant filter)
        var affiliate = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => a.ReferralCode == codeNormalized && !a.IsDeleted)
            .Select(a => new
            {
                a.Id,
                a.ReferralCode,
                a.Status,
                a.IsDeleted,
                a.UserId,
                TenantId = (Guid?)a.TenantId
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Affiliate with code '{codeNormalized}' not found");

        if (affiliate.Status != AffiliateStatus.Active)
            throw new InvalidOperationException($"Affiliate {affiliate.Id} is not active (status: {affiliate.Status})");

        await EnsureAffiliateSelfPurchaseBlockedAsync(order, affiliate.Id, affiliate.UserId, ct);

        if (expectedAffiliateAccountId.HasValue && affiliate.Id != expectedAffiliateAccountId.Value)
        {
            throw new InvalidOperationException(
                $"Affiliate attribution authority mismatch for Order {orderId}: referral code resolves to affiliate {affiliate.Id}, not event affiliate {expectedAffiliateAccountId.Value}.");
        }

        if (order.AffiliateAccountId.HasValue && order.AffiliateAccountId.Value != affiliate.Id)
        {
            throw new InvalidOperationException(
                $"Affiliate attribution authority mismatch for Order {orderId}: referral code resolves to affiliate {affiliate.Id}, not order affiliate {order.AffiliateAccountId.Value}.");
        }

        var attribution = promotionLinkId.HasValue
            ? AffiliateAttribution.CreateV2(
                tenantId, orderId, affiliate.Id, affiliate.ReferralCode, source, promotionLinkId.Value)
            : AffiliateAttribution.Create(
                tenantId, orderId, affiliate.Id, affiliate.ReferralCode, source);

        var ownsTransaction = _context.Database.CurrentTransaction == null;
        await using IDbContextTransaction? transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(ct)
            : null;
        var rolledBack = false;
        try
        {
            _context.AffiliateAttributions.Add(attribution);
            
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Race condition: another concurrent request inserted the same attribution
                // Detach the entity we tried to add
                _context.Entry(attribution).State = EntityState.Detached;
                
                // Reload the persisted attribution
                var persistedAttribution = await _context.AffiliateAttributions
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.OrderId == orderId && !a.IsDeleted, ct);
                
                if (persistedAttribution == null)
                {
                    // Should not happen, but if it does, rethrow original exception
                    throw;
                }
                
                // Re-run authority/validation logic against the persisted attribution
                if (persistedAttribution.ReferralCode == codeNormalized &&
                    (!expectedAffiliateAccountId.HasValue || persistedAttribution.AffiliateAccountId == expectedAffiliateAccountId.Value) &&
                    (!promotionLinkId.HasValue || persistedAttribution.PromotionLinkId == promotionLinkId.Value))
                {
                    // Additional validation: verify persistedAttribution matches order snapshot
                    if (order.AffiliateAccountId.HasValue && persistedAttribution.AffiliateAccountId != order.AffiliateAccountId.Value)
                    {
                        if (ownsTransaction)
                        {
                            await transaction!.RollbackAsync(ct);
                            rolledBack = true;
                        }
                        throw new InvalidOperationException(
                            $"Attribution conflict for Order {orderId}: persisted attribution affiliate {persistedAttribution.AffiliateAccountId} does not match order snapshot affiliate {order.AffiliateAccountId.Value} (race recovery)");
                    }
                    
                    if (!string.IsNullOrWhiteSpace(order.AffiliateReferralCode) &&
                        !string.Equals(persistedAttribution.ReferralCode, order.AffiliateReferralCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        if (ownsTransaction)
                        {
                            await transaction!.RollbackAsync(ct);
                            rolledBack = true;
                        }
                        throw new InvalidOperationException(
                            $"Attribution conflict for Order {orderId}: persisted attribution referral code {persistedAttribution.ReferralCode} does not match order snapshot referral code {order.AffiliateReferralCode} (race recovery)");
                    }
                    
                    if (order.PromotionLinkId.HasValue && persistedAttribution.PromotionLinkId != order.PromotionLinkId.Value)
                    {
                        if (ownsTransaction)
                        {
                            await transaction!.RollbackAsync(ct);
                            rolledBack = true;
                        }
                        throw new InvalidOperationException(
                            $"Attribution conflict for Order {orderId}: persisted attribution promotion link {persistedAttribution.PromotionLinkId} does not match order snapshot promotion link {order.PromotionLinkId.Value} (race recovery)");
                    }
                    
                    // Idempotent: same affiliate → rollback and return existing
                    if (ownsTransaction)
                    {
                        await transaction!.RollbackAsync(ct);
                        rolledBack = true;
                    }
                    
                    // Clear change tracker so rolled-back entities don't block outbox persistence
                    _context.ChangeTracker.Clear();
                    
                    // The winning insert transaction owns the authoritative projection event.
                    _logger.LogInformation(
                        "Order {OrderId} already attributed to {Code} (race recovery) — idempotent",
                        orderId, codeNormalized);
                    return persistedAttribution;
                }
                
                // Different affiliate → reject
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(ct);
                    rolledBack = true;
                }
                throw new InvalidOperationException(
                    $"Order {orderId} is already attributed to affiliate {persistedAttribution.ReferralCode} (detected during race recovery)");
            }

            // AF-36: Determine payment classification for downstream gating
            var paymentCode = order.PaymentMethodCode ?? string.Empty;
            var requiresVerification = RequiresPaymentVerification(paymentCode);

            await _eventPublisher.PublishAsync(new AffiliateAttributedEvent
            {
                TenantId = tenantId,
                StoreTenantId = tenantId,
                AffiliateAuthorityTenantId = NormalizeAffiliateAuthorityTenantId(affiliate.TenantId),
                AttributionId = attribution.Id,
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                AffiliateAccountId = affiliate.Id,
                ReferralCode = affiliate.ReferralCode,
                PromotionLinkId = attribution.PromotionLinkId,
                Source = source,
                AttributedAtUtc = attribution.AttributedAtUtc,
                CorrelationId = orderId.ToString(),
                PaymentMethodCode = paymentCode,
                RequiresPaymentVerification = requiresVerification
            }, ct);

            await _context.SaveChangesAsync(ct);
            if (ownsTransaction) await transaction!.CommitAsync(ct);
        }
        catch
        {
            if (ownsTransaction && !rolledBack) await transaction!.RollbackAsync(ct);
            throw;
        }

        _logger.LogInformation(
            "Order {OrderId} attributed to affiliate {AffiliateId} via {Code}",
            orderId, affiliate.Id, affiliate.ReferralCode);

        return attribution;
    }

    private async Task EnsureAffiliateSelfPurchaseBlockedAsync(
        Order order,
        Guid affiliateAccountId,
        Guid affiliateOwnerUserId,
        CancellationToken ct)
    {
        var buyerEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buyerPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddNormalizedEmail(buyerEmails, order.GuestEmail);
        AddNormalizedPhone(buyerPhones, order.GuestPhone);

        if (order.OnlineCustomerId.HasValue)
        {
            var onlineCustomerIdentity = await _context.OnlineCustomers
                .IgnoreQueryFilters()
                .Where(customer => customer.Id == order.OnlineCustomerId.Value)
                .Select(customer => new
                {
                    Email = string.IsNullOrWhiteSpace(customer.NormalizedEmail)
                        ? customer.Email
                        : customer.NormalizedEmail,
                    customer.Phone
                })
                .FirstOrDefaultAsync(ct);
            AddNormalizedEmail(buyerEmails, onlineCustomerIdentity?.Email);
            AddNormalizedPhone(buyerPhones, onlineCustomerIdentity?.Phone);
        }

        if (order.CustomerId.HasValue)
        {
            var posCustomerIdentity = await _context.Customers
                .IgnoreQueryFilters()
                .Where(customer => customer.Id == order.CustomerId.Value && !customer.IsDeleted)
                .Select(customer => new
                {
                    customer.Email,
                    customer.Phone
                })
                .FirstOrDefaultAsync(ct);
            AddNormalizedEmail(buyerEmails, posCustomerIdentity?.Email);
            AddNormalizedPhone(buyerPhones, posCustomerIdentity?.Phone);
        }

        if (buyerEmails.Count == 0 && buyerPhones.Count == 0)
        {
            return;
        }

        var affiliateEmails = await _context.AffiliateUsers
            .IgnoreQueryFilters()
            .Where(user => user.AffiliateAccountId == affiliateAccountId && !user.IsDeleted)
            .Select(user => user.Email)
            .ToListAsync(ct);

        if (affiliateOwnerUserId != Guid.Empty)
        {
            var ownerEmail = await _context.Users
                .IgnoreQueryFilters()
                .Where(user => user.Id == affiliateOwnerUserId && !user.IsDeleted)
                .Select(user => user.Email)
                .FirstOrDefaultAsync(ct);
            affiliateEmails.Add(ownerEmail ?? string.Empty);
        }

        foreach (var affiliateEmail in affiliateEmails)
        {
            var normalizedAffiliateEmail = NormalizeEmailOrNull(affiliateEmail);
            if (normalizedAffiliateEmail != null && buyerEmails.Contains(normalizedAffiliateEmail))
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_SELF_PURCHASE_BLOCKED: Affiliate {affiliateAccountId} cannot receive attribution for an order placed by the same email identity. OrderId={order.Id}.");
            }
        }

        if (buyerPhones.Count == 0 || affiliateOwnerUserId == Guid.Empty)
        {
            return;
        }

        var affiliateOwnerPhone = await _context.Users
            .IgnoreQueryFilters()
            .Where(user => user.Id == affiliateOwnerUserId && !user.IsDeleted)
            .Select(user => user.Phone)
            .FirstOrDefaultAsync(ct);

        var normalizedAffiliateOwnerPhone = NormalizePhoneOrNull(affiliateOwnerPhone);
        if (normalizedAffiliateOwnerPhone != null && buyerPhones.Contains(normalizedAffiliateOwnerPhone))
        {
            throw new InvalidOperationException(
                $"AFFILIATE_SELF_PURCHASE_BLOCKED: Affiliate {affiliateAccountId} cannot receive attribution for an order placed by the same phone identity. OrderId={order.Id}.");
        }
    }

    private static void AddNormalizedEmail(HashSet<string> emails, string? email)
    {
        var normalized = NormalizeEmailOrNull(email);
        if (normalized != null)
        {
            emails.Add(normalized);
        }
    }

    private static string? NormalizeEmailOrNull(string? email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim().ToLowerInvariant();
    }

    private static void AddNormalizedPhone(HashSet<string> phones, string? phone)
    {
        var normalized = NormalizePhoneOrNull(phone);
        if (normalized != null)
        {
            phones.Add(normalized);
        }
    }

    private static string? NormalizePhoneOrNull(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 7)
        {
            return null;
        }

        return digits.Length > 10 ? digits[^10..] : digits;
    }

    /// <summary>
    /// Recovery-only repair path that rebuilds the canonical attribution row from the
    /// order snapshot after the original attribution event path failed.
    /// Commission issuance must never call this directly.
    /// </summary>
    private async Task<AffiliateAttribution?> AttributeOrderToAffiliateFromOrderSnapshotAsync(
        Guid orderId,
        string referralCode,
        AttributionSource source,
        Guid affiliateAccountId,
        Guid? promotionLinkId,
        CancellationToken ct = default)
    {
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found");

        var tenantId = order.TenantId;

        var codeNormalized = referralCode.Trim().ToUpperInvariant();

        // Guard: check for existing attribution
        var existingAttribution = await _context.AffiliateAttributions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.OrderId == orderId && !a.IsDeleted, ct);

        if (existingAttribution != null)
        {
            // Validate existingAttribution matches order snapshot before treating as idempotent
            if (existingAttribution.AffiliateAccountId != affiliateAccountId)
            {
                throw new InvalidOperationException(
                    $"Attribution conflict for Order {orderId}: existing attribution affiliate {existingAttribution.AffiliateAccountId} does not match order snapshot affiliate {affiliateAccountId} (snapshot rebuild)");
            }
            
            if (!string.Equals(existingAttribution.ReferralCode, codeNormalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Attribution conflict for Order {orderId}: existing attribution referral code {existingAttribution.ReferralCode} does not match order snapshot referral code {codeNormalized} (snapshot rebuild)");
            }
            
            if (promotionLinkId.HasValue && existingAttribution.PromotionLinkId != promotionLinkId.Value)
            {
                throw new InvalidOperationException(
                    $"Attribution conflict for Order {orderId}: existing attribution promotion link {existingAttribution.PromotionLinkId} does not match order snapshot promotion link {promotionLinkId.Value} (snapshot rebuild)");
            }
            
            _logger.LogInformation(
                "Order {OrderId} already attributed to {Code} — idempotent (snapshot rebuild)",
                orderId, referralCode);
            return existingAttribution;
        }

        // Verify affiliate exists (but don't check status or IsDeleted - let commission recording handle inactive/deleted affiliates)
        // This allows deleted affiliates to reach the commission-recording skip path
        var existingAffiliateId = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => a.Id == affiliateAccountId)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        if (!existingAffiliateId.HasValue)
        {
            _logger.LogWarning(
                "Cannot rebuild attribution for Order {OrderId}: affiliate {AffiliateId} not found",
                orderId, affiliateAccountId);
            return null;
        }

        var affiliateIdentity = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => a.Id == affiliateAccountId)
            .Select(a => new
            {
                a.Id,
                a.UserId
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Affiliate {affiliateAccountId} disappeared during attribution snapshot rebuild.");

        await EnsureAffiliateSelfPurchaseBlockedAsync(order, affiliateIdentity.Id, affiliateIdentity.UserId, ct);

        var affiliateAuthorityTenantId = await ResolveAffiliateAuthorityTenantIdAsync(affiliateAccountId, ct);

        var attribution = promotionLinkId.HasValue
            ? AffiliateAttribution.CreateV2(
                tenantId, orderId, affiliateIdentity.Id, codeNormalized, source, promotionLinkId.Value)
            : AffiliateAttribution.Create(
                tenantId, orderId, affiliateIdentity.Id, codeNormalized, source);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        var rolledBack = false;
        try
        {
            _context.AffiliateAttributions.Add(attribution);
            
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Race condition: another concurrent request inserted the same attribution
                _context.Entry(attribution).State = EntityState.Detached;
                
                var persistedAttribution = await _context.AffiliateAttributions
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.OrderId == orderId && !a.IsDeleted, ct);
                
                if (persistedAttribution != null)
                {
                    // Validate persistedAttribution matches order snapshot before treating as idempotent
                    if (persistedAttribution.AffiliateAccountId != affiliateAccountId)
                    {
                        await transaction.RollbackAsync(ct);
                        rolledBack = true;
                        throw new InvalidOperationException(
                            $"Attribution conflict for Order {orderId}: persisted attribution affiliate {persistedAttribution.AffiliateAccountId} does not match order snapshot affiliate {affiliateAccountId} (race recovery, snapshot rebuild)");
                    }
                    
                    if (!string.Equals(persistedAttribution.ReferralCode, codeNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        await transaction.RollbackAsync(ct);
                        rolledBack = true;
                        throw new InvalidOperationException(
                            $"Attribution conflict for Order {orderId}: persisted attribution referral code {persistedAttribution.ReferralCode} does not match order snapshot referral code {codeNormalized} (race recovery, snapshot rebuild)");
                    }
                    
                    if (promotionLinkId.HasValue && persistedAttribution.PromotionLinkId != promotionLinkId.Value)
                    {
                        await transaction.RollbackAsync(ct);
                        rolledBack = true;
                        throw new InvalidOperationException(
                            $"Attribution conflict for Order {orderId}: persisted attribution promotion link {persistedAttribution.PromotionLinkId} does not match order snapshot promotion link {promotionLinkId.Value} (race recovery, snapshot rebuild)");
                    }
                    
                    await transaction.RollbackAsync(ct);
                    rolledBack = true;
                    
                    // Clear change tracker so rolled-back entities don't block outbox persistence
                    _context.ChangeTracker.Clear();
                    
                    // The winning insert transaction owns the authoritative projection event.
                    _logger.LogInformation(
                        "Order {OrderId} already attributed to {Code} (race recovery, snapshot rebuild) — idempotent",
                        orderId, codeNormalized);
                    return persistedAttribution;
                }
                
                throw;
            }

            // AF-36: Determine payment classification for downstream gating
            var paymentCode = order.PaymentMethodCode ?? string.Empty;
            var requiresVerification = RequiresPaymentVerification(paymentCode);

            // Publish AffiliateAttributedEvent so downstream projections see the attribution
            await _eventPublisher.PublishAsync(new AffiliateAttributedEvent
            {
                TenantId = tenantId,
                StoreTenantId = tenantId,
                AffiliateAuthorityTenantId = affiliateAuthorityTenantId,
                AttributionId = attribution.Id,
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                AffiliateAccountId = attribution.AffiliateAccountId,
                ReferralCode = attribution.ReferralCode,
                PromotionLinkId = attribution.PromotionLinkId,
                Source = source,
                AttributedAtUtc = attribution.AttributedAtUtc,
                CorrelationId = orderId.ToString(),
                PaymentMethodCode = paymentCode,
                RequiresPaymentVerification = requiresVerification
            }, ct);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            if (!rolledBack) await transaction.RollbackAsync(ct);
            throw;
        }

        _logger.LogInformation(
            "Order {OrderId} attributed to affiliate {AffiliateId} via {Code} (rebuilt from snapshot)",
            orderId, affiliateIdentity.Id, codeNormalized);

        return attribution;
    }

    // ═══════════════════════════════════════════════════════════════════
    // COMMISSION RECORDING
    //
    // CRITICAL: These methods are called from MassTransit consumers which
    // run WITHOUT HTTP tenant context. We must use IgnoreQueryFilters()
    // and derive TenantId from the Order entity itself.
    //
    // AF-2.5: All skip/success/idempotent paths emit metrics.
    // ═══════════════════════════════════════════════════════════════════

    public async Task<int> ReconcileMissingAttributionsAsync(CancellationToken ct = default)
    {
        var candidates = await _context.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                !order.IsDeleted &&
                order.AffiliateAccountId.HasValue &&
                !string.IsNullOrWhiteSpace(order.AffiliateReferralCode) &&
                !_context.AffiliateAttributions
                    .IgnoreQueryFilters()
                    .Any(attribution =>
                        attribution.TenantId == order.TenantId &&
                        attribution.OrderId == order.Id &&
                        !attribution.IsDeleted))
            .OrderBy(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .Take(AttributionRecoveryBatchSize)
            .Select(order => order.Id)
            .ToListAsync(ct);

        var repairedCount = 0;
        foreach (var orderId in candidates)
        {
            if (await TryRepairMissingAttributionAsync(orderId, ct))
            {
                repairedCount++;
            }
        }

        return repairedCount;
    }

    public async Task RecordCommissionForOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        // CRITICAL: Use IgnoreQueryFilters — called from MassTransit consumer (no HTTP tenant context).
        // Derive TenantId from the Order entity itself.
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct);

        if (order == null)
        {
            _logger.LogWarning(
                "[Affiliate:RecordCommission] Order {OrderId} not found — skipping", orderId);
            _telemetry.RecordSkipped(Guid.Empty, "order_not_found");
            return;
        }

        var tenantId = order.TenantId;

        // Guard: attribution must exist
        var attribution = await _context.AffiliateAttributions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.OrderId == orderId && !a.IsDeleted, ct);

        if (attribution == null)
        {
            if (order.AffiliateAccountId.HasValue && !string.IsNullOrWhiteSpace(order.AffiliateReferralCode))
            {
                _logger.LogWarning(
                    "[Affiliate:RecordCommission] Canonical attribution row missing for referred order {OrderId}; retrying commission recording until reconciliation repairs the authority boundary",
                    orderId);
                _telemetry.RecordSkipped(tenantId, "attribution_missing_authoritative");
                throw new InvalidOperationException(
                    $"Canonical affiliate attribution is missing for referred order {orderId}.");
            }

            if (order.AffiliateAccountId.HasValue || !string.IsNullOrWhiteSpace(order.AffiliateReferralCode))
            {
                _logger.LogWarning(
                    "[Affiliate:RecordCommission] Referred order {OrderId} has incomplete affiliate authority snapshot - retrying commission recording",
                    orderId);
                _telemetry.RecordSkipped(tenantId, "attribution_snapshot_incomplete");
                throw new InvalidOperationException(
                    $"Affiliate attribution authority snapshot is incomplete for referred order {orderId}.");
            }

            if (attribution == null)
            {
                _logger.LogDebug("No attribution for Order {OrderId} — skipping commission", orderId);
                _telemetry.RecordSkipped(tenantId, "no_attribution");
                return;
            }
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            _logger.LogWarning(
                "[Affiliate:RecordCommission] Terminal order status cannot create pending commission | OrderId: {OrderId} | OrderStatus: {OrderStatus}",
                orderId,
                order.Status);
            _telemetry.RecordSkipped(tenantId, "terminal_order_status");
            return;
        }

        if (!HasPendingCommissionPaymentAuthority(order.PaymentStatus))
        {
            _logger.LogWarning(
                "[Affiliate:RecordCommission] Payment authority not confirmed for referred order {OrderId}; pending commission not created | PaymentStatus: {PaymentStatus}",
                orderId,
                order.PaymentStatus);
            _telemetry.RecordSkipped(tenantId, "payment_not_collected");
            return;
        }

        if (order.FinancialPostingStatus != PointOfSale.Core.Enums.FinancialPostingStatus.Posted &&
            order.FinancialPostingStatus != PointOfSale.Core.Enums.FinancialPostingStatus.NotRequired)
        {
            _logger.LogWarning(
                "[Affiliate:RecordCommission] Accounting not posted for referred order {OrderId}; deferring commission | FinancialPostingStatus: {FinancialPostingStatus}",
                orderId,
                order.FinancialPostingStatus);
            _telemetry.RecordSkipped(tenantId, "accounting_not_posted");
            throw new AffiliateCommissionDeferredException(
                orderId,
                $"FinancialPostingStatus={order.FinancialPostingStatus}");
        }

        // Guard: affiliate identity must still exist. Do not re-check current active
        // status here; checkout line admission already captured candidate authority.
        // Later suspension must not erase an admitted commission candidate.
        // AF-18: CROSS-TENANT FIX - Don't filter by tenantId, affiliate may be from different tenant
        // Don't filter by IsDeleted - let deleted affiliates reach the skip logic below
        var affiliate = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => a.Id == attribution.AffiliateAccountId)
            .Select(a => new
            {
                a.Id,
                a.ReferralCode,
                a.IsDeleted,
                TenantId = (Guid?)a.TenantId
            })
            .FirstOrDefaultAsync(ct);

        if (affiliate == null || affiliate.IsDeleted)
        {
            _logger.LogWarning(
                "Affiliate {AffiliateId} no longer exists or has been deleted - skipping commission for Order {OrderId}",
                attribution.AffiliateAccountId, orderId);
            _telemetry.RecordSkipped(tenantId, "affiliate_missing_or_deleted");
            return;
        }

        // AF-18: Log cross-tenant commission recording
        if (affiliate.TenantId.HasValue && affiliate.TenantId.Value != tenantId)
        {
            _logger.LogInformation(
                "[AF-18:CrossTenant:Commission] Recording commission for cross-tenant sale | " +
                "OrderId: {OrderId} | OrderTenant: {OrderTenant} | AffiliateTenant: {AffiliateTenant} | " +
                "AffiliateId: {AffiliateId} | ReferralCode: {ReferralCode}",
                orderId, tenantId, affiliate.TenantId, affiliate.Id, affiliate.ReferralCode);
        }

        var earnedEntries = new List<AffiliateCommissionLedger>();
        var skippedUnpaid = 0;
        var skippedNoPolicy = 0;
        var idempotentSkips = 0;
        var ownsTransaction = _context.Database.CurrentTransaction == null;

        await using IDbContextTransaction? transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            foreach (var item in order.Items)
            {
                // Calculate the commissionable amount for this item.
                // PHASE 11.4.2.A: Use item.Total (UnitPrice × Quantity - Discount + Tax)
                // as the base for commission, NOT PaidAmount.
                //
                // WHY: For COD orders, PaidAmount starts at 0 and is only set when the
                // accounting outbox processes the CodDelivered message. But commission is
                // recorded when the CodDeliveredAffiliateConsumer fires, which happens
                // BEFORE accounting allocation. Using PaidAmount would skip ALL COD items.
                //
                // item.Total = the actual amount the customer paid for this SKU.
                var hasImmutableSnapshot = item.AffiliateCommissionSnapshotCapturedAtUtc.HasValue;
                var commissionableAmount = item.AffiliateCommissionBaseAmountSnapshot ?? item.Total;

                if (commissionableAmount <= 0)
                {
                    _logger.LogDebug(
                        "OrderItem {OrderItemId} has commissionable amount {Amount} — skipping",
                        item.Id, commissionableAmount);
                    skippedUnpaid++;
                    continue;
                }

                if (!item.AffiliateCommissionEligible)
                {
                    _logger.LogDebug(
                        "No eligible immutable affiliate commission snapshot for Product {ProductId} - skipping OrderItem {OrderItemId}",
                        item.ProductId, item.Id);
                    skippedNoPolicy++;
                    continue;
                }

                if (!item.AffiliateCommissionRateSnapshot.HasValue ||
                    !item.AffiliateCommissionBaseAmountSnapshot.HasValue ||
                    item.AffiliateCommissionBaseAmountSnapshot.Value <= 0m ||
                    !item.AffiliateCommissionAmountSnapshot.HasValue ||
                    item.AffiliateCommissionAmountSnapshot.Value <= 0m ||
                    !hasImmutableSnapshot)
                {
                    throw new InvalidOperationException(
                        $"OrderItem {item.Id} is marked affiliate-eligible but is missing immutable commission snapshot values.");
                }

                var snapshotEntry = AffiliateCommissionLedger.CreateEarnedFromSnapshot(
                    tenantId,
                    attribution.Id,
                    affiliate.Id,
                    orderId,
                    item.Id,
                    commissionableAmount,
                    item.AffiliateCommissionRateSnapshot.Value,
                    item.AffiliateCommissionAmountSnapshot.Value,
                    order.Currency);

                // Commission is paid only from the checkout-time immutable snapshot.
                var entry = snapshotEntry;

                // AF-19B: GLOBAL MARKETPLACE - Record StoreTenantId ONLY (where sale happened)
                // Affiliate has NO tenant binding - truly global
                try
                {
                    _context.AffiliateCommissionLedger.Add(entry);
                    await _context.SaveChangesAsync(ct);
                    earnedEntries.Add(entry);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // Idempotent: already recorded → detach the failed entity and continue
                    _context.Entry(entry).State = EntityState.Detached;
                    idempotentSkips++;
                    _telemetry.RecordIdempotentSkip(tenantId, "earned");

                    _logger.LogInformation(
                        "Commission already recorded for OrderItem {OrderItemId} — idempotent skip",
                        item.Id);
                }
            }

            // OUTBOX RULE: Stage authoritative event before commit so outbox row is atomic with ledger writes.
            // AF-18: Emit event in affiliate's tenant (where commission ledger is stored)
            if (earnedEntries.Count > 0)
            {
                await _eventPublisher.PublishAsync(new AffiliateCommissionEarnedEvent
                {
                    TenantId = order.TenantId,
                    StoreTenantId = order.TenantId, // AF-19B: Store's tenant ID (for accounting)
                    AffiliateAuthorityTenantId = NormalizeAffiliateAuthorityTenantId(affiliate.TenantId),
                    OrderId = orderId,
                    OrderNumber = order.OrderNumber,
                    AffiliateAccountId = affiliate.Id,
                    ReferralCode = affiliate.ReferralCode,
                    TotalCommission = earnedEntries.Sum(e => e.CommissionAmount),
                    Currency = order.Currency,
                    ItemCount = earnedEntries.Count,
                    Items = earnedEntries.Select(e =>
                    {
                        var item = order.Items.First(i => i.Id == e.OrderItemId);
                        return new CommissionLineItem
                        {
                            OrderItemId = e.OrderItemId,
                            ProductId = item.ProductId,
                            ProductName = item.ProductName,
                            VariantName = item.VariantName,
                            PaidAmount = e.BaseAmount,
                            CommissionRate = e.CommissionRate,
                            CommissionAmount = e.CommissionAmount
                        };
                    }).ToList(),
                    CorrelationId = orderId.ToString()
                }, ct);

                var currentEarnedEntries = await LoadCurrentEarnedEntriesForOrderAsync(orderId, ct);
                await ReconcileRefundedCommissionEntriesAsync(order, currentEarnedEntries, ct);
                await ReconcileDeliveredCommissionApprovalsAsync(order, currentEarnedEntries, ct);

                await _context.SaveChangesAsync(ct);
                if (ownsTransaction)
                {
                    await transaction!.CommitAsync(ct);
                }

                _logger.LogInformation(
                    "[AF-18] Recorded commission for Order {OrderId} in affiliate's tenant | " +
                    "AffiliateTenant: {AffiliateTenant} | OrderTenant: {OrderTenant} | " +
                    "Items: {Count}, Total: {Total} {Currency} " +
                    "(skippedUnpaid={SkippedUnpaid}, skippedNoPolicy={SkippedNoPolicy}, idempotent={IdempotentSkips})",
                    orderId, affiliate.TenantId, tenantId, earnedEntries.Count,
                    earnedEntries.Sum(e => e.CommissionAmount), order.Currency,
                    skippedUnpaid, skippedNoPolicy, idempotentSkips);
            }
            else
            {
                var currentEarnedEntries = await LoadCurrentEarnedEntriesForOrderAsync(orderId, ct);
                await ReconcileRefundedCommissionEntriesAsync(order, currentEarnedEntries, ct);
                await ReconcileDeliveredCommissionApprovalsAsync(order, currentEarnedEntries, ct);

                await _context.SaveChangesAsync(ct);
                if (ownsTransaction)
                {
                    await transaction!.CommitAsync(ct);
                }
            }
        }
        catch
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.RollbackAsync(ct);
            }

            throw;
        }

        // AF-2.5: Emit metrics for all skip reasons
        if (skippedUnpaid > 0)
            _telemetry.RecordSkipped(tenantId, "unpaid_item");
        if (skippedNoPolicy > 0)
            _telemetry.RecordSkipped(tenantId, "no_policy");

        // AF-2.5: Record success count
        if (earnedEntries.Count > 0)
        {
            _telemetry.RecordSuccess(tenantId, earnedEntries.Count);
        }
    }

    private static bool HasPendingCommissionPaymentAuthority(PaymentStatus paymentStatus)
    {
        return paymentStatus is PaymentStatus.Collected
            or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded;
    }

    private async Task<bool> TryRepairMissingAttributionAsync(Guid orderId, CancellationToken ct)
    {
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId && !candidate.IsDeleted, ct);

        if (order == null ||
            !order.AffiliateAccountId.HasValue ||
            string.IsNullOrWhiteSpace(order.AffiliateReferralCode))
        {
            return false;
        }

        var existingAttribution = await _context.AffiliateAttributions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                attribution => attribution.TenantId == order.TenantId &&
                               attribution.OrderId == order.Id &&
                               !attribution.IsDeleted,
                ct);

        if (existingAttribution != null)
        {
            return false;
        }

        try
        {
            var repaired = await AttributeOrderToAffiliateFromOrderSnapshotAsync(
                order.Id,
                order.AffiliateReferralCode,
                AttributionSource.ReferralLink,
                order.AffiliateAccountId.Value,
                order.PromotionLinkId,
                ct);

            return repaired != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[Affiliate:AttributionRecovery] Failed to repair missing attribution for Order {OrderId}",
                order.Id);
            return false;
        }
    }

    private async Task<List<AffiliateCommissionLedger>> LoadCurrentEarnedEntriesForOrderAsync(
        Guid orderId,
        CancellationToken ct)
    {
        return await _context.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(entry => entry.OrderId == orderId &&
                            entry.EntryType == CommissionEntryType.Earned)
            .ToListAsync(ct);
    }

    private async Task ReconcileRefundedCommissionEntriesAsync(
        Order order,
        IReadOnlyCollection<AffiliateCommissionLedger> earnedEntries,
        CancellationToken ct)
    {
        if (earnedEntries.Count == 0)
        {
            return;
        }

        var refundedItemIds = order.Items
            .Where(item => item.RefundedAmount > 0m)
            .Select(item => item.Id)
            .ToHashSet();

        if (refundedItemIds.Count == 0)
        {
            return;
        }

        var existingReversalTotalsByOriginalId = await LoadReversalTotalsByOriginalEntryIdAsync(
            earnedEntries.Select(entry => entry.Id).ToList(),
            ct);

        var refundedAmountByItemId = order.Items
            .Where(item => item.RefundedAmount > 0m)
            .ToDictionary(item => item.Id, item => item.RefundedAmount);

        var reversals = new List<AffiliateCommissionLedger>();

        foreach (var earned in earnedEntries.Where(entry => refundedItemIds.Contains(entry.OrderItemId)))
        {
            var refundedBaseAmount = refundedAmountByItemId.GetValueOrDefault(earned.OrderItemId);
            var reversalAuthority = CalculateRefundCommissionReversalAuthority(
                earned,
                existingReversalTotalsByOriginalId,
                refundedBaseAmount);

            if (reversalAuthority == null)
            {
                continue;
            }

            var idempotencyKey = BuildRefundReversalIdempotencyKey(
                $"CUMULATIVE:{order.Id:N}",
                earned.OrderItemId,
                reversalAuthority.BaseAmount);

            var reversal = AffiliateCommissionLedger.CreateReversalWithCustomKey(
                earned.StoreTenantId,
                earned.Id,
                earned.AttributionId,
                earned.AffiliateAccountId,
                earned.OrderId,
                earned.OrderItemId,
                reversalAuthority.BaseAmount,
                reversalAuthority.CommissionAmount,
                earned.CommissionRate,
                earned.Currency,
                idempotencyKey);

            try
            {
                _context.AffiliateCommissionLedger.Add(reversal);
                await _context.SaveChangesAsync(ct);
                reversals.Add(reversal);
                existingReversalTotalsByOriginalId[earned.Id] = new ReversalTotals(
                    (existingReversalTotalsByOriginalId.GetValueOrDefault(earned.Id)?.BaseAmount ?? 0m) + reversal.BaseAmount,
                    (existingReversalTotalsByOriginalId.GetValueOrDefault(earned.Id)?.CommissionAmount ?? 0m) + reversal.CommissionAmount);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _context.Entry(reversal).State = EntityState.Detached;
                _telemetry.RecordIdempotentSkip(earned.StoreTenantId, "reversed");
            }
        }

        foreach (var reversal in reversals)
        {
            var affiliateAuthorityTenantId = await ResolveAffiliateAuthorityTenantIdAsync(
                reversal.AffiliateAccountId,
                ct);

            await _eventPublisher.PublishAsync(new AffiliateCommissionReversedEvent
            {
                TenantId = reversal.StoreTenantId,
                StoreTenantId = reversal.StoreTenantId,
                AffiliateAuthorityTenantId = affiliateAuthorityTenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OrderItemId = reversal.OrderItemId,
                AffiliateAccountId = reversal.AffiliateAccountId,
                OriginalLedgerEntryId = reversal.OriginalEntryId!.Value,
                ReversalLedgerEntryId = reversal.Id,
                ReversedAmount = reversal.CommissionAmount,
                Currency = reversal.Currency,
                CorrelationId = order.Id.ToString()
            }, ct);
        }

        if (reversals.Count > 0)
        {
            _logger.LogWarning(
                "[Affiliate:RecordCommission] Reconciled previously refunded items while recording commission | OrderId: {OrderId} | Reversals: {Count}",
                order.Id,
                reversals.Count);
        }
    }

    private async Task<Dictionary<Guid, ReversalTotals>> LoadReversalTotalsByOriginalEntryIdAsync(
        IReadOnlyCollection<Guid> originalEntryIds,
        CancellationToken ct)
    {
        if (originalEntryIds.Count == 0)
        {
            return new Dictionary<Guid, ReversalTotals>();
        }

        var reversalRows = await _context.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(entry =>
                entry.EntryType == CommissionEntryType.Reversed &&
                entry.OriginalEntryId.HasValue &&
                originalEntryIds.Contains(entry.OriginalEntryId.Value))
            .Select(entry => new
            {
                OriginalEntryId = entry.OriginalEntryId!.Value,
                entry.BaseAmount,
                entry.CommissionAmount
            })
            .ToListAsync(ct);

        return reversalRows
            .GroupBy(entry => entry.OriginalEntryId)
            .ToDictionary(
                group => group.Key,
                group => new ReversalTotals(
                    group.Sum(entry => entry.BaseAmount),
                    group.Sum(entry => entry.CommissionAmount)));
    }

    private static ReversalTotals? CalculateRefundCommissionReversalAuthority(
        AffiliateCommissionLedger earned,
        IReadOnlyDictionary<Guid, ReversalTotals> existingReversalTotalsByOriginalId,
        decimal refundedBaseAmount)
    {
        if (refundedBaseAmount <= 0m)
        {
            return null;
        }

        existingReversalTotalsByOriginalId.TryGetValue(earned.Id, out var existingTotals);
        var alreadyReversedBase = existingTotals?.BaseAmount ?? 0m;
        var alreadyReversedCommission = existingTotals?.CommissionAmount ?? 0m;

        if (alreadyReversedBase > earned.BaseAmount ||
            alreadyReversedCommission > earned.CommissionAmount)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_COMMISSION_REVERSAL_EXCEEDS_EARNED: Existing reversals exceed earned commission authority. EarnedEntryId={earned.Id}; OrderItemId={earned.OrderItemId}; EarnedBase={earned.BaseAmount:F4}; ReversedBase={alreadyReversedBase:F4}; EarnedCommission={earned.CommissionAmount:F4}; ReversedCommission={alreadyReversedCommission:F4}.");
        }

        var remainingBase = earned.BaseAmount - alreadyReversedBase;
        var remainingCommission = earned.CommissionAmount - alreadyReversedCommission;
        var baseToReverse = Math.Min(refundedBaseAmount, remainingBase);

        if (baseToReverse <= 0m || remainingCommission <= 0m)
        {
            return null;
        }

        var proportionalCommission = Math.Round(
            earned.CommissionAmount * (baseToReverse / earned.BaseAmount),
            4,
            MidpointRounding.ToZero);

        var commissionToReverse = Math.Min(proportionalCommission, remainingCommission);
        var isFinalReversal = baseToReverse >= remainingBase;
        if (isFinalReversal || remainingCommission - commissionToReverse < 0.0001m)
        {
            commissionToReverse = remainingCommission;
        }

        if (commissionToReverse <= 0m)
        {
            return null;
        }

        return new ReversalTotals(baseToReverse, commissionToReverse);
    }

    private static bool IsFullyReversed(
        AffiliateCommissionLedger earned,
        IReadOnlyDictionary<Guid, ReversalTotals> reversalTotalsByOriginalEntryId)
    {
        if (!reversalTotalsByOriginalEntryId.TryGetValue(earned.Id, out var totals))
        {
            return false;
        }

        return totals.BaseAmount >= earned.BaseAmount ||
               totals.CommissionAmount >= earned.CommissionAmount;
    }

    private static string BuildRefundReversalIdempotencyKey(
        string refundReference,
        Guid orderItemId,
        decimal refundedBaseAmount) =>
        $"AFFILIATE_COMM:REVERSED:{refundReference}:{orderItemId:N}:{refundedBaseAmount.ToString("F4", CultureInfo.InvariantCulture)}";

    private sealed record ReversalTotals(decimal BaseAmount, decimal CommissionAmount);

    private async Task ReconcileDeliveredCommissionApprovalsAsync(
        Order order,
        IReadOnlyCollection<AffiliateCommissionLedger> earnedEntries,
        CancellationToken ct)
    {
        if (!order.IsPhysicallyDelivered || earnedEntries.Count == 0)
        {
            return;
        }

        EnsureAffiliateDeliveryApprovalAuthority(
            order,
            order.TenantId,
            deliveredAtUtc: order.DeliveredAt ?? DateTime.UtcNow,
            sourceEvent: "RecordCommissionDeliveredOrderReconciliation",
            eventVersion: 1,
            deliveryReference: order.DeliveryReference,
            proofOfDeliveryUrl: order.ProofOfDeliveryUrl);

        var existingTerminalEntries = await _context.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(entry => entry.OrderId == order.Id &&
                            (entry.EntryType == CommissionEntryType.Approved ||
                             entry.EntryType == CommissionEntryType.Reversed))
            .Select(entry => new
            {
                entry.OrderItemId,
                entry.EntryType,
                entry.OriginalEntryId,
                entry.BaseAmount,
                entry.CommissionAmount
            })
            .ToListAsync(ct);

        var approvedItemIds = existingTerminalEntries
            .Where(entry => entry.EntryType == CommissionEntryType.Approved)
            .Select(entry => entry.OrderItemId)
            .ToHashSet();

        var reversalTotalsByOriginalEntryId = existingTerminalEntries
            .Where(entry => entry.EntryType == CommissionEntryType.Reversed)
            .Where(entry => entry.OriginalEntryId.HasValue)
            .GroupBy(entry => entry.OriginalEntryId!.Value)
            .ToDictionary(
                group => group.Key,
                group => new ReversalTotals(
                    group.Sum(entry => entry.BaseAmount),
                    group.Sum(entry => entry.CommissionAmount)));

        var approvedEntries = new List<AffiliateCommissionLedger>();

        foreach (var earned in earnedEntries)
        {
            if (approvedItemIds.Contains(earned.OrderItemId) ||
                IsFullyReversed(earned, reversalTotalsByOriginalEntryId))
            {
                continue;
            }

            var approved = AffiliateCommissionLedger.CreateApproved(earned);

            try
            {
                _context.AffiliateCommissionLedger.Add(approved);
                await _context.SaveChangesAsync(ct);
                approvedEntries.Add(approved);
                approvedItemIds.Add(earned.OrderItemId);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _context.Entry(approved).State = EntityState.Detached;
                _telemetry.RecordIdempotentSkip(earned.StoreTenantId, "approved");
            }
        }

        if (approvedEntries.Count == 0)
        {
            return;
        }

        var affiliateAccountId = approvedEntries[0].AffiliateAccountId;
        var storeTenantId = approvedEntries[0].StoreTenantId;
        var affiliateAuthorityTenantId = await ResolveAffiliateAuthorityTenantIdAsync(
            affiliateAccountId,
            ct);

        await _eventPublisher.PublishAsync(new AffiliateCommissionApprovedEvent
        {
            TenantId = storeTenantId,
            StoreTenantId = storeTenantId,
            AffiliateAuthorityTenantId = affiliateAuthorityTenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            AffiliateAccountId = affiliateAccountId,
            TotalApproved = approvedEntries.Sum(entry => entry.CommissionAmount),
            Currency = approvedEntries[0].Currency,
            ItemCount = approvedEntries.Count,
            CorrelationId = order.Id.ToString()
        }, ct);

        _logger.LogWarning(
            "[Affiliate:RecordCommission] Reconciled previously delivered order while recording commission | OrderId: {OrderId} | ApprovedItems: {Count}",
            order.Id,
            approvedEntries.Count);
    }

    private async Task<bool> ShouldRetryMissingEarnedCommissionForRefundAsync(
        Guid tenantId,
        Guid orderId,
        Guid orderItemId,
        CancellationToken ct)
    {
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .Include(item => item.Items)
            .FirstOrDefaultAsync(item =>
                item.Id == orderId &&
                item.TenantId == tenantId &&
                !item.IsDeleted,
                ct);

        if (order == null)
        {
            return false;
        }

        var orderItem = order.Items.FirstOrDefault(item => item.Id == orderItemId);
        if (orderItem == null)
        {
            return false;
        }

        // AF-HARDENING: Don't retry if commission base is zero (fully discounted/free items)
        var commissionBase = orderItem.AffiliateCommissionBaseAmountSnapshot ?? orderItem.Total;
        if (commissionBase <= 0m)
        {
            return false;
        }

        return HasAffiliateCommissionExpectation(order) &&
               IsAffiliateCommissionExpectedForItem(orderItem);
    }

    private async Task EnsureCommissionReadyForDeliveredAffiliateOrderAsync(
        Guid orderId,
        CancellationToken ct)
    {
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .Include(item => item.Items)
            .FirstOrDefaultAsync(item => item.Id == orderId && !item.IsDeleted, ct);

        if (order is not { IsPhysicallyDelivered: true } ||
            !HasAffiliateCommissionExpectation(order))
        {
            return;
        }

        // AF-HARDENING: Don't retry if no items have positive commission base (all fully discounted/free)
        var hasPositiveCommissionBase = order.Items.Any(item =>
        {
            var commissionBase = item.AffiliateCommissionBaseAmountSnapshot ?? item.Total;
            return commissionBase > 0m && IsAffiliateCommissionExpectedForItem(item);
        });

        if (!hasPositiveCommissionBase)
        {
            return;
        }

        _logger.LogWarning(
            "[Affiliate:Approve] Delivered affiliate order has no earned commission yet - retrying | OrderId: {OrderId}",
            orderId);

        throw new AffiliateCommissionDeferredException(
            orderId,
            "AFFILIATE_COMMISSION_NOT_READY_FOR_DELIVERY_APPROVAL: No earned commission entry exists yet");
    }

    public async Task ReverseCommissionForOrderItemAsync(
        Guid tenantId,
        Guid orderId,
        Guid orderItemId,
        CancellationToken ct = default,
        decimal? refundedBaseAmount = null,
        string? refundIdempotencyKey = null)
    {
        // CRITICAL: Use IgnoreQueryFilters — called from MassTransit consumer (no HTTP tenant context).
        // Find the original Earned entry
        var original = await _context.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e =>
                e.StoreTenantId == tenantId &&
                e.OrderId == orderId &&
                e.OrderItemId == orderItemId &&
                e.EntryType == CommissionEntryType.Earned, ct);

        if (original == null)
        {
            if (await ShouldRetryMissingEarnedCommissionForRefundAsync(tenantId, orderId, orderItemId, ct))
            {
                _logger.LogWarning(
                    "[Affiliate:Reverse] Earned commission is not ready for refunded affiliate item - retrying | OrderId: {OrderId} | OrderItemId: {OrderItemId}",
                    orderId,
                    orderItemId);
                throw new InvalidOperationException(
                    $"AFFILIATE_COMMISSION_NOT_READY_FOR_REFUND_REVERSAL: Order {orderId} item {orderItemId} is affiliate-eligible/refunded but has no earned commission entry yet.");
            }

            _logger.LogDebug(
                "No earned commission for OrderItem {OrderItemId} — nothing to reverse",
                orderItemId);
            // AF-2.5: Not a skip per se — no earned entry means nothing to reverse
            return;
        }

        // Load order for event payload (IgnoreQueryFilters for consumer context)
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o =>
                o.Id == orderId &&
                o.TenantId == tenantId &&
                !o.IsDeleted,
                ct);

        var existingReversalTotalsByOriginalId = await LoadReversalTotalsByOriginalEntryIdAsync(
            new[] { original.Id },
            ct);

        var reversalAuthority = CalculateRefundCommissionReversalAuthority(
            original,
            existingReversalTotalsByOriginalId,
            refundedBaseAmount ?? original.BaseAmount);

        if (reversalAuthority == null)
        {
            _logger.LogInformation(
                "Commission reversal already covers refunded amount for OrderItem {OrderItemId} — idempotent skip",
                orderItemId);
            _telemetry.RecordIdempotentSkip(original.StoreTenantId, "reversed");
            return;
        }

        var idempotencyKey = !string.IsNullOrWhiteSpace(refundIdempotencyKey)
            ? refundIdempotencyKey
            : BuildRefundReversalIdempotencyKey("FULL", orderItemId, reversalAuthority.BaseAmount);

        var reversal = AffiliateCommissionLedger.CreateReversalWithCustomKey(
            original.StoreTenantId,
            original.Id,
            original.AttributionId,
            original.AffiliateAccountId,
            orderId,
            orderItemId,
            reversalAuthority.BaseAmount,
            reversalAuthority.CommissionAmount,
            original.CommissionRate,
            original.Currency,
            idempotencyKey);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            _context.AffiliateCommissionLedger.Add(reversal);
            await _context.SaveChangesAsync(ct);

            // AF-2.5: Record reversal success (use StoreTenantId for telemetry)
            _telemetry.RecordRefundSuccess(original.StoreTenantId);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(ct);
            _context.Entry(reversal).State = EntityState.Detached;
            _telemetry.RecordIdempotentSkip(original.StoreTenantId, "reversed");

            _logger.LogInformation(
                "Commission reversal already exists for OrderItem {OrderItemId} — idempotent skip",
                orderItemId);
            return;
        }

        var affiliateAuthorityTenantId = await ResolveAffiliateAuthorityTenantIdAsync(
            original.AffiliateAccountId,
            ct);

        // OUTBOX RULE: Event published ONLY AFTER successful DB commit.
        // AF-19C: Add StoreTenantId for correct MongoDB projection
        await _eventPublisher.PublishAsync(new AffiliateCommissionReversedEvent
        {
            TenantId = original.StoreTenantId, // AF-19B: Store where sale happened
            StoreTenantId = original.StoreTenantId, // AF-19C: For MongoDB projection
            AffiliateAuthorityTenantId = affiliateAuthorityTenantId,
            OrderId = orderId,
            OrderNumber = order?.OrderNumber ?? string.Empty,
            OrderItemId = orderItemId,
            AffiliateAccountId = original.AffiliateAccountId,
            OriginalLedgerEntryId = original.Id,
            ReversalLedgerEntryId = reversal.Id,
            ReversedAmount = reversal.CommissionAmount,
            Currency = reversal.Currency,
            CorrelationId = orderId.ToString()
        }, ct);

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Reversed commission for OrderItem {OrderItemId}: amount={Amount}",
            orderItemId, reversal.CommissionAmount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SETTLEMENT (AF-7)
    //
    // Append-only settlement lifecycle: Earned → Approved → Paid
    // No recalculation — financial data is copied from predecessor entries.
    // Reversal cancels Earned and blocks further settlement.
    // ═══════════════════════════════════════════════════════════════════

    public Task ApproveCommissionForOrderAsync(Guid orderId, CancellationToken ct = default) =>
        ApproveCommissionForDeliveredOrderAsync(
            orderId,
            eventTenantId: null,
            deliveredAtUtc: default,
            sourceEvent: "ManualOrLegacyAffiliateApproval",
            eventVersion: 1,
            ct: ct);

    public async Task ApproveCommissionForDeliveredOrderAsync(
        Guid orderId,
        Guid? eventTenantId,
        DateTime deliveredAtUtc,
        string sourceEvent,
        int eventVersion,
        CancellationToken ct = default,
        string? deliveryReference = null,
        string? proofOfDeliveryUrl = null)
    {
        if (eventVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Affiliate delivery approval requires a positive event version. OrderId={orderId}; SourceEvent={sourceEvent}; EventVersion={eventVersion}.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await LoadAffiliateApprovalOrderAuthorityAsync(orderId, ct);
        EnsureAffiliateDeliveryApprovalAuthority(
            order,
            eventTenantId,
            deliveredAtUtc,
            sourceEvent,
            eventVersion,
            deliveryReference,
            proofOfDeliveryUrl);

        var allEntries = await _context.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(e => e.OrderId == orderId)
            .ToListAsync(ct);

        if (allEntries.Count == 0)
        {
            await EnsureCommissionReadyForDeliveredAffiliateOrderAsync(orderId, ct);

            _logger.LogWarning(
                "[Affiliate:Approve] No ledger entries for delivered Order {OrderId} — skipping | SourceEvent: {SourceEvent}",
                orderId, sourceEvent);
            return;
        }

        // AF-19B: Validate that ALL entries have StoreTenantId matching order.TenantId
        // Reject the entire batch if any entry has a mismatched tenant
        var mismatchedEntries = allEntries
            .Where(e => e.StoreTenantId != order.TenantId)
            .ToList();

        if (mismatchedEntries.Any())
        {
            var mismatchedTenants = string.Join(", ", mismatchedEntries.Select(e => e.StoreTenantId).Distinct());
            throw new InvalidOperationException(
                $"Affiliate commission ledger entries have StoreTenantId mismatch with order tenant. " +
                $"OrderId={orderId}; OrderTenant={order.TenantId}; LedgerTenant(s)={mismatchedTenants}; SourceEvent={sourceEvent}.");
        }

        var earnedEntries = allEntries
            .Where(e => e.EntryType == CommissionEntryType.Earned)
            .ToList();

        if (earnedEntries.Count == 0)
        {
            await EnsureCommissionReadyForDeliveredAffiliateOrderAsync(orderId, ct);

            _logger.LogWarning(
                "[Affiliate:Approve] No Earned entries for delivered Order {OrderId} — nothing to approve | SourceEvent: {SourceEvent}",
                orderId, sourceEvent);
            return;
        }

        var reversalTotalsByOriginalEntryId = allEntries
            .Where(e => e.EntryType == CommissionEntryType.Reversed)
            .Where(e => e.OriginalEntryId.HasValue)
            .GroupBy(e => e.OriginalEntryId!.Value)
            .ToDictionary(
                group => group.Key,
                group => new ReversalTotals(
                    group.Sum(entry => entry.BaseAmount),
                    group.Sum(entry => entry.CommissionAmount)));

        var approvedItemIds = allEntries
            .Where(e => e.EntryType == CommissionEntryType.Approved)
            .Select(e => e.OrderItemId)
            .ToHashSet();

        var approvedEntries = new List<AffiliateCommissionLedger>();
        var skippedReversed = 0;
        var idempotentSkips = 0;

        foreach (var earned in earnedEntries)
        {
            if (IsFullyReversed(earned, reversalTotalsByOriginalEntryId))
            {
                _logger.LogDebug(
                    "[Affiliate:Approve] OrderItem {OrderItemId} was fully reversed — skipping approval",
                    earned.OrderItemId);
                skippedReversed++;
                continue;
            }

            if (approvedItemIds.Contains(earned.OrderItemId))
            {
                idempotentSkips++;
                _telemetry.RecordIdempotentSkip(earned.StoreTenantId, "approved");
                continue;
            }

            var approved = AffiliateCommissionLedger.CreateApproved(earned);
            _context.AffiliateCommissionLedger.Add(approved);
            approvedEntries.Add(approved);
            approvedItemIds.Add(earned.OrderItemId);
        }

        // CONCURRENCY GUARD: Before persisting, re-check for Approved entries that may have been
        // inserted by concurrent consumers after our initial snapshot. The approvedItemIds snapshot
        // was taken at the start of the transaction, but another consumer could have committed
        // Approved rows between then and now, causing a unique constraint violation on
        // IdempotencyKey (AFFILIATE_COMM:APPROVED:{OrderItemId}).
        //
        // Filter out any approvedEntries whose OrderItemId now exists in the DB as Approved.
        // This makes the operation idempotent and prevents duplicate commission approval.
        if (approvedEntries.Count > 0)
        {
            var candidateOrderItemIds = approvedEntries.Select(e => e.OrderItemId).ToHashSet();

            var existingApprovedItemIds = await _context.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .Where(e => e.OrderId == orderId &&
                           e.EntryType == CommissionEntryType.Approved &&
                           candidateOrderItemIds.Contains(e.OrderItemId))
                .Select(e => e.OrderItemId)
                .ToListAsync(ct);

            if (existingApprovedItemIds.Count > 0)
            {
                var existingSet = existingApprovedItemIds.ToHashSet();
                var filteredEntries = approvedEntries
                    .Where(e => !existingSet.Contains(e.OrderItemId))
                    .ToList();

                var concurrentSkips = approvedEntries.Count - filteredEntries.Count;
                if (concurrentSkips > 0)
                {
                    _logger.LogWarning(
                        "[Affiliate:Approve] Order {OrderId}: {Count} items already approved by concurrent consumer — skipping duplicates | SourceEvent: {SourceEvent}",
                        orderId, concurrentSkips, sourceEvent);

                    // Remove the duplicate entries from EF tracking
                    foreach (var duplicate in approvedEntries.Where(e => existingSet.Contains(e.OrderItemId)))
                    {
                        _context.Entry(duplicate).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    }

                    idempotentSkips += concurrentSkips;
                }

                approvedEntries = filteredEntries;
            }
        }

        if (approvedEntries.Count > 0)
        {
            var affiliateAccountId = approvedEntries[0].AffiliateAccountId;
            var storeTenantId = approvedEntries[0].StoreTenantId;
            var affiliateAuthorityTenantId = await ResolveAffiliateAuthorityTenantIdAsync(
                affiliateAccountId,
                ct);

            await _eventPublisher.PublishAsync(new AffiliateCommissionApprovedEvent
            {
                TenantId = storeTenantId,
                StoreTenantId = storeTenantId,
                AffiliateAuthorityTenantId = affiliateAuthorityTenantId,
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                AffiliateAccountId = affiliateAccountId,
                TotalApproved = approvedEntries.Sum(e => e.CommissionAmount),
                Currency = approvedEntries[0].Currency,
                ItemCount = approvedEntries.Count,
                CorrelationId = orderId.ToString()
            }, ct);

            _logger.LogInformation(
                "[Affiliate:Approve] Order {OrderId}: {Count} items approved, total={Total} " +
                "(skippedReversed={Reversed}, idempotent={Idempotent}) | SourceEvent: {SourceEvent} | EventVersion: {EventVersion}",
                orderId, approvedEntries.Count,
                approvedEntries.Sum(e => e.CommissionAmount),
                skippedReversed, idempotentSkips, sourceEvent, eventVersion);

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // IDEMPOTENT RACE: Another concurrent consumer committed Approved entries between
                // our pre-save check and SaveChangesAsync. The unique constraint on IdempotencyKey
                // (AFFILIATE_COMM:APPROVED:{OrderItemId}) prevents duplicates.
                //
                // Detach the conflicting entries from EF tracking and treat this as an idempotent
                // success. The commission was already approved by the concurrent consumer.
                _logger.LogWarning(
                    "[Affiliate:Approve] Order {OrderId}: Unique constraint violation on SaveChanges — " +
                    "concurrent consumer already approved these items. Detaching {Count} entries and treating as idempotent success | SourceEvent: {SourceEvent}",
                    orderId, approvedEntries.Count, sourceEvent);

                foreach (var entry in approvedEntries)
                {
                    _context.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                }

                // CRITICAL: Also detach any outbox/event entities that were staged by PublishAsync
                // to prevent them from being persisted on subsequent SaveChangesAsync calls.
                // The event was already published by the concurrent consumer that won the race.
                var stagedOutboxEntries = _context.ChangeTracker.Entries()
                    .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added &&
                                (e.Entity.GetType().Name.Contains("OutboxMessage") ||
                                 e.Entity.GetType().Name.Contains("OutboxState") ||
                                 e.Entity.GetType().Name.Contains("InboxState")))
                    .ToList();

                foreach (var outboxEntry in stagedOutboxEntries)
                {
                    outboxEntry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                }

                if (stagedOutboxEntries.Count > 0)
                {
                    _logger.LogInformation(
                        "[Affiliate:Approve] Order {OrderId}: Detaching {Count} staged outbox/event entities to prevent duplicate event publication | SourceEvent: {SourceEvent}",
                        orderId, stagedOutboxEntries.Count, sourceEvent);
                }

                idempotentSkips += approvedEntries.Count;

                _logger.LogInformation(
                    "[Affiliate:Approve] Order {OrderId}: Idempotent race handled — " +
                    "total idempotent skips: {Idempotent} | SourceEvent: {SourceEvent}",
                    orderId, idempotentSkips, sourceEvent);
            }
        }
        else
        {
            _logger.LogInformation(
                "[Affiliate:Approve] Order {OrderId}: No new items to approve after idempotency check " +
                "(skippedReversed={Reversed}, idempotent={Idempotent}) | SourceEvent: {SourceEvent} | EventVersion: {EventVersion}",
                orderId, skippedReversed, idempotentSkips, sourceEvent, eventVersion);
        }

        await transaction.CommitAsync(ct);
    }

    private async Task<Order> LoadAffiliateApprovalOrderAuthorityAsync(Guid orderId, CancellationToken ct)
    {
        var providerName = _context.Database.ProviderName ?? string.Empty;
        Order? order;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            order = await _context.Orders
                .FromSqlInterpolated($@"
                    SELECT *, xmin
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(ct);
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            order = await _context.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            order = await _context.Orders
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct);
        }

        if (order == null)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_DELIVERY_APPROVAL_ORDER_NOT_FOUND: Cannot approve affiliate commission because order {orderId} was not found.");
        }

        return order;
    }

    private void EnsureAffiliateDeliveryApprovalAuthority(
        Order order,
        Guid? eventTenantId,
        DateTime deliveredAtUtc,
        string sourceEvent,
        int eventVersion,
        string? deliveryReference,
        string? proofOfDeliveryUrl)
    {
        if (eventTenantId.HasValue &&
            eventTenantId.Value != Guid.Empty &&
            eventTenantId.Value != order.TenantId)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_DELIVERY_EVENT_TENANT_MISMATCH: Delivery event tenant does not match order tenant. OrderId={order.Id}; OrderTenant={order.TenantId}; EventTenant={eventTenantId}; SourceEvent={sourceEvent}; EventVersion={eventVersion}.");
        }

        if (!order.IsPhysicallyDelivered)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_DELIVERY_AUTHORITY_NOT_CONFIRMED: Cannot approve affiliate commission until the authoritative order is physically delivered. OrderId={order.Id}; FulfillmentStatus={order.FulfillmentStatus}; OrderStatus={order.Status}; SourceEvent={sourceEvent}; EventVersion={eventVersion}.");
        }

        if (order.FulfillmentMode == FulfillmentMode.Manual &&
            (string.IsNullOrWhiteSpace(deliveryReference) || string.IsNullOrWhiteSpace(proofOfDeliveryUrl)))
        {
            throw new InvalidOperationException(
                $"AFFILIATE_MANUAL_DELIVERY_EVIDENCE_REQUIRED: Manual fulfillment cannot approve affiliate commission without delivery reference and proof of delivery evidence. OrderId={order.Id}; SourceEvent={sourceEvent}; EventVersion={eventVersion}.");
        }

        if (order.FulfillmentMode == FulfillmentMode.Manual &&
            _settlementOptions.RequireVerifiedManualDeliveryEvidence)
        {
            if (_settlementOptions.AllowManualDeliveryEvidenceOverride)
            {
                _logger.LogCritical(
                    "[Affiliate:Delivery:BreakGlass] Manual delivery evidence override is enabled. " +
                    "OrderId={OrderId} | SourceEvent={SourceEvent} | EventVersion={EventVersion}",
                    order.Id,
                    sourceEvent,
                    eventVersion);
            }
            else
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_MANUAL_DELIVERY_VERIFIED_EVIDENCE_REQUIRED: Manual fulfillment requires verified delivery evidence from a trusted fulfillment or proof authority before affiliate commission can be approved. OrderId={order.Id}; SourceEvent={sourceEvent}; EventVersion={eventVersion}.");
            }
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_DELIVERY_ORDER_AUTHORITY_NOT_ELIGIBLE: Cannot approve affiliate commission for a cancelled or fully refunded order. OrderId={order.Id}; OrderStatus={order.Status}; SourceEvent={sourceEvent}; EventVersion={eventVersion}.");
        }

        if (order.Total > 0m && !HasPendingCommissionPaymentAuthority(order.PaymentStatus))
        {
            throw new AffiliateCommissionDeferredException(
                order.Id,
                $"AFFILIATE_DELIVERY_PAYMENT_AUTHORITY_NOT_CONFIRMED: PaymentStatus={order.PaymentStatus}; SourceEvent={sourceEvent}; EventVersion={eventVersion}");
        }

        if (order.Total > 0m &&
            order.FinancialPostingStatus is not (FinancialPostingStatus.Posted or FinancialPostingStatus.NotRequired))
        {
            throw new AffiliateCommissionDeferredException(
                order.Id,
                $"AFFILIATE_DELIVERY_ACCOUNTING_AUTHORITY_NOT_CONFIRMED: FinancialPostingStatus={order.FinancialPostingStatus}; SourceEvent={sourceEvent}; EventVersion={eventVersion}");
        }

        _ = deliveredAtUtc;
    }

    /// <summary>
    /// AF-38H: Records commission payment for an order (Legacy Settlement Flow).
    /// 
    /// DELEGATES TO UNIFIED ORCHESTRATOR:
    /// All monetary operations (BalanceLedger + CommissionLedger + Reconciliation)
    /// are handled by IAffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync.
    /// 
    /// This method now only:
    /// 1. Validates Approved entries exist for the order
    /// 2. Delegates payout execution to the orchestrator
    /// 3. Publishes the AffiliateCommissionPaidEvent
    /// 
    /// Idempotent: orchestrator handles duplicate detection.
    /// </summary>
    public async Task RecordCommissionPaidAsync(
        Guid orderId,
        string paymentReference,
        string? adminNotes = null,
        decimal? externalTransferAmount = null,
        CancellationToken ct = default)
    {
        // Load Approved entries for this order
        var approvedEntries = await _context.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(e => e.OrderId == orderId && e.EntryType == CommissionEntryType.Approved)
            .ToListAsync(ct);

        if (approvedEntries.Count == 0)
        {
            _logger.LogWarning(
                "[Affiliate:Paid] No Approved entries for Order {OrderId} — cannot record payment",
                orderId);
            return;
        }

        // AUDIT FIX F-E1: Use StoreTenantId, not TenantId. TenantId is always NULL
        // for global affiliates (AF-19B), which would cause events with TenantId=NULL
        // and break MongoDB projections.
        var tenantId = approvedEntries[0].StoreTenantId;
        var affiliateAccountId = approvedEntries[0].AffiliateAccountId;
        var currency = approvedEntries[0].Currency;

        // Compute the actual payable amount (same logic as orchestrator's BuildExactPayoutPlan)
        // This filters out already-paid and reversed entries to get the true amount that can be paid
        var reversalAmountsByOrderItem = await _context.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(e =>
                e.AffiliateAccountId == affiliateAccountId &&
                e.StoreTenantId == tenantId &&
                e.EntryType == CommissionEntryType.Reversed)
            .GroupBy(e => e.OrderItemId)
            .Select(group => new
            {
                OrderItemId = group.Key,
                CommissionAmount = group.Sum(entry => entry.CommissionAmount)
            })
            .ToDictionaryAsync(item => item.OrderItemId, item => item.CommissionAmount, ct);

        var alreadyPaidOrderItemIds = await _context.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(e =>
                e.AffiliateAccountId == affiliateAccountId &&
                e.StoreTenantId == tenantId &&
                e.EntryType == CommissionEntryType.Paid)
            .Select(e => e.OrderItemId)
            .ToHashSetAsync(ct);

        // Calculate the actual payable amount (after filtering)
        var payableAmount = approvedEntries
            .Where(e => !alreadyPaidOrderItemIds.Contains(e.OrderItemId))
            .Sum(e => Math.Max(
                e.CommissionAmount - reversalAmountsByOrderItem.GetValueOrDefault(e.OrderItemId),
                0m));

        // Guard: Skip if nothing to pay (all items already paid or reversed)
        if (payableAmount <= 0)
        {
            _logger.LogInformation(
                "[AF-38H:Paid] Order {OrderId}: No payable amount after filtering (already paid or reversed) — skipping orchestrator call",
                orderId);
            return;
        }

        // Validate externalTransferAmount if provided
        if (externalTransferAmount.HasValue)
        {
            if (externalTransferAmount.Value <= 0)
            {
                throw new ArgumentException(
                    "External transfer amount must be greater than zero.",
                    nameof(externalTransferAmount));
            }

            if (externalTransferAmount.Value < payableAmount)
            {
                throw new ArgumentException(
                    $"External transfer amount ({externalTransferAmount.Value}) cannot be less than the actual payable commission amount ({payableAmount}).",
                    nameof(externalTransferAmount));
            }
        }

        // AF-38H + AF-38Y: Delegate to Unified Orchestrator
        // This atomically handles:
        // - Balance validation (V-1: using RawAvailable)
        // - BalanceLedger.Withdrawal creation
        // - CommissionLedger.Paid entries (FIFO lifecycle sync for live payouts only)
        // - PayoutRequest reconciliation
        // - Serializable isolation (V-2)
        // - StoreTenantId enforcement (V-4)
        var result = await _orchestrator.ExecuteAffiliatePayoutAsync(
            affiliateAccountId,
            tenantId,           // AF-38Y V-4: StoreTenantId from commission entries
            payableAmount,
            SettlementSource.LegacySettlement,
            paymentReference,
            orderId: orderId,
            reference: $"Order #{orderId}",
            adminNotes: adminNotes,
            externalTransferAmount: externalTransferAmount,
            ct: ct);

        _logger.LogInformation(
            "[AF-38H:Paid] Order {OrderId}: {Count} items paid via orchestrator, total={Total} | OrdersPaid={OrderCount}",
            orderId, approvedEntries.Count, payableAmount, result.PaidOrders.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static bool HasAffiliateCommissionExpectation(Order order) =>
        order.AffiliateAccountId.HasValue ||
        !string.IsNullOrWhiteSpace(order.AffiliateReferralCode) ||
        order.Items.Any(IsAffiliateCommissionExpectedForItem);

    private static bool IsAffiliateCommissionExpectedForItem(OrderItem item) =>
        item.AffiliateCommissionEligible ||
        item.AffiliateCommissionSnapshotCapturedAtUtc.HasValue ||
        item.AffiliateCommissionPolicyIdSnapshot.HasValue ||
        item.AffiliateCommissionAmountSnapshot.GetValueOrDefault() > 0m;

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg && pg.SqlState == "23505";
    }

    private static bool IsAffiliatePolicyWriteConflict(Exception ex) =>
        ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
        ex is DbUpdateException dbUpdateException &&
        (IsUniqueViolation(dbUpdateException) ||
         dbUpdateException.InnerException is PostgresException
         {
             SqlState: PostgresErrorCodes.SerializationFailure
         });

    /// <summary>
    /// AF-36: Determines if a payment method requires admin verification before
    /// commission, conversion, and ranking signals should be triggered.
    /// Manual wallet payments (Vodafone Cash, InstaPay, etc.) return true.
    /// </summary>
    private static bool RequiresPaymentVerification(string? paymentMethodCode)
    {
        if (string.IsNullOrEmpty(paymentMethodCode))
        {
            return false;
        }

        return paymentMethodCode.Equals("MANUALWALLET", StringComparison.OrdinalIgnoreCase) ||
               paymentMethodCode.Equals("WALLET", StringComparison.OrdinalIgnoreCase) ||
               paymentMethodCode.Contains("VODAFONE", StringComparison.OrdinalIgnoreCase) ||
               paymentMethodCode.Contains("INSTAPAY", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid?> ResolveAffiliateAuthorityTenantIdAsync(Guid affiliateAccountId, CancellationToken ct)
    {
        var tenantId = await _context.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(account => account.Id == affiliateAccountId)
            .Select(account => (Guid?)account.TenantId)
            .FirstOrDefaultAsync(ct);

        // Normalize Guid.Empty to null for global/unscoped affiliates
        if (tenantId.HasValue && tenantId.Value == Guid.Empty)
        {
            tenantId = null;
        }

        return tenantId;
    }

    private static Guid? NormalizeAffiliateAuthorityTenantId(Guid? authorityTenantId) =>
        authorityTenantId.HasValue && authorityTenantId.Value != Guid.Empty
            ? authorityTenantId.Value
            : null;
}

