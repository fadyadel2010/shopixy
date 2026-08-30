using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using System.Security.Claims;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-4B: Admin Affiliate Management API.
///
/// ARCHITECTURE RULES:
/// 1. MongoDB projections are display-only and must never drive settlement decisions
/// 2. AffiliateCommissionLedger is the financial authority for approval/payment eligibility and payout totals
/// 3. Write operations go through IAffiliateService ONLY
/// 4. Controller NEVER writes ledger rows
/// 5. Tenant isolation enforced via trusted tenant context
/// 6. Return 404 for cross-tenant attempts (never 403)
///
/// ENDPOINTS:
/// GET    /api/admin/affiliates                  → List all affiliates (Postgres + Mongo join)
/// GET    /api/admin/affiliates/{id}              → Affiliate detail (Postgres + Mongo)
/// GET    /api/admin/affiliates/{id}/orders        → Affiliate orders (Mongo)
/// GET    /api/admin/affiliates/{id}/earnings      → Affiliate earnings (Mongo)
/// POST   /api/admin/affiliates/{id}/approve       → Approve earned commissions (IAffiliateService)
/// POST   /api/admin/affiliates/{id}/pay           → Record commission payment (IAffiliateService)
/// POST   /api/admin/affiliates/{id}/suspend       → Suspend affiliate (IAffiliateService)
/// POST   /api/admin/affiliates/{id}/activate      → Activate affiliate (IAffiliateService)
/// </summary>
[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/admin/affiliates")]
public class AdminAffiliateController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly IAffiliateService _affiliateService;
    private readonly IAffiliatePayoutService _payoutService;
    private readonly IAffiliateSettlementOrchestrator _orchestrator;
    private readonly IAffiliateReadinessService _affiliateReadiness;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITreasuryReadService _treasuryReadService;
    private readonly ILogger<AdminAffiliateController> _logger;
    private readonly ITenantContext _tenantContext;

    private Guid TenantId => _tenantContext.RequiredTenantId;

    public AdminAffiliateController(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        IAffiliateService affiliateService,
        IAffiliatePayoutService payoutService,
        IAffiliateSettlementOrchestrator orchestrator,
        IAffiliateReadinessService affiliateReadiness,
        IEventPublisher eventPublisher,
        ITreasuryReadService treasuryReadService,
        ITenantContext tenantContext,
        ILogger<AdminAffiliateController> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _affiliateService = affiliateService;
        _payoutService = payoutService;
        _orchestrator = orchestrator;
        _affiliateReadiness = affiliateReadiness;
        _eventPublisher = eventPublisher;
        _treasuryReadService = treasuryReadService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // DIAGNOSTIC ENDPOINT (AUDIT FIX F-A1: Only available in DEBUG builds)
    // ═══════════════════════════════════════════════════════════════════

#if DEBUG
    /// <summary>
    /// TEMPORARY: Debug MongoDB data for affiliate
    /// SECURITY: Only available in DEBUG builds (gated by preprocessor directive).
    /// </summary>
    [HttpGet("debug/{id:guid}/mongo")]
    public async Task<ActionResult> DebugMongoData(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;

        // Check dashboards
        var dashboards = await _mongoDbContext.AffiliateDashboards.Find(
            Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, id)
        ).ToListAsync(ct);

        // Check orders
        var orders = await _mongoDbContext.AffiliateOrders.Find(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id)
        ).ToListAsync(ct);

        // Check earnings
        var earnings = await _mongoDbContext.AffiliateEarningsSummaries.Find(
            Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, id)
        ).ToListAsync(ct);

        return Ok(new
        {
            currentTenantId = tenantId,
            dashboards = dashboards.Select(d => new
            {
                d.TenantId,
                d.TotalOrders,
                d.TotalEarned,
                d.TotalApproved,
                d.TotalPaid
            }).ToList(),
            orders = orders.Select(o => new
            {
                o.OrderId,
                o.OrderNumber,
                o.TenantId,
                o.Status,
                o.TotalCommission
            }).ToList(),
            earnings = earnings.Select(e => new
            {
                e.TenantId,
                e.Currency,
                e.TotalEarned,
                e.TotalApproved,
                e.TotalPaid
            }).ToList()
        });
    }
#endif

    // ═══════════════════════════════════════════════════════════════════
    // LIST AFFILIATES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/affiliates?page=1&pageSize=20&status=Active&search=john
    /// AF-20: Lists ONLY affiliates who have generated orders for the CURRENT STORE.
    /// Visibility rule: Affiliate appears in list when AffiliateCommissionLedger has earned entries for this tenant.
    /// Uses AffiliateCommissionLedger as the authority for store affiliate visibility.
    /// 
    /// AF-23: READINESS GATE — Requires Storefront Live + Payment + Shipping.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AdminAffiliateListResponse>> ListAffiliates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var tenantId = TenantId;

        // ─── AF-23: READINESS GATE ──────────────────────────────────────
        var readiness = await _affiliateReadiness.CheckAsync(tenantId, ct);
        if (!readiness.IsReady)
        {
            return BadRequest(new
            {
                code = "AFFILIATE_NOT_READY",
                message = "Affiliate system is not ready",
                missing = readiness.MissingRequirements
            });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        // AF-20: STEP 1 — Build subquery for affiliates who generated orders for this tenant
        // The SQL commission ledger is authoritative; dashboard documents are metric projections only.
        // PERFORMANCE: Use subquery instead of materializing IDs to let EF generate efficient SQL
        var activeAffiliateIdsSubquery = _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(entry =>
                entry.StoreTenantId == tenantId &&
                entry.EntryType == CommissionEntryType.Earned)
            .Select(entry => entry.AffiliateAccountId)
            .Distinct();

        // AF-20: STEP 2 — Load ONLY those affiliates from PostgreSQL using subquery
        var query = _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => activeAffiliateIdsSubquery.Contains(a.Id) && !a.IsDeleted);

        // Status filter
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AffiliateStatus>(status, true, out var statusEnum))
        {
            query = query.Where(a => a.Status == statusEnum);
        }

        // Search filter (DisplayName or ReferralCode)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(a =>
                a.DisplayName.ToLower().Contains(s) ||
                a.ReferralCode.ToLower().Contains(s));
        }

        var totalCount = await query.CountAsync(ct);

        var accounts = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.DisplayName,
                a.ReferralCode,
                Status = a.Status.ToString(),
                a.ActivatedAtUtc,
                a.CreatedAt,
                // AF-16: Payout Wallet
                a.PayoutWalletNumber,
                a.PayoutWalletType
            })
            .ToListAsync(ct);

        // AF-20: STEP 3 — Fetch SQL ledger totals for CURRENT STORE
        var affiliateIds = accounts.Select(a => a.Id).ToList();

        var dashboardMap = await BuildAffiliateLedgerTotalsByAffiliateAsync(affiliateIds, tenantId, ct);

        // AF-20: STEP 4 — Join and return
        var items = accounts.Select(a =>
        {
            dashboardMap.TryGetValue(a.Id, out var dash);
            return new AdminAffiliateListItem
            {
                AffiliateAccountId = a.Id,
                DisplayName = a.DisplayName,
                ReferralCode = a.ReferralCode,
                Status = a.Status,
                TotalEarned = dash?.TotalEarned ?? 0,
                TotalApproved = dash?.TotalApproved ?? 0,
                TotalPaid = dash?.TotalPaid ?? 0,
                NetEarnings = dash?.NetEarnings ?? 0,
                EarningsByCurrency = dash?.EarningsByCurrency ?? new List<AffiliateEarningsSummaryDto>(),
                TotalOrders = dash?.TotalOrders ?? 0,
                ActivatedAtUtc = a.ActivatedAtUtc,
                CreatedAtUtc = a.CreatedAt,
                // AF-16: Payout Wallet
                PayoutWalletNumber = a.PayoutWalletNumber,
                PayoutWalletType = a.PayoutWalletType
            };
        }).ToList();

        return Ok(new AdminAffiliateListResponse
        {
            Affiliates = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }



    // ═══════════════════════════════════════════════════════════════════
    // GET AFFILIATE DETAIL
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/affiliates/{id}
    /// AF-19B: Returns affiliate identity (Postgres) + ledger totals for the current store.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminAffiliateDetailResponse>> GetAffiliateDetail(
        Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;

        // AF-19B: Query affiliate globally (no tenant filter)
        var account = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => a.Id == id && !a.IsDeleted)
            .Select(a => new
            {
                a.Id,
                a.DisplayName,
                a.ReferralCode,
                Status = a.Status.ToString(),
                a.ActivatedAtUtc,
                a.SuspendedAtUtc,
                a.SuspensionReason,
                a.ClosedAtUtc,
                a.ClosureReason,
                a.CreatedAt,
                // AF-16: Payout Wallet (admin can read)
                a.PayoutWalletNumber,
                a.PayoutWalletType,
                a.PayoutWalletUpdatedAtUtc
            })
            .FirstOrDefaultAsync(ct);

        if (account == null)
            return NotFound(new { message = "Affiliate not found" });

        // SECURITY FIX: Verify affiliate has relationship with current tenant before exposing data
        // Check if affiliate has any ledger entries for this tenant (prevents cross-tenant data exposure)
        var hasRelationship = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AnyAsync(entry => 
                entry.AffiliateAccountId == id && 
                entry.StoreTenantId == tenantId, ct);

        if (!hasRelationship)
            return NotFound(new { message = "Affiliate not found" });

        var metrics = await BuildAffiliateLedgerTotalsAsync(id, tenantId, ct);

        return Ok(new AdminAffiliateDetailResponse
        {
            AffiliateAccountId = account.Id,
            DisplayName = account.DisplayName,
            ReferralCode = account.ReferralCode,
            Status = account.Status,
            ActivatedAtUtc = account.ActivatedAtUtc,
            SuspendedAtUtc = account.SuspendedAtUtc,
            SuspensionReason = account.SuspensionReason,
            ClosedAtUtc = account.ClosedAtUtc,
            ClosureReason = account.ClosureReason,
            CreatedAtUtc = account.CreatedAt,
            TotalEarned = metrics.TotalEarned,
            TotalApproved = metrics.TotalApproved,
            TotalPaid = metrics.TotalPaid,
            TotalReversed = metrics.TotalReversed,
            NetEarnings = metrics.NetEarnings,
            EarningsByCurrency = metrics.EarningsByCurrency,
            TotalOrders = metrics.TotalOrders,
            LastCommissionAtUtc = metrics.LastCommissionAtUtc,
            CashboxBalance = await GetCashboxBalanceAsync(tenantId, ct),
            // AF-16: Payout Wallet (admin read-only)
            PayoutWalletNumber = account.PayoutWalletNumber,
            PayoutWalletType = account.PayoutWalletType,
            PayoutWalletUpdatedAtUtc = account.PayoutWalletUpdatedAtUtc
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // AFFILIATE ORDERS (paginated)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/affiliates/{id}/orders?page=1&pageSize=20&status=Earned
    /// AF-19B: Returns paginated commission orders from MongoDB for a specific affiliate across ALL stores.
    /// </summary>
    [HttpGet("{id:guid}/orders")]
    public async Task<ActionResult<AffiliateOrdersResponse>> GetAffiliateOrders(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var tenantId = TenantId;

        // AF-19B: Verify affiliate exists globally (no tenant filter)
        var exists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);

        if (!exists)
            return NotFound(new { message = "Affiliate not found" });

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // AF-19B: Filter by AffiliateAccountId AND TenantId (show only CURRENT STORE's orders)
        var filters = new List<FilterDefinition<AffiliateOrderDocument>>
        {
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, id),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId)
        };

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusOrderIds = await BuildAffiliateOrderIdsForLedgerStatusAsync(id, tenantId, status, ct);

            if (statusOrderIds.Count == 0)
            {
                return Ok(new AffiliateOrdersResponse
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = 0,
                    TotalPages = 0
                });
            }

            filters.Add(Builders<AffiliateOrderDocument>.Filter.In(d => d.OrderId, statusOrderIds));
        }

        var filter = Builders<AffiliateOrderDocument>.Filter.And(filters);

        var totalCount = await _mongoDbContext.AffiliateOrders.CountDocumentsAsync(filter, cancellationToken: ct);

        var docs = await _mongoDbContext.AffiliateOrders.Find(filter)
            .SortByDescending(d => d.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var ledgerSnapshots = await BuildAffiliateOrderLedgerSnapshotsAsync(
            id,
            tenantId,
            docs.Select(d => d.OrderId).Distinct().ToList(),
            ct);

        return Ok(new AffiliateOrdersResponse
        {
            Orders = docs.Select(d =>
            {
                ledgerSnapshots.TryGetValue(d.OrderId, out var ledger);

                // FALLBACK BEHAVIOR: Missing ledger can occur when:
                // 1. Order has no commission entries yet (e.g., order placed but not yet processed)
                // 2. Order was excluded from affiliate commission (e.g., affiliate's own purchase)
                // 3. Ledger rebuild is in progress (eventual consistency)
                // Fallback strategy:
                // - Status/Currency: Use MongoDB document values (display-only, not authoritative)
                // - Numeric totals (TotalCommission/TotalApproved/TotalPaid/TotalReversed): Default to 0m (no commission earned)
                // - Item-level fields: Default to 0m/false/"Pending" (no commission data)
                if (ledger == null)
                {
                    _logger.LogWarning(
                        "Missing ledger snapshot for OrderId={OrderId}, AffiliateAccountId={AffiliateAccountId}. " +
                        "Falling back to MongoDB document values for Status/Currency and 0m for commission totals.",
                        d.OrderId, id);
                }

                return new AffiliateOrderDto
                {
                    OrderId = d.OrderId,
                    OrderNumber = d.OrderNumber,
                    Status = ledger?.Status ?? d.Status, // Fallback: MongoDB document Status (display-only)
                    OrderStatus = d.OrderStatus,
                    FulfillmentStatus = d.FulfillmentStatus,
                    PaymentMethod = d.PaymentMethod,
                    PaidAmount = ledger?.PaidAmount ?? 0m, // Fallback: 0m (no commission base)
                    TotalCommission = ledger?.TotalCommission ?? 0m, // Fallback: 0m (no commission)
                    TotalApproved = ledger?.TotalApproved ?? 0m, // Fallback: 0m (no approved commission)
                    TotalPaid = ledger?.TotalPaid ?? 0m, // Fallback: 0m (no paid commission)
                    TotalReversed = ledger?.TotalReversed ?? 0m, // Fallback: 0m (no reversals)
                    Currency = ledger?.Currency ?? d.Currency, // Fallback: MongoDB document Currency
                    CreatedAtUtc = d.CreatedAtUtc,
                    Items = d.Items.Select(i =>
                    {
                        AffiliateOrderItemLedgerSnapshot? itemLedger = null;
                        ledger?.Items.TryGetValue(i.OrderItemId, out itemLedger);

                        return new AffiliateOrderItemDto
                        {
                            OrderItemId = i.OrderItemId,
                            ProductId = i.ProductId,
                            ProductName = i.ProductName,
                            VariantName = i.VariantName,
                            PaidAmount = itemLedger?.PaidAmount ?? 0m, // Fallback: 0m (no commission base)
                            CommissionAmount = itemLedger?.CommissionAmount ?? 0m, // Fallback: 0m (no commission)
                            ReversedAmount = itemLedger?.ReversedAmount ?? 0m,
                            NetCommissionAmount = itemLedger?.NetCommissionAmount ?? 0m,
                            IsReversed = itemLedger?.IsReversed ?? false, // Fallback: false (not reversed)
                            IsPartiallyReversed = itemLedger?.IsPartiallyReversed ?? false,
                            SettlementStatus = itemLedger?.SettlementStatus ?? "Pending" // Fallback: "Pending" (no settlement)
                        };
                    }).ToList()
                };
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = (int)totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // AFFILIATE EARNINGS (per-currency)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/affiliates/{id}/earnings
    /// AF-19B: Returns per-currency earnings breakdown for CURRENT STORE ONLY from the SQL commission ledger.
    /// </summary>
    [HttpGet("{id:guid}/earnings")]
    public async Task<ActionResult<List<AffiliateEarningsSummaryDto>>> GetAffiliateEarnings(
        Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;

        // AF-19B: Verify affiliate exists globally (no tenant filter)
        var exists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);

        if (!exists)
            return NotFound(new { message = "Affiliate not found" });

        return Ok(await BuildAffiliateLedgerEarningsSummaryAsync(id, tenantId, ct));
    }

    // ═══════════════════════════════════════════════════════════════════
    // RECONCILE COMMISSION LEDGER (Legacy catch-up)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliates/{id}/reconcile-commissions
    /// Disabled in production because affiliate payout lifecycle mutations must flow
    /// through IAffiliateSettlementOrchestrator.
    /// </summary>
    [HttpPost("{id:guid}/reconcile-commissions")]
    public ActionResult ReconcileCommissions(Guid id, CancellationToken ct)
    {
        _logger.LogWarning(
            "[AF-38Y:Reconcile] Blocked legacy affiliate commission reconciliation because it bypasses the payout orchestrator | AffiliateId={AffiliateId}",
            id);

        return StatusCode(StatusCodes.Status410Gone, new
        {
            code = "AFFILIATE_RECONCILIATION_MUTATION_DISABLED",
            message = "Legacy commission reconciliation is disabled. Use the approved payout request mark-paid flow so IAffiliateSettlementOrchestrator remains the single payout authority."
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // APPROVE COMMISSIONS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliates/{id}/approve
    /// Approves earned commissions for specified orders (or all Earned orders).
    /// AF-19B: Works with global affiliates, approves orders from CURRENT STORE ONLY.
    /// Calls IAffiliateService.ApproveCommissionForOrderAsync per order.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<AdminSettlementActionResponse>> ApproveCommissions(
        Guid id,
        [FromBody] AdminSettlementActionRequest request,
        CancellationToken ct)
    {
        var tenantId = TenantId;

        // AF-19B: Verify affiliate exists globally (no tenant filter)
        var exists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);

        if (!exists)
            return NotFound(new { message = "Affiliate not found" });

        // SECURITY FIX: Verify affiliate has relationship with current tenant
        var hasRelationship = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AnyAsync(entry => 
                entry.AffiliateAccountId == id && 
                entry.StoreTenantId == tenantId, ct);

        if (!hasRelationship)
            return NotFound(new { message = "Affiliate not found" });

        // Determine which orders to approve
        List<Guid> orderIds;

        if (request.ApplyAll)
        {
            orderIds = await GetAffiliateOrderIdsFromCommissionLedgerAsync(
                id,
                tenantId,
                CommissionEntryType.Earned,
                scopedOrderIds: null,
                ct: ct);
        }
        else
        {
            var requestedOrderIds = (request.OrderIds ?? new List<Guid>()).Distinct().ToList();
            
            // AF-19B: Validate that OrderIds is not empty when ApplyAll is false
            if (requestedOrderIds.Count == 0)
            {
                return BadRequest(new
                {
                    message = "OrderIds cannot be empty when ApplyAll is false. Either provide specific order IDs or set ApplyAll to true."
                });
            }
            
            orderIds = await GetAffiliateOrderIdsFromCommissionLedgerAsync(
                id,
                tenantId,
                CommissionEntryType.Earned,
                requestedOrderIds,
                ct);

            var invalidOrderIds = requestedOrderIds.Except(orderIds).ToList();
            if (invalidOrderIds.Count > 0)
            {
                return BadRequest(new
                {
                    message = "One or more requested orders are not eligible Earned commissions for this affiliate and store.",
                    invalidOrderIds
                });
            }
        }

        if (!orderIds.Any())
            return Ok(new AdminSettlementActionResponse { ProcessedCount = 0, FailedCount = 0 });

        var processed = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var orderId in orderIds)
        {
            try
            {
                await _affiliateService.ApproveCommissionForOrderAsync(orderId, ct);
                processed++;
            }
            catch (Exception ex)
            {
                failed++;
                var error = AffiliateFinancialErrorContract.From(ex);
                errors.Add($"{error.Code}: {error.Message}");
                _logger.LogWarning(ex,
                    "ApproveCommission failed | AffiliateId: {AffiliateId} | OrderId: {OrderId}",
                    id, orderId);
            }
        }

        _logger.LogInformation(
            "Admin ApproveCommissions | TenantId: {TenantId} | AffiliateId: {AffiliateId} | Processed: {Processed} | Failed: {Failed}",
            tenantId, id, processed, failed);

        return Ok(new AdminSettlementActionResponse
        {
            ProcessedCount = processed,
            FailedCount = failed,
            Errors = errors
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // RECORD PAYMENT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliates/{id}/pay
    /// Records payment for approved commissions for specified orders (or all Approved orders).
    /// Requires immutable external payment evidence before any ledger withdrawal is posted.
    /// AF-19B: Works with global affiliates, pays orders from ALL stores.
    /// Calls IAffiliateService.RecordCommissionPaidAsync per order.
    /// </summary>
    [HttpPost("{id:guid}/pay")]
    public async Task<ActionResult<AdminSettlementActionResponse>> RecordPayment(
        Guid id,
        [FromBody] AdminSettlementActionRequest request,
        CancellationToken ct)
    {
        var tenantId = TenantId;
        var paymentReference = request.PaymentReference?.Trim();

        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            return BadRequest(new
            {
                code = "AFFILIATE_PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED",
                message = "Payment reference is required before approved affiliate commissions can be marked paid."
            });
        }

        // AF-19B: Verify affiliate exists globally (no tenant filter)
        var exists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);

        if (!exists)
            return NotFound(new { message = "Affiliate not found" });

        // SECURITY FIX: Verify affiliate has relationship with current tenant
        var hasRelationship = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AnyAsync(entry => 
                entry.AffiliateAccountId == id && 
                entry.StoreTenantId == tenantId, ct);

        if (!hasRelationship)
            return NotFound(new { message = "Affiliate not found" });

        // Determine which orders to pay
        List<Guid> orderIds;

        if (request.ApplyAll)
        {
            orderIds = await GetAffiliateOrderIdsFromCommissionLedgerAsync(
                id,
                tenantId,
                CommissionEntryType.Approved,
                scopedOrderIds: null,
                ct: ct);
        }
        else
        {
            var requestedOrderIds = (request.OrderIds ?? new List<Guid>()).Distinct().ToList();
            
            // AF-19B: Validate that OrderIds is not empty when ApplyAll is false
            if (requestedOrderIds.Count == 0)
            {
                return BadRequest(new
                {
                    message = "OrderIds cannot be empty when ApplyAll is false. Either provide specific order IDs or set ApplyAll to true."
                });
            }
            
            orderIds = await GetAffiliateOrderIdsFromCommissionLedgerAsync(
                id,
                tenantId,
                CommissionEntryType.Approved,
                requestedOrderIds,
                ct);

            var invalidOrderIds = requestedOrderIds.Except(orderIds).ToList();
            if (invalidOrderIds.Count > 0)
            {
                return BadRequest(new
                {
                    message = "One or more requested orders are not eligible Approved commissions for this affiliate and store.",
                    invalidOrderIds
                });
            }
        }

        if (!orderIds.Any())
            return Ok(new AdminSettlementActionResponse { ProcessedCount = 0, FailedCount = 0 });

        // ═══════════════════════════════════════════════════════════════
        // AF-14C: PRE-CHECK CASHBOX BALANCE
        // AF-19B: Check current tenant's cashbox only (not all stores)
        // Calculate total payout and verify cashbox has sufficient funds
        // BEFORE recording any payments in the sub-ledger.
        // ═══════════════════════════════════════════════════════════════
        var totalPayout = await CalculateApprovedCommissionPayoutFromLedgerAsync(id, tenantId, orderIds, ct);
        var externalTransferAmount = request.ExternalTransferAmount ?? totalPayout;
        if (externalTransferAmount < totalPayout)
        {
            return BadRequest(new
            {
                code = "AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_AMOUNT_TOO_LOW",
                message = "External transfer amount cannot be less than the approved commission payout being marked paid.",
                externalTransferAmount,
                totalPayout
            });
        }

        var cashboxBalance = await GetCashboxBalanceAsync(tenantId, ct);
        if (cashboxBalance < totalPayout)
        {
            _logger.LogWarning(
                "[Affiliate:Pay] BLOCKED — Insufficient cashbox balance | " +
                "TenantId: {TenantId} | AffiliateId: {AffiliateId} | " +
                "TotalPayout: {TotalPayout} | CashboxBalance: {CashboxBalance}",
                tenantId, id, totalPayout, cashboxBalance);

            return BadRequest(new
            {
                message = $"Insufficient cashbox balance. Available: {cashboxBalance:F2}, Required: {totalPayout:F2}. " +
                          $"Please deposit funds to the cashbox before paying affiliate commissions.",
                cashboxBalance,
                totalPayout,
                shortfall = totalPayout - cashboxBalance
            });
        }

        try
        {
            var result = await _orchestrator.ExecuteAffiliateBatchPayoutAsync(
                id,
                tenantId,
                orderIds,
                totalPayout,
                paymentReference,
                adminNotes: request.AdminNotes,
                externalTransferAmount: externalTransferAmount,
                ct: ct);

            _logger.LogInformation(
                "Admin RecordPayment Batch | TenantId: {TenantId} | AffiliateId: {AffiliateId} | Processed: {Processed}",
                tenantId, id, result.PaidOrders.Count);

            return Ok(new AdminSettlementActionResponse
            {
                ProcessedCount = result.PaidOrders.Count,
                FailedCount = 0,
                Errors = new List<string>()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RecordPayment Batch failed | TenantId: {TenantId} | AffiliateId: {AffiliateId}",
                tenantId, id);

            var error = AffiliateFinancialErrorContract.From(ex);
            return StatusCode(error.StatusCode, new AdminSettlementActionResponse
            {
                ProcessedCount = 0,
                FailedCount = orderIds.Count,
                Errors = new List<string> { $"{error.Code}: {error.Message}" }
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SUSPEND
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliates/{id}/suspend
    /// AF-19B: Suspends a global affiliate account. Requires a reason.
    /// </summary>
    [HttpPost("{id:guid}/suspend")]
    public async Task<ActionResult> SuspendAffiliate(
        Guid id,
        [FromBody] AdminSuspendRequest request,
        CancellationToken ct)
    {
        var tenantId = TenantId;

        // AF-19B: Verify affiliate exists globally (no tenant filter)
        var exists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);

        if (!exists)
            return NotFound(new { message = "Affiliate not found" });

        // SECURITY FIX: Verify affiliate has relationship with current tenant
        var hasRelationship = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AnyAsync(entry => 
                entry.AffiliateAccountId == id && 
                entry.StoreTenantId == tenantId, ct);

        if (!hasRelationship)
            return NotFound(new { message = "Affiliate not found" });

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Suspension reason is required" });

        try
        {
            await _affiliateService.SuspendAffiliateAsync(id, request.Reason, ct);

            _logger.LogInformation(
                "Admin SuspendAffiliate | TenantId: {TenantId} | AffiliateId: {AffiliateId} | Reason: {Reason}",
                tenantId, id, request.Reason);

            return Ok(new { message = "Affiliate suspended successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SuspendAffiliate failed | TenantId: {TenantId} | AffiliateId: {AffiliateId}",
                tenantId, id);
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ACTIVATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliates/{id}/activate
    /// AF-19B: Activates (or reactivates) a global affiliate account.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult> ActivateAffiliate(
        Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;

        // AF-19B: Verify affiliate exists globally (no tenant filter)
        var exists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);

        if (!exists)
            return NotFound(new { message = "Affiliate not found" });

        // SECURITY FIX: Verify affiliate has relationship with current tenant
        var hasRelationship = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AnyAsync(entry => 
                entry.AffiliateAccountId == id && 
                entry.StoreTenantId == tenantId, ct);

        if (!hasRelationship)
            return NotFound(new { message = "Affiliate not found" });

        try
        {
            await _affiliateService.ActivateAffiliateAsync(id, ct);

            _logger.LogInformation(
                "Admin ActivateAffiliate | TenantId: {TenantId} | AffiliateId: {AffiliateId}",
                tenantId, id);

            return Ok(new { message = "Affiliate activated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ActivateAffiliate failed | TenantId: {TenantId} | AffiliateId: {AffiliateId}",
                tenantId, id);
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // CLOSE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliates/{id}/close
    /// AF-PA3.4: Closes a global affiliate account as a terminal lifecycle state.
    /// </summary>
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult> CloseAffiliate(
        Guid id,
        [FromBody] AdminCloseRequest request,
        CancellationToken ct)
    {
        var tenantId = TenantId;

        var exists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);

        if (!exists)
            return NotFound(new { message = "Affiliate not found" });

        var hasRelationship = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AnyAsync(entry =>
                entry.AffiliateAccountId == id &&
                entry.StoreTenantId == tenantId, ct);

        if (!hasRelationship)
            return NotFound(new { message = "Affiliate not found" });

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Closure reason is required" });

        try
        {
            await _affiliateService.CloseAffiliateAsync(id, request.Reason, ct);

            _logger.LogInformation(
                "Admin CloseAffiliate | TenantId: {TenantId} | AffiliateId: {AffiliateId} | Reason: {Reason}",
                tenantId, id, request.Reason);

            return Ok(new { message = "Affiliate closed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CloseAffiliate failed | TenantId: {TenantId} | AffiliateId: {AffiliateId}",
                tenantId, id);
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-18: BACKFILL MISSING COMMISSIONS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliates/backfill-commission
    /// 
    /// Triggers commission recording for a specific order.
    /// Used to backfill historical orders that were affected by the AF-18 bug
    /// (orders that went directly to Completed without triggering commission).
    /// 
    /// IDEMPOTENT: Safe to call multiple times for the same order.
    /// </summary>
    [HttpPost("backfill-commission")]
    public async Task<IActionResult> BackfillCommission(
        [FromBody] BackfillCommissionRequest request,
        CancellationToken ct = default)
    {
        var tenantId = TenantId;

        // Verify order exists and belongs to this tenant
        var order = await _dbContext.Orders
            .Where(o => o.Id == request.OrderId && o.TenantId == tenantId && !o.IsDeleted)
            .Select(o => new { o.Id, o.OrderNumber, o.AffiliateAccountId, o.Status })
            .FirstOrDefaultAsync(ct);

        if (order == null)
            return NotFound(new { error = "Order not found" });

        if (!order.AffiliateAccountId.HasValue)
            return BadRequest(new { error = "Order is not affiliated" });

        try
        {
            await _affiliateService.RecordCommissionForOrderAsync(request.OrderId, ct);

            _logger.LogInformation(
                "[AF-18:Backfill] Commission recorded | OrderId: {OrderId} | OrderNumber: {OrderNumber} | TenantId: {TenantId}",
                order.Id, order.OrderNumber, tenantId);

            return Ok(new
            {
                success = true,
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                message = "Commission recording triggered successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AF-18:Backfill] Failed to record commission | OrderId: {OrderId} | TenantId: {TenantId}",
                request.OrderId, tenantId);

            var error = AffiliateFinancialErrorContract.From(ex);
            return StatusCode(error.StatusCode, new
            {
                code = error.Code,
                message = error.Message
            });
        }
    }

    /// <summary>
    /// POST /api/admin/affiliates/backfill-all-missing
    /// 
    /// Identifies and backfills ALL orders with attribution but no commission.
    /// Returns summary of affected orders and results.
    /// 
    /// DANGEROUS: Use with caution in production. Prefer single-order backfill.
    /// </summary>
    [HttpPost("backfill-all-missing")]
    public async Task<IActionResult> BackfillAllMissing(CancellationToken ct = default)
    {
        var tenantId = TenantId;

        // Find orders with attribution but no commission
        var affectedOrders = await _dbContext.Orders
            .Where(o => o.TenantId == tenantId 
                     && !o.IsDeleted
                     && o.AffiliateAccountId.HasValue
                     && (o.Status == Core.Enums.OrderStatus.Delivered || o.Status == Core.Enums.OrderStatus.Completed))
            .Select(o => new { o.Id, o.OrderNumber, o.AffiliateAccountId })
            .ToListAsync(ct);

        var results = new List<object>();
        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;

        foreach (var order in affectedOrders)
        {
            // Check if commission already exists
            // AUDIT FIX: Must use IgnoreQueryFilters because commission ledger entries
            // have TenantId=NULL (AF-19B global affiliates), so the tenant query filter
            // would never match and this check would always return false.
            var hasCommission = await _dbContext.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .AnyAsync(acl => acl.OrderId == order.Id, ct);

            if (hasCommission)
            {
                skippedCount++;
                results.Add(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    status = "skipped",
                    reason = "Commission already exists"
                });
                continue;
            }

            try
            {
                await _affiliateService.RecordCommissionForOrderAsync(order.Id, ct);
                successCount++;
                results.Add(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    status = "success"
                });

                _logger.LogInformation(
                    "[AF-18:BackfillAll] Commission recorded | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                    order.Id, order.OrderNumber);
            }
            catch (Exception ex)
            {
                failureCount++;
                var error = AffiliateFinancialErrorContract.From(ex);
                results.Add(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    status = "failed",
                    code = error.Code,
                    error = error.Message
                });

                _logger.LogError(ex,
                    "[AF-18:BackfillAll] Failed | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                    order.Id, order.OrderNumber);
            }
        }

        return Ok(new
        {
            summary = new
            {
                totalOrders = affectedOrders.Count,
                successCount,
                failureCount,
                skippedCount
            },
            results
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // AUDIT FIX O-2: AFFILIATE HEALTH CHECK
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/affiliates/health
    /// 
    /// AUDIT FIX O-2: Affiliate subsystem health check.
    /// Verifies Postgres and MongoDB connectivity and cross-checks data consistency.
    /// Returns component-level status with drift detection.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> HealthCheck(CancellationToken ct)
    {
        var tenantId = TenantId;
        var checks = new Dictionary<string, object>();
        var healthy = true;

        // ─── Check 1: Postgres affiliate tables ─────────────────────────
        try
        {
            var accountCount = await _dbContext.AffiliateAccounts
                .IgnoreQueryFilters()
                .CountAsync(a => !a.IsDeleted, ct);
            var ledgerCount = await _dbContext.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .CountAsync(ct);
            var policyCount = await _dbContext.AffiliateProductPolicies
                .Where(p => p.TenantId == tenantId && p.IsEnabled && !p.IsDeleted)
                .CountAsync(ct);

            checks["postgres"] = new
            {
                status = "ok",
                totalAccounts = accountCount,
                totalLedgerEntries = ledgerCount,
                enabledPolicies = policyCount
            };
        }
        catch (Exception ex)
        {
            healthy = false;
            checks["postgres"] = new { status = "error", error = ex.Message };
        }

        // ─── Check 2: MongoDB affiliate collections ─────────────────────
        try
        {
            var dashboardCount = await _mongoDbContext.AffiliateDashboards
                .CountDocumentsAsync(
                    Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId),
                    cancellationToken: ct);
            var orderCount = await _mongoDbContext.AffiliateOrders
                .CountDocumentsAsync(
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
                    cancellationToken: ct);

            checks["mongodb"] = new
            {
                status = "ok",
                dashboards = dashboardCount,
                orders = orderCount
            };
        }
        catch (Exception ex)
        {
            healthy = false;
            checks["mongodb"] = new { status = "error", error = ex.Message };
        }

        // ─── Check 3: Data consistency (drift detection) ────────────────
        try
        {
            // Count distinct orders with earned commissions in Postgres
            var pgOrderCount = await _dbContext.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .Where(e => e.StoreTenantId == tenantId && e.EntryType == CommissionEntryType.Earned)
                .Select(e => e.OrderId)
                .Distinct()
                .CountAsync(ct);

            // Count orders in MongoDB for this tenant
            var mongoOrderCount = await _mongoDbContext.AffiliateOrders
                .CountDocumentsAsync(
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
                    cancellationToken: ct);

            var drift = Math.Abs(pgOrderCount - (int)mongoOrderCount);
            checks["consistency"] = new
            {
                status = drift == 0 ? "ok" : "drift_detected",
                postgresOrders = pgOrderCount,
                mongoOrders = mongoOrderCount,
                drift
            };

            if (drift > 0)
            {
                _logger.LogWarning(
                    "[Affiliate:Health] Drift detected | TenantId: {TenantId} | PG: {PG} | Mongo: {Mongo} | Drift: {Drift}",
                    tenantId, pgOrderCount, mongoOrderCount, drift);
            }
        }
        catch (Exception ex)
        {
            checks["consistency"] = new { status = "error", error = ex.Message };
        }

        return Ok(new
        {
            status = healthy ? "healthy" : "degraded",
            tenantId,
            timestamp = DateTime.UtcNow,
            checks
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // AUDIT FIX S-1: REINDEX AFFILIATE PRODUCTS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliates/reindex-products
    /// 
    /// AUDIT FIX S-1: Republishes AffiliateProductPolicyChangedEvent for all enabled
    /// policies in the current tenant, triggering a full rebuild of MongoDB product
    /// intelligence and Elasticsearch affiliate flags.
    /// 
    /// IDEMPOTENT: Safe to call multiple times. Projections use upsert semantics.
    /// </summary>
    [HttpPost("reindex-products")]
    public async Task<IActionResult> ReindexProducts(CancellationToken ct)
    {
        var tenantId = TenantId;

        var policies = await _dbContext.AffiliateProductPolicies
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .ToListAsync(ct);

        if (!policies.Any())
            return Ok(new { message = "No affiliate product policies found", count = 0 });

        var published = 0;
        var errors = new List<string>();

        foreach (var policy in policies)
        {
            try
            {
                await _eventPublisher.PublishAsync(new AffiliateProductPolicyChangedEvent
                {
                    TenantId = tenantId,
                    ProductId = policy.ProductId,
                    IsEnabled = policy.IsEnabled,
                    CommissionRate = policy.CommissionRate,
                    MaxCommissionPerItem = policy.MaxCommissionPerItem
                }, ct);
                published++;
            }
            catch (Exception ex)
            {
                errors.Add($"Product {policy.ProductId}: {ex.Message}");
                _logger.LogWarning(ex,
                    "[Affiliate:Reindex] Failed to publish policy event | ProductId: {ProductId}",
                    policy.ProductId);
            }
        }

        _logger.LogInformation(
            "[Affiliate:Reindex] Completed | TenantId: {TenantId} | Published: {Published}/{Total} | Errors: {Errors}",
            tenantId, published, policies.Count, errors.Count);

        return Ok(new
        {
            message = "Reindex triggered",
            total = policies.Count,
            published,
            errors = errors.Count > 0 ? errors : null
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-38: WITHDRAWAL REQUEST MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/affiliates/payouts?status=Pending&amp;page=1&amp;pageSize=20
    /// Lists payout requests for the current store.
    /// </summary>
    [HttpGet("payouts")]
    public async Task<ActionResult> ListPayoutRequests(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = TenantId;
        PayoutRequestStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PayoutRequestStatus>(status, true, out var parsed))
            statusFilter = parsed;

        var (items, totalCount) = await _payoutService.ListPayoutRequestsAsync(
            tenantId, statusFilter, page, pageSize, ct);

        // Batch-load affiliate names
        var affiliateIds = items.Select(r => r.AffiliateAccountId).Distinct().ToList();
        var affiliateNames = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => affiliateIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.DisplayName, ct);

        return Ok(new
        {
            items = items.Select(r => new
            {
                r.Id,
                r.AffiliateAccountId,
                affiliateName = affiliateNames.GetValueOrDefault(r.AffiliateAccountId, "Unknown"),
                r.RequestedAmount,
                r.RemainingAmount,
                r.ApprovedAmount,
                status = r.Status.ToString(),
                r.RequestedAtUtc,
                r.ApprovedAtUtc,
                r.ApprovedBy,
                r.RejectedAtUtc,
                r.RejectedBy,
                r.PaidAtUtc,
                r.PaidBy,
                r.PaymentReference,
                r.AdminNotes,
                r.Currency
            }).ToList(),
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// AF-38C: GET /api/admin/affiliates/{id}/withdrawals?status=Pending&amp;page=1&amp;pageSize=20
    /// Lists withdrawal requests for a SPECIFIC affiliate.
    /// DATA ISOLATION: Filters by BOTH StoreTenantId AND AffiliateAccountId.
    /// </summary>
    [HttpGet("{id:guid}/withdrawals")]
    public async Task<ActionResult> GetAffiliateWithdrawals(
        Guid id,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = TenantId;
        
        // Verify affiliate exists
        var exists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && !a.IsDeleted, ct);
        
        if (!exists)
            return NotFound(new { message = "Affiliate not found" });
        
        PayoutRequestStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PayoutRequestStatus>(status, true, out var parsed))
            statusFilter = parsed;
        
        // AF-38C: Call new service method with BOTH filters
        var (items, totalCount) = await _payoutService.ListPayoutRequestsForAffiliateAsync(
            tenantId, id, statusFilter, page, pageSize, ct);
        
        // Load affiliate name
        var affiliateName = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => a.Id == id)
            .Select(a => a.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "Unknown";
        
        _logger.LogInformation(
            "[DATA-ISOLATION] AdminAffiliateDetail withdrawals loaded | " +
            "TenantId: {TenantId} | AffiliateId: {AffiliateId} | Count: {Count}",
            tenantId, id, items.Count);
        
        return Ok(new
        {
            items = items.Select(r => new
            {
                r.Id,
                r.AffiliateAccountId,
                affiliateName,
                r.RequestedAmount,
                r.RemainingAmount,
                r.ApprovedAmount,
                status = r.Status.ToString(),
                r.RequestedAtUtc,
                r.ApprovedAtUtc,
                r.ApprovedBy,
                r.RejectedAtUtc,
                r.RejectedBy,
                r.PaidAtUtc,
                r.PaidBy,
                r.PaymentReference,
                r.AdminNotes,
                r.Currency
            }).ToList(),
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// POST /api/admin/affiliates/payouts/{id}/approve
    /// Approves a pending withdrawal request.
    /// </summary>
    [HttpPost("payouts/{id:guid}/approve")]
    public async Task<ActionResult> ApprovePayoutRequest(
        Guid id,
        [FromBody] AdminPayoutActionRequest? body,
        CancellationToken ct)
    {
        try
        {
            if (!TryGetCurrentAdminUserId(out var adminUserId))
                return Unauthorized(new { message = "Current admin identity is required to approve payout requests." });

            var result = await _payoutService.ApprovePayoutAsync(
                id, TenantId, adminUserId, body?.AdminNotes, ct);

            return Ok(new { message = "Payout request approved", id = result.Id, approvedAmount = result.ApprovedAmount, approvedBy = result.ApprovedBy });
        }
        catch (InvalidOperationException ex)
        {
            var error = AffiliateFinancialErrorContract.From(ex);
            return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message });
        }
        catch (ArgumentException ex)
        {
            var error = AffiliateFinancialErrorContract.From(ex);
            return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message });
        }
    }

    /// <summary>
    /// POST /api/admin/affiliates/payouts/{id}/reject
    /// Rejects a pending withdrawal request. Funds return to Available.
    /// </summary>
    [HttpPost("payouts/{id:guid}/reject")]
    public async Task<ActionResult> RejectPayoutRequest(
        Guid id,
        [FromBody] AdminPayoutActionRequest? body,
        CancellationToken ct)
    {
        try
        {
            if (!TryGetCurrentAdminUserId(out var adminUserId))
                return Unauthorized(new { message = "Current admin identity is required to reject payout requests." });

            var result = await _payoutService.RejectPayoutAsync(
                id, TenantId, adminUserId, body?.AdminNotes, ct);

            return Ok(new { message = "Payout request rejected", id = result.Id, rejectedBy = result.RejectedBy });
        }
        catch (InvalidOperationException ex)
        {
            var error = AffiliateFinancialErrorContract.From(ex);
            return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message });
        }
        catch (ArgumentException ex)
        {
            var error = AffiliateFinancialErrorContract.From(ex);
            return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message });
        }
    }

    /// <summary>
    /// POST /api/admin/affiliates/payouts/{id}/mark-paid
    /// AF-38H + AF-38Y: Marks an approved request as Paid via the Unified Payout Orchestrator.
    /// 
    /// V-2: SERIALIZABLE isolation eliminates TOCTOU race.
    /// V-3: MarkPaid lifecycle is handled INSIDE the orchestrator's transaction (no drift).
    /// V-4: StoreTenantId is passed explicitly for defense-in-depth.
    /// 
    /// After orchestrator commits, publishes AffiliateCommissionPaidEvent per paid order
    /// so MongoDB projections (Settlement tab) stay in sync.
    /// </summary>
    [HttpPost("payouts/{id:guid}/mark-paid")]
    public async Task<ActionResult> MarkPayoutPaid(
        Guid id,
        [FromBody] AdminMarkPaidRequest? body,
        CancellationToken ct)
    {
        try
        {
            var storeTenantId = TenantId;

            if (!TryGetCurrentAdminUserId(out var adminUserId))
                return Unauthorized(new { message = "Current admin identity is required to execute payout payment." });

            // Load the payout request
            var request = await _dbContext.AffiliatePayoutRequests
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    r => r.Id == id && r.StoreTenantId == storeTenantId, ct);

            if (request == null)
                return NotFound(new { code = "AFFILIATE_PAYOUT_REQUEST_NOT_FOUND", message = "The requested payout was not found." });

            // Guard: must be Approved
            if (request.Status != PayoutRequestStatus.Approved)
                return Conflict(new { code = "AFFILIATE_PAYOUT_REQUEST_STATUS_INVALID", message = "The payout must be approved before it can be marked paid." });

            var paymentReference = body?.PaymentReference?.Trim();
            if (string.IsNullOrWhiteSpace(paymentReference))
            {
                return BadRequest(new
                {
                    code = "AFFILIATE_PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED",
                    message = "Payment reference is required before an affiliate payout request can be marked paid."
                });
            }

            var externalTransferAmount = body?.ExternalTransferAmount ?? request.ApprovedAmount!.Value;
            if (externalTransferAmount < request.ApprovedAmount!.Value)
            {
                return BadRequest(new
                {
                    code = "AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_AMOUNT_TOO_LOW",
                    message = "External transfer amount cannot be less than the approved payout amount.",
                    externalTransferAmount,
                    approvedAmount = request.ApprovedAmount!.Value
                });
            }

            // AF-38H + AF-38Y: Execute payout through the Unified Orchestrator
            var result = await _orchestrator.ExecuteAffiliatePayoutAsync(
                request.AffiliateAccountId,
                storeTenantId,
                request.ApprovedAmount!.Value,
                SettlementSource.WithdrawalEngine,
                paymentReference,
                payoutRequestId: request.Id,
                reference: $"Withdrawal Request #{request.Id}",
                adminNotes: body?.AdminNotes,
                externalTransferAmount: externalTransferAmount,
                paidByUserId: adminUserId,
                ct: ct);

            _logger.LogInformation(
                "[AF-38Y:MarkPaid] Payout executed via orchestrator | RequestId={RequestId} | " +
                "Amount={Amount} | AffiliateId={AffiliateId} | OrdersPaid={OrderCount}",
                request.Id, request.ApprovedAmount, request.AffiliateAccountId, result.PaidOrders.Count);

            return Ok(new
            {
                message = "Payout marked as paid",
                id = request.Id,
                paidAmount = request.ApprovedAmount,
                paidBy = adminUserId,
                paymentReference,
                ordersPaid = result.PaidOrders.Count
            });
        }
        catch (InvalidOperationException ex)
        {
            var error = AffiliateFinancialErrorContract.From(ex);
            return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private async Task<Guid?> ResolveAffiliateAuthorityTenantIdAsync(Guid affiliateAccountId, CancellationToken ct)
    {
        var tenantId = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(account => account.Id == affiliateAccountId && !account.IsDeleted)
            .Select(account => account.TenantId)
            .FirstOrDefaultAsync(ct);

        return tenantId == Guid.Empty ? null : tenantId;
    }

    private async Task<List<Guid>> GetAffiliateOrderIdsFromCommissionLedgerAsync(
        Guid affiliateAccountId,
        Guid tenantId,
        CommissionEntryType eligibleEntryType,
        IReadOnlyCollection<Guid>? scopedOrderIds,
        CancellationToken ct)
    {
        var blockingEntryTypes = eligibleEntryType switch
        {
            CommissionEntryType.Earned => new[] { CommissionEntryType.Approved, CommissionEntryType.Paid, CommissionEntryType.Reversed },
            CommissionEntryType.Approved => new[] { CommissionEntryType.Paid, CommissionEntryType.Reversed },
            _ => Array.Empty<CommissionEntryType>()
        };

        var trackedTypes = blockingEntryTypes.Concat(new[] { eligibleEntryType }).Distinct().ToList();
        var query = _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(e =>
                e.AffiliateAccountId == affiliateAccountId &&
                e.StoreTenantId == tenantId &&
                trackedTypes.Contains(e.EntryType));

        // AF-19B: Explicit handling of empty scopedOrderIds collection
        // When scopedOrderIds is provided but empty, return empty list instead of all orders
        if (scopedOrderIds != null)
        {
            if (scopedOrderIds.Count == 0)
            {
                // Empty collection means no orders should match
                return new List<Guid>();
            }
            
            query = query.Where(e => scopedOrderIds.Contains(e.OrderId));
        }

        var entries = await query
            .Select(e => new
            {
                e.OrderId,
                e.OrderItemId,
                e.EntryType,
                e.CommissionAmount
            })
            .ToListAsync(ct);

        return entries
            .GroupBy(e => new { e.OrderId, e.OrderItemId })
            .Where(group =>
            {
                var earned = group.Where(entry => entry.EntryType == CommissionEntryType.Earned)
                    .Sum(entry => entry.CommissionAmount);
                var approved = group.Where(entry => entry.EntryType == CommissionEntryType.Approved)
                    .Sum(entry => entry.CommissionAmount);
                var reversed = group.Where(entry => entry.EntryType == CommissionEntryType.Reversed)
                    .Sum(entry => entry.CommissionAmount);
                var hasPaid = group.Any(entry => entry.EntryType == CommissionEntryType.Paid);

                return eligibleEntryType switch
                {
                    CommissionEntryType.Earned => earned > reversed && approved == 0m && !hasPaid,
                    CommissionEntryType.Approved => approved > reversed && !hasPaid,
                    _ => group.Any(entry => entry.EntryType == eligibleEntryType) &&
                         !group.Any(entry => blockingEntryTypes.Contains(entry.EntryType))
                };
            })
            .Select(g => g.Key.OrderId)
            .Distinct()
            .ToList();
    }

    private async Task<decimal> CalculateApprovedCommissionPayoutFromLedgerAsync(
        Guid affiliateAccountId,
        Guid tenantId,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken ct)
    {
        if (orderIds.Count == 0)
            return 0m;

        var entries = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(e =>
                e.AffiliateAccountId == affiliateAccountId &&
                e.StoreTenantId == tenantId &&
                orderIds.Contains(e.OrderId) &&
                (e.EntryType == CommissionEntryType.Approved ||
                 e.EntryType == CommissionEntryType.Paid ||
                 e.EntryType == CommissionEntryType.Reversed))
            .Select(e => new
            {
                e.OrderItemId,
                e.EntryType,
                e.CommissionAmount
            })
            .ToListAsync(ct);

        var paidOrderItemIds = entries
            .Where(e => e.EntryType == CommissionEntryType.Paid)
            .Select(e => e.OrderItemId)
            .ToHashSet();

        return entries
            .GroupBy(e => e.OrderItemId)
            .Where(group => !paidOrderItemIds.Contains(group.Key))
            .Sum(group => Math.Max(
                group.Where(entry => entry.EntryType == CommissionEntryType.Approved)
                    .Sum(entry => entry.CommissionAmount) -
                group.Where(entry => entry.EntryType == CommissionEntryType.Reversed)
                    .Sum(entry => entry.CommissionAmount),
                0m));
    }

    private async Task<HashSet<Guid>> BuildAffiliateOrderIdsForLedgerStatusAsync(
        Guid affiliateAccountId,
        Guid tenantId,
        string status,
        CancellationToken ct)
    {
        // Performance optimization: compute status at DB level instead of loading all snapshots
        var normalizedStatus = status.Trim();
        
        // Get all ledger entries for this affiliate and tenant
        var entries = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.AffiliateAccountId == affiliateAccountId &&
                entry.StoreTenantId == tenantId)
            .Select(entry => new
            {
                entry.OrderId,
                entry.OrderItemId,
                entry.EntryType,
                entry.CommissionAmount
            })
            .ToListAsync(ct);

        // Group by order and compute status
        var orderStatuses = entries
            .GroupBy(e => e.OrderId)
            .Select(orderGroup => new
            {
                OrderId = orderGroup.Key,
                Status = ComputeOrderStatusFromEntries(orderGroup.Select(e => new
                {
                    e.OrderItemId,
                    e.EntryType,
                    e.CommissionAmount
                }))
            })
            .Where(x => string.Equals(x.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.OrderId)
            .ToHashSet();

        return orderStatuses;
    }

    private static string ComputeOrderStatusFromEntries(IEnumerable<dynamic> entries)
    {
        // Group by order item to determine per-item status
        var itemStatuses = entries
            .GroupBy(e => e.OrderItemId)
            .Select(itemGroup =>
            {
                var entryTypes = itemGroup.Select(e => (CommissionEntryType)e.EntryType).ToList();
                var earned = itemGroup
                    .Where(e => (CommissionEntryType)e.EntryType == CommissionEntryType.Earned)
                    .Sum(e => (decimal)e.CommissionAmount);
                var reversed = itemGroup
                    .Where(e => (CommissionEntryType)e.EntryType == CommissionEntryType.Reversed)
                    .Sum(e => (decimal)e.CommissionAmount);

                if (earned > 0m && reversed >= earned) return "Reversed";
                if (reversed > 0m) return "PartiallyReversed";
                if (entryTypes.Contains(CommissionEntryType.Paid)) return "Paid";
                if (entryTypes.Contains(CommissionEntryType.Approved)) return "Approved";
                if (entryTypes.Contains(CommissionEntryType.Earned)) return "Earned";
                return "Pending";
            })
            .ToList();

        // Determine order-level status from item statuses
        if (itemStatuses.Count == 0) return "Pending";
        if (itemStatuses.All(s => s == "Reversed")) return "Reversed";
        if (itemStatuses.Any(s => s is "Reversed" or "PartiallyReversed")) return "PartiallyReversed";
        if (itemStatuses.All(s => s == "Paid")) return "Paid";
        if (itemStatuses.All(s => s is "Approved" or "Paid")) return "Approved";
        return "Earned";
    }

    private async Task<Dictionary<Guid, AffiliateOrderLedgerSnapshot>> BuildAffiliateOrderLedgerSnapshotsAsync(
        Guid affiliateAccountId,
        Guid tenantId,
        IReadOnlyCollection<Guid>? orderIds,
        CancellationToken ct)
    {
        if (orderIds is { Count: 0 })
        {
            return new Dictionary<Guid, AffiliateOrderLedgerSnapshot>();
        }

        var query = _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.AffiliateAccountId == affiliateAccountId &&
                entry.StoreTenantId == tenantId);

        if (orderIds != null)
        {
            query = query.Where(entry => orderIds.Contains(entry.OrderId));
        }

        var rows = await query
            .Select(entry => new AffiliateOrderLedgerEntryRow(
                entry.OrderId,
                entry.OrderItemId,
                entry.EntryType,
                entry.BaseAmount,
                entry.CommissionAmount,
                entry.Currency))
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.OrderId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var itemSnapshots = group
                        .GroupBy(row => row.OrderItemId)
                        .ToDictionary(
                            itemGroup => itemGroup.Key,
                            BuildAffiliateOrderItemLedgerSnapshot);

                    var earnedRows = group.Where(row => row.EntryType == CommissionEntryType.Earned).ToList();
                    var currency = NormalizeCurrency(
                        earnedRows.Select(row => row.Currency).FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency))
                        ?? group.Select(row => row.Currency).FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency)));

                    return new AffiliateOrderLedgerSnapshot(
                        PaidAmount: earnedRows.Sum(row => row.BaseAmount),
                        TotalCommission: earnedRows.Sum(row => row.CommissionAmount),
                        TotalApproved: group.Where(row => row.EntryType == CommissionEntryType.Approved).Sum(row => row.CommissionAmount),
                        TotalPaid: group.Where(row => row.EntryType == CommissionEntryType.Paid).Sum(row => row.CommissionAmount),
                        TotalReversed: group.Where(row => row.EntryType == CommissionEntryType.Reversed).Sum(row => row.CommissionAmount),
                        Currency: currency,
                        Status: DetermineLedgerOrderStatus(itemSnapshots.Values),
                        Items: itemSnapshots);
                });
    }

    private static AffiliateOrderItemLedgerSnapshot BuildAffiliateOrderItemLedgerSnapshot(
        IEnumerable<AffiliateOrderLedgerEntryRow> rows)
    {
        var rowList = rows.ToList();
        var earnedRows = rowList.Where(row => row.EntryType == CommissionEntryType.Earned).ToList();
        var earnedCommission = earnedRows.Sum(row => row.CommissionAmount);
        var reversedCommission = rowList
            .Where(row => row.EntryType == CommissionEntryType.Reversed)
            .Sum(row => row.CommissionAmount);
        var isFullyReversed = earnedCommission > 0m && reversedCommission >= earnedCommission;
        var isPartiallyReversed = reversedCommission > 0m && !isFullyReversed;
        var status = isFullyReversed ? "Reversed" :
            isPartiallyReversed ? "PartiallyReversed" :
            rowList.Any(row => row.EntryType == CommissionEntryType.Paid) ? "Paid" :
            rowList.Any(row => row.EntryType == CommissionEntryType.Approved) ? "Approved" :
            rowList.Any(row => row.EntryType == CommissionEntryType.Earned) ? "Earned" :
            "Pending";

        return new AffiliateOrderItemLedgerSnapshot(
            PaidAmount: earnedRows.Sum(row => row.BaseAmount),
            CommissionAmount: earnedCommission,
            ReversedAmount: reversedCommission,
            NetCommissionAmount: Math.Max(earnedCommission - reversedCommission, 0m),
            IsReversed: isFullyReversed,
            IsPartiallyReversed: isPartiallyReversed,
            SettlementStatus: status);
    }

    private static string DetermineLedgerOrderStatus(IEnumerable<AffiliateOrderItemLedgerSnapshot> items)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0) return "Pending";
        
        // Check for all-reversed first
        if (itemList.All(item => item.IsReversed)) return "Reversed";
        
        // Check for partial reversals BEFORE checking mixed states that allow reversals
        // This ensures mixed reversed+paid/approved orders return "PartiallyReversed"
        if (itemList.Any(item => item.IsReversed || item.IsPartiallyReversed)) return "PartiallyReversed";
        
        // Now check non-reversed item statuses (no reversals at this point)
        if (itemList.All(item => item.SettlementStatus == "Paid")) return "Paid";
        if (itemList.All(item => item.SettlementStatus is "Approved" or "Paid")) return "Approved";
        
        return "Earned";
    }

    private async Task<Dictionary<Guid, AffiliateLedgerTotals>> BuildAffiliateLedgerTotalsByAffiliateAsync(
        IReadOnlyCollection<Guid> affiliateIds,
        Guid tenantId,
        CancellationToken ct)
    {
        var ids = affiliateIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, AffiliateLedgerTotals>();
        }

        var query = _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.StoreTenantId == tenantId &&
                ids.Contains(entry.AffiliateAccountId));

        var totalsRows = await query
            .GroupBy(entry => new { entry.AffiliateAccountId, Currency = entry.Currency, entry.EntryType })
            .Select(group => new
            {
                group.Key.AffiliateAccountId,
                group.Key.Currency,
                group.Key.EntryType,
                Amount = group.Sum(entry => entry.CommissionAmount)
            })
            .ToListAsync(ct);

        var orderCounts = await query
            .Where(entry => entry.EntryType == CommissionEntryType.Earned)
            .GroupBy(entry => entry.AffiliateAccountId)
            .Select(group => new
            {
                AffiliateAccountId = group.Key,
                Count = group.Select(entry => entry.OrderId).Distinct().Count()
            })
            .ToDictionaryAsync(row => row.AffiliateAccountId, row => row.Count, ct);

        var lastCommissionTimes = await query
            .GroupBy(entry => entry.AffiliateAccountId)
            .Select(group => new
            {
                AffiliateAccountId = group.Key,
                LastCommissionAtUtc = group.Max(entry => (DateTime?)entry.RecordedAtUtc)
            })
            .ToDictionaryAsync(row => row.AffiliateAccountId, row => row.LastCommissionAtUtc, ct);

        return ids.ToDictionary(
            id => id,
            id =>
            {
                var rowsForAffiliate = totalsRows
                    .Where(row => row.AffiliateAccountId == id)
                    .ToList();

                var summariesByCurrency = rowsForAffiliate
                    .GroupBy(row => NormalizeCurrency(row.Currency))
                    .Select(group =>
                    {
                        var earned = group.Where(row => row.EntryType == CommissionEntryType.Earned).Sum(row => row.Amount);
                        var reversed = group.Where(row => row.EntryType == CommissionEntryType.Reversed).Sum(row => row.Amount);

                        return new AffiliateEarningsSummaryDto
                        {
                            Currency = group.Key,
                            TotalEarned = earned,
                            TotalApproved = group.Where(row => row.EntryType == CommissionEntryType.Approved).Sum(row => row.Amount),
                            TotalPaid = group.Where(row => row.EntryType == CommissionEntryType.Paid).Sum(row => row.Amount),
                            TotalReversed = reversed,
                            NetEarnings = earned - reversed
                        };
                    })
                    .OrderBy(summary => summary.Currency)
                    .ToList();

                return new AffiliateLedgerTotals(
                    TotalEarned: summariesByCurrency.Sum(summary => summary.TotalEarned),
                    TotalApproved: summariesByCurrency.Sum(summary => summary.TotalApproved),
                    TotalPaid: summariesByCurrency.Sum(summary => summary.TotalPaid),
                    TotalReversed: summariesByCurrency.Sum(summary => summary.TotalReversed),
                    TotalOrders: orderCounts.GetValueOrDefault(id),
                    LastCommissionAtUtc: lastCommissionTimes.GetValueOrDefault(id),
                    EarningsByCurrency: summariesByCurrency);
            });
    }

    private async Task<AffiliateLedgerTotals> BuildAffiliateLedgerTotalsAsync(
        Guid affiliateAccountId,
        Guid tenantId,
        CancellationToken ct)
    {
        var totalsByAffiliate = await BuildAffiliateLedgerTotalsByAffiliateAsync(
            new[] { affiliateAccountId },
            tenantId,
            ct);

        return totalsByAffiliate.GetValueOrDefault(
            affiliateAccountId,
            new AffiliateLedgerTotals(0m, 0m, 0m, 0m, 0, null, new List<AffiliateEarningsSummaryDto>()));
    }

    private async Task<List<AffiliateEarningsSummaryDto>> BuildAffiliateLedgerEarningsSummaryAsync(
        Guid affiliateAccountId,
        Guid tenantId,
        CancellationToken ct)
    {
        var rows = await _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.AffiliateAccountId == affiliateAccountId &&
                entry.StoreTenantId == tenantId)
            .GroupBy(entry => new { entry.Currency, entry.EntryType })
            .Select(group => new
            {
                Currency = group.Key.Currency,
                EntryType = group.Key.EntryType,
                Amount = group.Sum(entry => entry.CommissionAmount)
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(row => NormalizeCurrency(row.Currency))
            .Select(group =>
            {
                var earned = group.Where(row => row.EntryType == CommissionEntryType.Earned).Sum(row => row.Amount);
                var reversed = group.Where(row => row.EntryType == CommissionEntryType.Reversed).Sum(row => row.Amount);

                return new AffiliateEarningsSummaryDto
                {
                    Currency = group.Key,
                    TotalEarned = earned,
                    TotalApproved = group.Where(row => row.EntryType == CommissionEntryType.Approved).Sum(row => row.Amount),
                    TotalPaid = group.Where(row => row.EntryType == CommissionEntryType.Paid).Sum(row => row.Amount),
                    TotalReversed = reversed,
                    NetEarnings = earned - reversed
                };
            })
            .OrderBy(summary => summary.Currency)
            .ToList();
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency)
            ? "UNKNOWN"
            : currency.Trim().ToUpperInvariant();
    }

    private sealed record AffiliateLedgerTotals(
        decimal TotalEarned,
        decimal TotalApproved,
        decimal TotalPaid,
        decimal TotalReversed,
        int TotalOrders,
        DateTime? LastCommissionAtUtc,
        List<AffiliateEarningsSummaryDto> EarningsByCurrency)
    {
        public decimal NetEarnings => TotalEarned - TotalReversed;
    }

    private sealed record AffiliateOrderLedgerEntryRow(
        Guid OrderId,
        Guid OrderItemId,
        CommissionEntryType EntryType,
        decimal BaseAmount,
        decimal CommissionAmount,
        string Currency);

    private sealed record AffiliateOrderLedgerSnapshot(
        decimal PaidAmount,
        decimal TotalCommission,
        decimal TotalApproved,
        decimal TotalPaid,
        decimal TotalReversed,
        string Currency,
        string Status,
        IReadOnlyDictionary<Guid, AffiliateOrderItemLedgerSnapshot> Items);

    private sealed record AffiliateOrderItemLedgerSnapshot(
        decimal PaidAmount,
        decimal CommissionAmount,
        decimal ReversedAmount,
        decimal NetCommissionAmount,
        bool IsReversed,
        bool IsPartiallyReversed,
        string SettlementStatus);

    /// <summary>
    /// Gets the current cashbox (1010) balance for a tenant.
    /// Returns 0 if the account doesn't exist.
    /// </summary>
    private async Task<decimal> GetCashboxBalanceAsync(Guid tenantId, CancellationToken ct)
    {
        var dashboard = await _treasuryReadService.GetDashboardAsync(
            tenantId,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date,
            ct);

        return dashboard.CashboxBalance;
    }

    private bool TryGetCurrentAdminUserId(out Guid adminUserId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(claimValue, out adminUserId);
    }
}

// ═══════════════════════════════════════════════════════════════════
// DTOs — Admin Affiliate Management
// ═══════════════════════════════════════════════════════════════════

public class AdminAffiliateListResponse
{
    public List<AdminAffiliateListItem> Affiliates { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class AdminAffiliateListItem
{
    public Guid AffiliateAccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ReferralCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalEarned { get; set; }
    public decimal TotalApproved { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal NetEarnings { get; set; }
    public List<AffiliateEarningsSummaryDto> EarningsByCurrency { get; set; } = new();
    public int TotalOrders { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    
    // AF-16: Payout Wallet (admin read-only in list view)
    public string? PayoutWalletNumber { get; set; }
    public string? PayoutWalletType { get; set; }
}

public class AdminAffiliateDetailResponse
{
    public Guid AffiliateAccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ReferralCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosureReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public decimal TotalEarned { get; set; }
    public decimal TotalApproved { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalReversed { get; set; }
    public decimal NetEarnings { get; set; }
    public List<AffiliateEarningsSummaryDto> EarningsByCurrency { get; set; } = new();
    public int TotalOrders { get; set; }
    public DateTime? LastCommissionAtUtc { get; set; }
    /// <summary>
    /// Current cashbox balance for fund availability display.
    /// </summary>
    public decimal CashboxBalance { get; set; }
    
    // AF-16: Payout Wallet (admin read-only)
    public string? PayoutWalletNumber { get; set; }
    public string? PayoutWalletType { get; set; }
    public DateTime? PayoutWalletUpdatedAtUtc { get; set; }
}

public class AdminSettlementActionRequest
{
    /// <summary>
    /// Specific order IDs to process. Ignored if ApplyAll is true.
    /// </summary>
    public List<Guid>? OrderIds { get; set; }

    /// <summary>
    /// If true, processes ALL eligible orders for this affiliate.
    /// ApplyAll=true for "Approve" → all Earned orders.
    /// ApplyAll=true for "Pay" → all Approved orders.
    /// </summary>
    public bool ApplyAll { get; set; }

    /// <summary>
    /// Immutable external payout reference, such as a bank transfer id or wallet transaction id.
    /// Required when marking affiliate commissions paid.
    /// </summary>
    public string? PaymentReference { get; set; }

    /// <summary>
    /// Confirmed external transfer amount for this payment reference.
    /// Defaults to the payout total when omitted.
    /// </summary>
    public decimal? ExternalTransferAmount { get; set; }

    public string? AdminNotes { get; set; }
}

public class AdminSettlementActionResponse
{
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class AdminSuspendRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class AdminCloseRequest
{
    public string Reason { get; set; } = string.Empty;
}


// ═══════════════════════════════════════════════════════════════════
// AF-18: BACKFILL REQUEST DTOS
// ═══════════════════════════════════════════════════════════════════

public record BackfillCommissionRequest
{
    public Guid OrderId { get; init; }
}

// ═══════════════════════════════════════════════════════════════════
// AF-38: WITHDRAWAL ENGINE DTOs
// ═══════════════════════════════════════════════════════════════════

public class AdminPayoutActionRequest
{
    public string? AdminNotes { get; set; }
}

public class AdminMarkPaidRequest
{
    public string? PaymentReference { get; set; }
    public decimal? ExternalTransferAmount { get; set; }
    public string? AdminNotes { get; set; }
}
