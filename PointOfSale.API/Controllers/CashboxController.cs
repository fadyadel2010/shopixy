using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.API.Extensions;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using System.Security.Claims;
using System.Text.Json;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Cashbox entity management controller.
///
/// Ledger is the only source of truth for balances.
/// DO NOT use CurrentBalance or ExpectedBalance for validation or business logic.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CashboxController : ControllerBase
{
    private const string TreasuryGovernanceRoles = "Owner,Admin,Manager";
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CashboxController> _logger;
    private readonly ITreasuryOperationService _treasuryOperationService;
    private readonly ITreasuryReadService _treasuryReadService;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;
    private readonly IAuditService _auditService;

    private const string OpeningFloatOffsetAccountCode = AccountCodes.CashFloatReserve;
    private const string ManualCashInOffsetAccountCode = AccountCodes.TreasurySuspense;
    private const string ManualCashOutOffsetAccountCode = AccountCodes.TreasurySuspense;
    private const string GeneralExpenseAccountCode = AccountCodes.Expenses;

    public CashboxController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<CashboxController> logger,
        ITreasuryOperationService treasuryOperationService,
        ITreasuryReadService treasuryReadService,
        ICashboxLedgerAccountService cashboxLedgerAccountService,
        IAuditService auditService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _treasuryOperationService = treasuryOperationService;
        _treasuryReadService = treasuryReadService;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
        _auditService = auditService;
    }

    /// <summary>
    /// Get all cashboxes with pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CashboxResponse>>> GetCashboxes(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Cashboxes
            .AsNoTracking()
            .Include(c => c.Warehouse)
            .Include(c => c.OpenedByUser)
            .Where(c => c.TenantId == _tenantContext.RequiredTenantId)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(c => c.WarehouseId == warehouseId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(c => c.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var cashboxes = await query
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<CashboxResponse>();
        foreach (var c in cashboxes)
        {
            var ledgerBalance = await GetCashboxLedgerBalanceAsync(c.Id, cancellationToken);
            items.Add(BuildCashboxResponse(c, ledgerBalance));
        }

        return Ok(new PagedResult<CashboxResponse>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Get cashbox by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CashboxResponse>> GetCashbox(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var cashbox = await _context.Cashboxes
            .AsNoTracking()
            .Include(c => c.Warehouse)
            .Include(c => c.OpenedByUser)
            .FirstOrDefaultAsync(c =>
                c.Id == id &&
                c.TenantId == _tenantContext.RequiredTenantId,
                cancellationToken);

        if (cashbox == null)
        {
            return NotFound(new { message = "Cashbox not found" });
        }

        var ledgerBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
        return Ok(BuildCashboxResponse(cashbox, ledgerBalance));
    }

    /// <summary>
    /// Create new cashbox.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CashboxResponse>> CreateCashbox(
        CreateCashboxRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        Warehouse? warehouse = null;
        if (request.WarehouseId.HasValue)
        {
            warehouse = await _context.Warehouses.FindAsync(new object?[] { request.WarehouseId.Value }, cancellationToken);
            if (warehouse == null)
            {
                return BadRequest(new { message = "Warehouse not found" });
            }
        }

        var operationId = Guid.NewGuid();
        var performedAtUtc = DateTime.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var cashbox = new Cashbox
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.RequiredTenantId,
            Name = request.Name,
            WarehouseId = request.WarehouseId,
            Description = request.Description,
            // DO NOT use CurrentBalance/ExpectedBalance for business logic. Ledger is authoritative.
            CurrentBalance = 0m,
            ExpectedBalance = 0m,
            OpeningBalance = 0m,
            IsActive = false,
            CreatedAt = performedAtUtc
        };

        _context.Cashboxes.Add(cashbox);
        await _context.SaveChangesAsync(cancellationToken);
        await _cashboxLedgerAccountService.EnsureCashboxAccountAsync(
            cashbox,
            warehouse?.Name,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var ledgerBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
        var response = BuildCashboxResponse(
            cashbox,
            ledgerBalance,
            warehouse?.Name ?? "System-wide",
            openedByUserName: null);

        await LogCashboxAuditAsync(
            actionType: "CashboxCreate",
            cashbox: cashbox,
            userId: userId,
            referenceId: operationId,
            performedAtUtc: performedAtUtc,
            description: $"Cashbox '{cashbox.Name}' created.",
            beforeData: null,
            afterData: BuildOperationAuditData(
                "CashboxCreate",
                userId,
                operationId,
                performedAtUtc,
                new
                {
                    cashboxId = cashbox.Id,
                    cashbox.Name,
                    cashbox.WarehouseId,
                    warehouseName = warehouse?.Name,
                    ledgerBalance
                }));

        return CreatedAtAction(nameof(GetCashbox), new { id = cashbox.Id }, response);
    }

    /// <summary>
    /// Update cashbox.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<CashboxResponse>> UpdateCashbox(
        Guid id,
        UpdateCashboxRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        var cashbox = await _context.Cashboxes
            .Include(c => c.Warehouse)
            .FirstOrDefaultAsync(c =>
                c.Id == id &&
                c.TenantId == _tenantContext.RequiredTenantId,
                cancellationToken);

        if (cashbox == null)
        {
            return NotFound(new { message = "Cashbox not found" });
        }

        var operationId = Guid.NewGuid();
        var performedAtUtc = DateTime.UtcNow;
        var ledgerBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
        var beforeSnapshot = BuildCashboxSnapshot(cashbox, ledgerBalance);

        cashbox.Name = request.Name;
        cashbox.Description = request.Description;

        await _context.SaveChangesAsync(cancellationToken);
        await _cashboxLedgerAccountService.EnsureCashboxAccountAsync(
            cashbox,
            cashbox.Warehouse?.Name,
            cancellationToken);

        var response = BuildCashboxResponse(cashbox, ledgerBalance);

        await LogCashboxAuditAsync(
            actionType: "CashboxUpdate",
            cashbox: cashbox,
            userId: userId,
            referenceId: operationId,
            performedAtUtc: performedAtUtc,
            description: $"Cashbox '{cashbox.Name}' updated.",
            beforeData: beforeSnapshot,
            afterData: BuildOperationAuditData(
                "CashboxUpdate",
                userId,
                operationId,
                performedAtUtc,
                BuildCashboxSnapshot(cashbox, ledgerBalance)));

        return Ok(response);
    }

    /// <summary>
    /// Delete cashbox (soft delete).
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCashbox(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        var cashbox = await _context.Cashboxes
            .FirstOrDefaultAsync(c =>
                c.Id == id &&
                c.TenantId == _tenantContext.RequiredTenantId,
                cancellationToken);

        if (cashbox == null)
        {
            return NotFound(new { message = "Cashbox not found" });
        }

        if (cashbox.IsActive)
        {
            return BadRequest(new { message = "Cannot delete an active cashbox. Please close it first." });
        }

        var operationId = Guid.NewGuid();
        var performedAtUtc = DateTime.UtcNow;
        var ledgerBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
        var beforeSnapshot = BuildCashboxSnapshot(cashbox, ledgerBalance);

        cashbox.IsDeleted = true;
        await _context.SaveChangesAsync(cancellationToken);

        await LogCashboxAuditAsync(
            actionType: "CashboxDelete",
            cashbox: cashbox,
            userId: userId,
            referenceId: operationId,
            performedAtUtc: performedAtUtc,
            description: $"Cashbox '{cashbox.Name}' soft-deleted.",
            beforeData: beforeSnapshot,
            afterData: BuildOperationAuditData(
                "CashboxDelete",
                userId,
                operationId,
                performedAtUtc,
                new
                {
                    cashboxId = cashbox.Id,
                    isDeleted = true,
                    ledgerBalance
                }));

        return NoContent();
    }

    /// <summary>
    /// Open cashbox and record the opening float in the ledger when provided.
    /// </summary>
    [HttpPost("{id}/open")]
    public async Task<ActionResult<CashboxResponse>> OpenCashbox(
        Guid id,
        OpenCashboxRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        if (request.OpeningBalance != 0m)
        {
            return BadRequest(new
            {
                code = "OPENING_FLOAT_TRANSFER_REQUIRED",
                message = "Cashboxes must open with zero new cash. Transfer existing authoritative treasury cash after opening."
            });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var cashbox = await _context.Cashboxes
                .FirstOrDefaultAsync(c =>
                    c.Id == id &&
                    c.TenantId == _tenantContext.RequiredTenantId,
                    cancellationToken);

            if (cashbox == null)
            {
                return NotFound(new { message = "Cashbox not found" });
            }

            if (cashbox.IsActive)
            {
                return BadRequest(new { message = "Cashbox is already open" });
            }

            var operationId = Guid.NewGuid();
            var performedAtUtc = DateTime.UtcNow;
            var operationReference = BuildOperationReference("OPEN_CASHBOX", cashbox.Id, operationId);
            var ledgerBalanceBefore = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
            PointOfSale.Core.Entities.Accounting.JournalEntry? openingJournal = null;

            if (request.OpeningBalance > 0)
            {
                var cashboxAccountCode = (await _cashboxLedgerAccountService.ResolveCashboxAccountAsync(
                    _tenantContext.RequiredTenantId,
                    cashbox.Id,
                    cancellationToken)).AccountCode;
                openingJournal = await _treasuryOperationService.CashInAsync(
                    _tenantContext.RequiredTenantId,
                    cashboxAccountCode,
                    OpeningFloatOffsetAccountCode,
                    request.OpeningBalance,
                    string.IsNullOrWhiteSpace(request.Notes)
                        ? $"Cashbox opening float for {cashbox.Name}"
                        : request.Notes,
                    operationReference,
                    operationId,
                    userId,
                    cancellationToken);
            }

            cashbox.IsActive = true;
            cashbox.OpenedAt = performedAtUtc;
            cashbox.OpenedByUserId = userId;
            cashbox.OpeningBalance = request.OpeningBalance;
            // DO NOT use CurrentBalance - use ledger.
            // DO NOT use ExpectedBalance - use ledger.

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var ledgerBalanceAfter = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
            var response = BuildCashboxResponse(cashbox, ledgerBalanceAfter);

            await LogCashboxAuditAsync(
                actionType: "CashboxOpen",
                cashbox: cashbox,
                userId: userId,
                referenceId: operationId,
                performedAtUtc: performedAtUtc,
                description: $"Cashbox '{cashbox.Name}' opened.",
                beforeData: BuildOperationAuditData(
                    "CashboxOpen",
                    userId,
                    operationId,
                    performedAtUtc,
                    new
                    {
                        cashboxId = cashbox.Id,
                        ledgerBalance = ledgerBalanceBefore,
                        wasActive = false
                    }),
                afterData: BuildOperationAuditData(
                    "CashboxOpen",
                    userId,
                    operationId,
                    performedAtUtc,
                    new
                    {
                        cashboxId = cashbox.Id,
                        openingBalance = request.OpeningBalance,
                        ledgerBalanceBefore,
                        ledgerBalanceAfter,
                        journalId = openingJournal?.Id,
                        journalNumber = openingJournal?.JournalNumber,
                        operationReference,
                        openedAtUtc = cashbox.OpenedAt
                    }));

            return Ok(response);
        }
        catch (InsufficientFundsException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxOpen", id);
        }
        catch (AccountNotFoundException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxOpen", id);
        }
        catch (InactiveAccountException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxOpen", id);
        }
        catch (InvalidJournalException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxOpen", id);
        }
        catch (TreasuryValidationException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxOpen", id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error opening cashbox {CashboxId}", id);
            return StatusCode(500, new { message = "An error occurred while opening the cashbox" });
        }
    }

    /// <summary>
    /// Close cashbox using ledger balance as the expected balance.
    /// </summary>
    [HttpPost("{id}/close")]
    [Authorize(Roles = TreasuryGovernanceRoles)]
    public async Task<ActionResult> CloseCashbox(
        Guid id,
        CloseCashboxRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ActualCash < 0m)
        {
            return BadRequest(new { message = "Actual cash cannot be negative." });
        }

        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var cashbox = await _context.Cashboxes
                .FirstOrDefaultAsync(c =>
                    c.Id == id &&
                    c.TenantId == _tenantContext.RequiredTenantId,
                    cancellationToken);

            if (cashbox == null)
            {
                return NotFound(new { message = "Cashbox not found" });
            }

            if (!cashbox.IsActive)
            {
                return BadRequest(new { message = "Cashbox is not open" });
            }

            var operationId = Guid.NewGuid();
            var performedAtUtc = DateTime.UtcNow;
            var expectedBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
            var difference = request.ActualCash - expectedBalance;
            var beforeSnapshot = BuildCashboxSnapshot(cashbox, expectedBalance);
            var afterAuditData = BuildOperationAuditData(
                "CashboxClose",
                userId,
                operationId,
                performedAtUtc,
                new
                {
                    cashboxId = cashbox.Id,
                    expectedBalance,
                    actualCash = request.ActualCash,
                    difference,
                    discrepancyDetected = Math.Abs(difference) > 0.01m
                });

            var affectedRows = await _context.Cashboxes
                .Where(c =>
                    c.Id == id &&
                    c.TenantId == _tenantContext.RequiredTenantId &&
                    c.IsActive)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.IsActive, false)
                    .SetProperty(c => c.OpenedAt, (DateTime?)null)
                    .SetProperty(c => c.OpenedByUserId, (Guid?)null),
                    cancellationToken);

            if (affectedRows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new { message = "Cashbox is already closed or was closed by another operation." });
            }

            cashbox.IsActive = false;
            cashbox.OpenedAt = null;
            cashbox.OpenedByUserId = null;

            AppendCashboxAuditLog(
                actionType: "CashboxClose",
                cashbox: cashbox,
                userId: userId,
                referenceId: operationId,
                performedAtUtc: performedAtUtc,
                description: $"Cashbox '{cashbox.Name}' closed.",
                beforeData: beforeSnapshot,
                afterData: afterAuditData);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Cashbox operation {ActionType} succeeded | CashboxId: {CashboxId} | UserId: {UserId} | ReferenceId: {ReferenceId} | TimestampUtc: {TimestampUtc}",
                "CashboxClose",
                cashbox.Id,
                userId,
                operationId,
                performedAtUtc);

            return Ok(new
            {
                message = "Cashbox closed successfully",
                expectedBalance,
                actualBalance = request.ActualCash,
                difference,
                referenceId = operationId
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error closing cashbox {CashboxId}", id);
            return StatusCode(500, new { message = "An error occurred while closing the cashbox" });
        }
    }

    /// <summary>
    /// Get cashbox transactions with filtering.
    /// </summary>
    [HttpGet("{id}/transactions")]
    public async Task<ActionResult<PagedResult<CashboxTransactionResponse>>> GetTransactions(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] CashboxTransactionType? transactionType = null,
        [FromQuery] PaymentMethod? paymentMethod = null,
        CancellationToken cancellationToken = default)
    {
        var cashbox = await _context.Cashboxes
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == id &&
                c.TenantId == _tenantContext.RequiredTenantId,
                cancellationToken);

        if (cashbox == null)
        {
            return NotFound(new { message = "Cashbox not found" });
        }

        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);
        var skip = PaginationGuard.GetSkip(page, pageSize);

        if (paymentMethod.HasValue && paymentMethod.Value != PaymentMethod.Cash)
        {
            return Ok(new PagedResult<CashboxTransactionResponse>
            {
                Items = new List<CashboxTransactionResponse>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
                TotalPages = 0
            });
        }

        var startUtc = startDate.HasValue
            ? startDate.Value.EnsureUtc()
            : DateTime.SpecifyKind(cashbox.CreatedAt.Date, DateTimeKind.Utc);
        var endUtc = endDate.HasValue
            ? endDate.Value.NormalizeInclusiveEndUtc()
            : DateTime.UtcNow;

        if (endUtc < startUtc)
        {
            return Ok(new PagedResult<CashboxTransactionResponse>
            {
                Items = new List<CashboxTransactionResponse>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
                TotalPages = 0
            });
        }

        var accountInfo = await _cashboxLedgerAccountService.GetCashboxAccountIfExistsAsync(
            _tenantContext.RequiredTenantId,
            cashbox.Id,
            cancellationToken);

        if (accountInfo == null)
        {
            return Ok(new PagedResult<CashboxTransactionResponse>
            {
                Items = new List<CashboxTransactionResponse>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
                TotalPages = 0
            });
        }

        var movementQuery = BuildCashboxLedgerMovementQuery(
            _tenantContext.RequiredTenantId,
            accountInfo.AccountId,
            startUtc,
            endUtc);

        if (transactionType.HasValue)
        {
            movementQuery = ApplyCashboxTransactionTypeFilter(movementQuery, transactionType.Value);
        }

        var totalCount = await movementQuery.CountAsync(cancellationToken);
        var movements = await movementQuery
            .OrderByDescending(movement => movement.TimestampUtc)
            .ThenByDescending(movement => movement.JournalNumber)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = movements
            .Select(movement => MapLedgerMovementToCashboxTransaction(cashbox.Id, movement))
            .ToList();

        return Ok(new PagedResult<CashboxTransactionResponse>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Record manual cash in.
    /// </summary>
    [HttpPost("{id}/cash-in")]
    public async Task<ActionResult<CashboxTransactionResponse>> CashIn(
        Guid id,
        CashTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        try
        {
            var cashbox = await _context.Cashboxes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (cashbox == null)
            {
                return NotFound(new { message = "Cashbox not found" });
            }

            if (!cashbox.IsActive)
            {
                return BadRequest(new { message = "Cashbox is not open" });
            }

            if (request.Amount <= 0)
            {
                return BadRequest(new { message = "Amount must be greater than zero" });
            }

            if (request.PaymentMethod.HasValue && request.PaymentMethod.Value != PaymentMethod.Cash)
            {
                return BadRequest(new { message = "Cashbox cash-in must use cash payment method" });
            }

            var operationId = Guid.NewGuid();
            var performedAtUtc = DateTime.UtcNow;
            var operationReference = BuildOperationReference("CASHBOX_CASH_IN", cashbox.Id, operationId);

            var cashboxAccountCode = (await _cashboxLedgerAccountService.ResolveCashboxAccountAsync(
                _tenantContext.RequiredTenantId,
                cashbox.Id,
                cancellationToken)).AccountCode;
            var journal = await _treasuryOperationService.CashInAsync(
                _tenantContext.RequiredTenantId,
                cashboxAccountCode,
                ManualCashInOffsetAccountCode,
                request.Amount,
                string.IsNullOrWhiteSpace(request.Description) ? "Manual cash in" : request.Description,
                operationReference,
                operationId,
                userId,
                cancellationToken);

            var ledgerBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);

            await LogCashboxAuditAsync(
                actionType: "CashboxCashIn",
                cashbox: cashbox,
                userId: userId,
                referenceId: operationId,
                performedAtUtc: performedAtUtc,
                description: $"Manual cash in recorded for cashbox '{cashbox.Name}'.",
                beforeData: null,
                afterData: BuildOperationAuditData(
                    "CashboxCashIn",
                    userId,
                    operationId,
                    performedAtUtc,
                    new
                    {
                        cashboxId = cashbox.Id,
                        amount = request.Amount,
                        ledgerBalanceAfter = ledgerBalance,
                        journalId = journal.Id,
                        journalNumber = journal.JournalNumber,
                        operationReference,
                        notes = request.Notes
                    }));

            return Ok(new CashboxTransactionResponse
            {
                Id = operationId,
                CashboxId = cashbox.Id,
                TransactionDate = performedAtUtc,
                TransactionType = CashboxTransactionType.ManualCashIn,
                TransactionTypeName = CashboxTransactionType.ManualCashIn.ToString(),
                Amount = request.Amount,
                BalanceAfter = ledgerBalance,
                PaymentMethod = PaymentMethod.Cash,
                PaymentMethodName = PaymentMethod.Cash.ToString(),
                ReferenceId = journal.Id,
                ReferenceNumber = journal.JournalNumber,
                Description = request.Description,
                Notes = request.Notes
            });
        }
        catch (InsufficientFundsException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashIn", id);
        }
        catch (AccountNotFoundException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashIn", id);
        }
        catch (InactiveAccountException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashIn", id);
        }
        catch (InvalidJournalException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashIn", id);
        }
        catch (TreasuryValidationException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashIn", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording cash in for cashbox {CashboxId}", id);
            return StatusCode(500, new { message = "An error occurred while recording cash in" });
        }
    }

    /// <summary>
    /// Record manual cash out.
    /// </summary>
    [HttpPost("{id}/cash-out")]
    public async Task<ActionResult<CashboxTransactionResponse>> CashOut(
        Guid id,
        CashTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        try
        {
            var cashbox = await _context.Cashboxes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (cashbox == null)
            {
                return NotFound(new { message = "Cashbox not found" });
            }

            if (!cashbox.IsActive)
            {
                return BadRequest(new { message = "Cashbox is not open" });
            }

            if (request.Amount <= 0)
            {
                return BadRequest(new { message = "Amount must be greater than zero" });
            }

            if (request.PaymentMethod.HasValue && request.PaymentMethod.Value != PaymentMethod.Cash)
            {
                return BadRequest(new { message = "Cashbox cash-out must use cash payment method" });
            }

            var availableBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
            if (availableBalance < request.Amount)
            {
                return BadRequest(new { message = "Insufficient cash in cashbox" });
            }

            var operationId = Guid.NewGuid();
            var performedAtUtc = DateTime.UtcNow;
            var operationReference = BuildOperationReference("CASHBOX_CASH_OUT", cashbox.Id, operationId);

            var cashboxAccountCode = (await _cashboxLedgerAccountService.ResolveCashboxAccountAsync(
                _tenantContext.RequiredTenantId,
                cashbox.Id,
                cancellationToken)).AccountCode;
            var journal = await _treasuryOperationService.CashOutAsync(
                _tenantContext.RequiredTenantId,
                cashboxAccountCode,
                ManualCashOutOffsetAccountCode,
                request.Amount,
                string.IsNullOrWhiteSpace(request.Description) ? "Manual cash out" : request.Description,
                operationReference,
                operationId,
                userId,
                cancellationToken);

            var ledgerBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);

            await LogCashboxAuditAsync(
                actionType: "CashboxCashOut",
                cashbox: cashbox,
                userId: userId,
                referenceId: operationId,
                performedAtUtc: performedAtUtc,
                description: $"Manual cash out recorded for cashbox '{cashbox.Name}'.",
                beforeData: null,
                afterData: BuildOperationAuditData(
                    "CashboxCashOut",
                    userId,
                    operationId,
                    performedAtUtc,
                    new
                    {
                        cashboxId = cashbox.Id,
                        amount = request.Amount,
                        ledgerBalanceAfter = ledgerBalance,
                        journalId = journal.Id,
                        journalNumber = journal.JournalNumber,
                        operationReference,
                        notes = request.Notes
                    }));

            return Ok(new CashboxTransactionResponse
            {
                Id = operationId,
                CashboxId = cashbox.Id,
                TransactionDate = performedAtUtc,
                TransactionType = CashboxTransactionType.ManualCashOut,
                TransactionTypeName = CashboxTransactionType.ManualCashOut.ToString(),
                Amount = -request.Amount,
                BalanceAfter = ledgerBalance,
                PaymentMethod = PaymentMethod.Cash,
                PaymentMethodName = PaymentMethod.Cash.ToString(),
                ReferenceId = journal.Id,
                ReferenceNumber = journal.JournalNumber,
                Description = request.Description,
                Notes = request.Notes
            });
        }
        catch (InsufficientFundsException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashOut", id);
        }
        catch (AccountNotFoundException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashOut", id);
        }
        catch (InactiveAccountException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashOut", id);
        }
        catch (InvalidJournalException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashOut", id);
        }
        catch (TreasuryValidationException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxCashOut", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording cash out for cashbox {CashboxId}", id);
            return StatusCode(500, new { message = "An error occurred while recording cash out" });
        }
    }

    /// <summary>
    /// Record expense payment from cashbox cash.
    /// </summary>
    [HttpPost("{id}/expense")]
    public async Task<ActionResult<CashboxTransactionResponse>> RecordExpense(
        Guid id,
        ExpenseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        try
        {
            var cashbox = await _context.Cashboxes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (cashbox == null)
            {
                return NotFound(new { message = "Cashbox not found" });
            }

            if (!cashbox.IsActive)
            {
                return BadRequest(new { message = "Cashbox is not open" });
            }

            if (request.Amount <= 0)
            {
                return BadRequest(new { message = "Amount must be greater than zero" });
            }

            if (request.PaymentMethod != PaymentMethod.Cash)
            {
                return BadRequest(new { message = "Cashbox expense must use cash payment method" });
            }

            var availableBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
            if (availableBalance < request.Amount)
            {
                return BadRequest(new { message = "Insufficient cash in cashbox" });
            }

            var operationId = Guid.NewGuid();
            var performedAtUtc = DateTime.UtcNow;
            var operationReference = BuildOperationReference("CASHBOX_EXPENSE", cashbox.Id, operationId);

            var cashboxAccountCode = (await _cashboxLedgerAccountService.ResolveCashboxAccountAsync(
                _tenantContext.RequiredTenantId,
                cashbox.Id,
                cancellationToken)).AccountCode;
            var journal = await _treasuryOperationService.RecordExpenseAsync(
                _tenantContext.RequiredTenantId,
                cashboxAccountCode,
                GeneralExpenseAccountCode,
                request.Amount,
                string.IsNullOrWhiteSpace(request.Description) ? "Cashbox expense" : request.Description,
                operationReference,
                operationId,
                userId,
                cancellationToken);

            var ledgerBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);

            await LogCashboxAuditAsync(
                actionType: "CashboxExpense",
                cashbox: cashbox,
                userId: userId,
                referenceId: operationId,
                performedAtUtc: performedAtUtc,
                description: $"Expense recorded for cashbox '{cashbox.Name}'.",
                beforeData: null,
                afterData: BuildOperationAuditData(
                    "CashboxExpense",
                    userId,
                    operationId,
                    performedAtUtc,
                    new
                    {
                        cashboxId = cashbox.Id,
                        amount = request.Amount,
                        ledgerBalanceAfter = ledgerBalance,
                        journalId = journal.Id,
                        journalNumber = journal.JournalNumber,
                        operationReference,
                        notes = request.Notes
                    }));

            return Ok(new CashboxTransactionResponse
            {
                Id = operationId,
                CashboxId = cashbox.Id,
                TransactionDate = performedAtUtc,
                TransactionType = CashboxTransactionType.Expense,
                TransactionTypeName = CashboxTransactionType.Expense.ToString(),
                Amount = -request.Amount,
                BalanceAfter = ledgerBalance,
                PaymentMethod = PaymentMethod.Cash,
                PaymentMethodName = PaymentMethod.Cash.ToString(),
                ReferenceId = journal.Id,
                ReferenceNumber = journal.JournalNumber,
                Description = request.Description,
                Notes = request.Notes
            });
        }
        catch (InsufficientFundsException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxExpense", id);
        }
        catch (AccountNotFoundException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxExpense", id);
        }
        catch (InactiveAccountException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxExpense", id);
        }
        catch (InvalidJournalException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxExpense", id);
        }
        catch (TreasuryValidationException ex)
        {
            return TreasuryOperationBadRequest(ex, "CashboxExpense", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording expense for cashbox {CashboxId}", id);
            return StatusCode(500, new { message = "An error occurred while recording expense" });
        }
    }

    /// <summary>
    /// Get cashbox summary for a period.
    /// </summary>
    [HttpGet("{id}/summary")]
    public async Task<ActionResult<CashboxSummaryResponse>> GetSummary(
        Guid id,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cashbox = await _context.Cashboxes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (cashbox == null)
            {
                return NotFound(new { message = "Cashbox not found" });
            }

            var transactions = await GetCashboxLedgerTransactionsAsync(cashbox, startDate, endDate, cancellationToken);

            var totalCashIn = transactions.Any()
                ? transactions.Where(t => t.Amount > 0).Sum(t => t.Amount)
                : 0m;
            var totalCashOut = transactions.Any()
                ? Math.Abs(transactions.Where(t => t.Amount < 0).Sum(t => t.Amount))
                : 0m;
            var transactionsByType = transactions.Any()
                ? transactions
                    .GroupBy(t => t.TransactionTypeName)
                    .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount))
                : new Dictionary<string, decimal>();

            var ledgerBalance = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);

            return Ok(new CashboxSummaryResponse
            {
                CashboxId = cashbox.Id,
                CashboxName = cashbox.Name,
                CurrentBalance = ledgerBalance,
                OpeningBalance = cashbox.OpeningBalance,
                TotalCashIn = totalCashIn,
                TotalCashOut = totalCashOut,
                NetChange = totalCashIn - totalCashOut,
                TransactionsByType = transactionsByType,
                PeriodStart = startDate,
                PeriodEnd = endDate
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cashbox summary for cashbox {CashboxId}", id);
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Get daily report for cashbox.
    /// </summary>
    [HttpGet("{id}/daily-report")]
    public async Task<ActionResult<DailyReportResponse>> GetDailyReport(
        Guid id,
        [FromQuery] DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        var cashbox = await _context.Cashboxes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (cashbox == null)
        {
            return NotFound(new { message = "Cashbox not found" });
        }

        var reportDate = date?.Date ?? DateTime.UtcNow.Date;
        var transactions = (await GetCashboxLedgerTransactionsAsync(cashbox, reportDate, reportDate, cancellationToken))
            .OrderBy(t => t.TransactionDate)
            .ToList();

        var openingBalance = await GetCashboxOpeningBalanceForDateAsync(cashbox, reportDate, transactions, cancellationToken);
        var totalSales = transactions.Where(t => t.TransactionType == CashboxTransactionType.Sale).Sum(t => t.Amount);
        var totalRefunds = Math.Abs(transactions.Where(t => t.TransactionType == CashboxTransactionType.SaleRefund).Sum(t => t.Amount));
        var totalPurchasePayments = Math.Abs(transactions.Where(t => t.TransactionType == CashboxTransactionType.PurchasePayment).Sum(t => t.Amount));
        var totalCashIn = transactions.Where(t => t.TransactionType == CashboxTransactionType.ManualCashIn).Sum(t => t.Amount);
        var totalCashOut = Math.Abs(transactions.Where(t => t.TransactionType == CashboxTransactionType.ManualCashOut).Sum(t => t.Amount));
        var totalExpenses = Math.Abs(transactions.Where(t => t.TransactionType == CashboxTransactionType.Expense).Sum(t => t.Amount));
        var closingBalance = transactions.LastOrDefault()?.BalanceAfter ?? openingBalance;

        return Ok(new DailyReportResponse
        {
            CashboxId = cashbox.Id,
            CashboxName = cashbox.Name,
            ReportDate = reportDate,
            OpeningBalance = openingBalance,
            TotalSales = totalSales,
            TotalRefunds = totalRefunds,
            TotalPurchasePayments = totalPurchasePayments,
            TotalCashIn = totalCashIn,
            TotalCashOut = totalCashOut,
            TotalExpenses = totalExpenses,
            ClosingBalance = closingBalance,
            Transactions = transactions
        });
    }

    /// <summary>
    /// Get all cashboxes for a warehouse.
    /// </summary>
    [HttpGet("warehouse/{warehouseId}")]
    public async Task<ActionResult<List<CashboxResponse>>> GetWarehouseCashboxes(
        Guid warehouseId,
        CancellationToken cancellationToken = default)
    {
        var cashboxes = await _context.Cashboxes
            .AsNoTracking()
            .Include(c => c.Warehouse)
            .Include(c => c.OpenedByUser)
            .Where(c => c.WarehouseId == warehouseId)
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var responses = new List<CashboxResponse>();
        foreach (var c in cashboxes)
        {
            var ledgerBalance = await GetCashboxLedgerBalanceAsync(c.Id, cancellationToken);
            responses.Add(BuildCashboxResponse(c, ledgerBalance));
        }
        return Ok(responses);
    }

    /// <summary>
    /// Set the initial opening balance and journal it into the ledger.
    /// </summary>
    [HttpPost("{id}/opening-balance")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<CashboxResponse>> SetOpeningBalance(
        Guid id,
        SetOpeningBalanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        if (request.Amount != 0m)
        {
            return BadRequest(new
            {
                code = "OPENING_BALANCE_AUTHORITY_REQUIRED",
                message = "Cashbox opening balances cannot create cash. Use an authoritative treasury transfer."
            });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var cashbox = await _context.Cashboxes
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (cashbox == null)
            {
                return NotFound(new { message = "Cashbox not found" });
            }

            if (cashbox.OpeningBalanceAmount.HasValue)
            {
                return BadRequest(new { message = "Opening balance already defined." });
            }

            var operationId = Guid.NewGuid();
            var performedAtUtc = DateTime.UtcNow;
            var operationReference = BuildOperationReference("SET_OPENING_BALANCE", cashbox.Id, operationId);
            var ledgerBalanceBefore = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
            PointOfSale.Core.Entities.Accounting.JournalEntry? openingJournal = null;

            if (request.Amount > 0)
            {
                var cashboxAccountCode = (await _cashboxLedgerAccountService.ResolveCashboxAccountAsync(
                    _tenantContext.RequiredTenantId,
                    cashbox.Id,
                    cancellationToken)).AccountCode;
                openingJournal = await _treasuryOperationService.CashInAsync(
                    _tenantContext.RequiredTenantId,
                    cashboxAccountCode,
                    OpeningFloatOffsetAccountCode,
                    request.Amount,
                    string.IsNullOrWhiteSpace(request.Notes)
                        ? $"Initial opening balance for {cashbox.Name}"
                        : request.Notes,
                    operationReference,
                    operationId,
                    userId,
                    cancellationToken);
            }

            cashbox.OpeningBalanceAmount = request.Amount;
            cashbox.OpeningBalanceDate = DateTime.SpecifyKind(request.Date, DateTimeKind.Utc);
            cashbox.IsActive = true;
            cashbox.OpenedAt = performedAtUtc;
            cashbox.OpenedByUserId = userId;
            cashbox.OpeningBalance = request.Amount;
            // DO NOT use CurrentBalance - use ledger.
            // DO NOT use ExpectedBalance - use ledger.

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var ledgerBalanceAfter = await GetCashboxLedgerBalanceAsync(cashbox.Id, cancellationToken);
            var response = BuildCashboxResponse(cashbox, ledgerBalanceAfter);

            await LogCashboxAuditAsync(
                actionType: "CashboxSetOpeningBalance",
                cashbox: cashbox,
                userId: userId,
                referenceId: operationId,
                performedAtUtc: performedAtUtc,
                description: $"Initial opening balance set for cashbox '{cashbox.Name}'.",
                beforeData: BuildOperationAuditData(
                    "CashboxSetOpeningBalance",
                    userId,
                    operationId,
                    performedAtUtc,
                    new
                    {
                        cashboxId = cashbox.Id,
                        ledgerBalance = ledgerBalanceBefore,
                        openingBalanceAmount = (decimal?)null
                    }),
                afterData: BuildOperationAuditData(
                    "CashboxSetOpeningBalance",
                    userId,
                    operationId,
                    performedAtUtc,
                    new
                    {
                        cashboxId = cashbox.Id,
                        request.Amount,
                        openingBalanceDate = cashbox.OpeningBalanceDate,
                        ledgerBalanceBefore,
                        ledgerBalanceAfter,
                        journalId = openingJournal?.Id,
                        journalNumber = openingJournal?.JournalNumber,
                        operationReference
                    }));

            return Ok(response);
        }
        catch (InsufficientFundsException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxSetOpeningBalance", id);
        }
        catch (AccountNotFoundException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxSetOpeningBalance", id);
        }
        catch (InactiveAccountException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxSetOpeningBalance", id);
        }
        catch (InvalidJournalException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxSetOpeningBalance", id);
        }
        catch (TreasuryValidationException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TreasuryOperationBadRequest(ex, "CashboxSetOpeningBalance", id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error setting opening balance for cashbox {CashboxId}", id);
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Get opening balance for cashbox.
    /// </summary>
    [HttpGet("{id}/opening-balance")]
    public async Task<ActionResult<object>> GetOpeningBalance(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var cashbox = await _context.Cashboxes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (cashbox == null)
        {
            return NotFound(new { message = "Cashbox not found" });
        }

        if (!cashbox.OpeningBalanceAmount.HasValue)
        {
            return NotFound(new { message = "Opening balance not defined." });
        }

        return Ok(new
        {
            Amount = cashbox.OpeningBalanceAmount,
            Date = cashbox.OpeningBalanceDate,
            IsOpeningBalance = true
        });
    }

    private ActionResult TreasuryOperationBadRequest(Exception ex, string operationType, Guid cashboxId)
    {
        _logger.LogWarning(
            ex,
            "Cashbox operation {OperationType} rejected | CashboxId: {CashboxId}",
            operationType,
            cashboxId);

        return BadRequest(new { message = ex.Message });
    }

    private async Task<decimal> GetCashboxLedgerBalanceAsync(Guid cashboxId, CancellationToken cancellationToken)
    {
        var accountInfo = await _cashboxLedgerAccountService.GetCashboxAccountIfExistsAsync(
            _tenantContext.RequiredTenantId,
            cashboxId,
            cancellationToken);

        if (accountInfo == null)
        {
            return 0m;
        }

        return await _treasuryReadService.GetAccountBalanceAsync(
            _tenantContext.RequiredTenantId,
            accountInfo.AccountCode,
            cancellationToken);
    }

    private async Task<List<CashboxTransactionResponse>> GetCashboxLedgerTransactionsAsync(
        Cashbox cashbox,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var startUtc = startDate.HasValue
            ? startDate.Value.EnsureUtc()
            : DateTime.SpecifyKind(cashbox.CreatedAt.Date, DateTimeKind.Utc);
        var endUtc = endDate.HasValue
            ? endDate.Value.NormalizeInclusiveEndUtc()
            : DateTime.UtcNow;

        if (endUtc < startUtc)
        {
            return new List<CashboxTransactionResponse>();
        }

        var accountInfo = await _cashboxLedgerAccountService.GetCashboxAccountIfExistsAsync(
            _tenantContext.RequiredTenantId,
            cashbox.Id,
            cancellationToken);

        if (accountInfo == null)
        {
            return new List<CashboxTransactionResponse>();
        }

        var movements = await BuildCashboxLedgerMovementQuery(
                _tenantContext.RequiredTenantId,
                accountInfo.AccountId,
                startUtc,
                endUtc)
            .OrderBy(movement => movement.TimestampUtc)
            .ThenBy(movement => movement.JournalNumber)
            .ToListAsync(cancellationToken);

        return movements
            .Select(m => MapLedgerMovementToCashboxTransaction(cashbox.Id, m))
            .ToList();
    }

    private IQueryable<TreasuryMovementDto> BuildCashboxLedgerMovementQuery(
        Guid tenantId,
        Guid accountId,
        DateTime startUtc,
        DateTime endUtc)
    {
        return _context.ProjectToTreasuryMovements(tenantId, accountId, startUtc, endUtc);
    }

    private static IQueryable<TreasuryMovementDto> ApplyCashboxTransactionTypeFilter(
        IQueryable<TreasuryMovementDto> movementQuery,
        CashboxTransactionType transactionType)
    {
        return transactionType switch
        {
            CashboxTransactionType.OpeningBalance => movementQuery.Where(movement =>
                movement.SourceReference != null &&
                (movement.SourceReference.StartsWith("OPEN_CASHBOX:") ||
                 movement.SourceReference.StartsWith("SET_OPENING_BALANCE:"))),
            CashboxTransactionType.Sale => movementQuery.Where(movement => movement.SourceType == "SalesInvoice"),
            CashboxTransactionType.SaleRefund => movementQuery.Where(movement => movement.SourceType == "Return"),
            CashboxTransactionType.PurchasePayment => movementQuery.Where(movement =>
                movement.SourceType == "PurchaseInvoice" || movement.SourceType == "SupplierPayment"),
            CashboxTransactionType.Expense => movementQuery.Where(movement =>
                movement.SourceType == "Expense" || movement.SourceType == "TreasuryExpense"),
            CashboxTransactionType.ManualCashIn => movementQuery.Where(movement =>
                movement.SourceType == "TreasuryCashIn" ||
                ((movement.SourceType == null ||
                  (movement.SourceType != "SalesInvoice" &&
                   movement.SourceType != "Return" &&
                   movement.SourceType != "PurchaseInvoice" &&
                   movement.SourceType != "SupplierPayment" &&
                   movement.SourceType != "Expense" &&
                   movement.SourceType != "TreasuryExpense" &&
                   movement.SourceType != "TreasuryCashOut")) &&
                 (movement.SourceReference == null ||
                  (!movement.SourceReference.StartsWith("OPEN_CASHBOX:") &&
                   !movement.SourceReference.StartsWith("SET_OPENING_BALANCE:"))) &&
                 movement.Debit > 0)),
            CashboxTransactionType.ManualCashOut => movementQuery.Where(movement =>
                movement.SourceType == "TreasuryCashOut" ||
                ((movement.SourceType == null ||
                  (movement.SourceType != "SalesInvoice" &&
                   movement.SourceType != "Return" &&
                   movement.SourceType != "PurchaseInvoice" &&
                   movement.SourceType != "SupplierPayment" &&
                   movement.SourceType != "Expense" &&
                   movement.SourceType != "TreasuryExpense" &&
                   movement.SourceType != "TreasuryCashIn")) &&
                 (movement.SourceReference == null ||
                  (!movement.SourceReference.StartsWith("OPEN_CASHBOX:") &&
                   !movement.SourceReference.StartsWith("SET_OPENING_BALANCE:"))) &&
                 movement.Credit > 0)),
            _ => movementQuery.Where(_ => false)
        };
    }

    private async Task<decimal> GetCashboxOpeningBalanceForDateAsync(
        Cashbox cashbox,
        DateTime reportDate,
        IReadOnlyList<CashboxTransactionResponse> dayTransactions,
        CancellationToken cancellationToken)
    {
        if (dayTransactions.Count > 0)
        {
            var explicitOpeningEntry = dayTransactions
                .FirstOrDefault(t => t.TransactionType == CashboxTransactionType.OpeningBalance);

            if (explicitOpeningEntry != null)
            {
                return explicitOpeningEntry.Amount;
            }

            var firstTransaction = dayTransactions[0];
            return firstTransaction.BalanceAfter - firstTransaction.Amount;
        }

        var priorDay = reportDate.Date.AddDays(-1);
        if (priorDay < cashbox.CreatedAt.Date)
        {
            return 0m;
        }

        var accountInfo = await _cashboxLedgerAccountService.GetCashboxAccountIfExistsAsync(
            _tenantContext.RequiredTenantId,
            cashbox.Id,
            cancellationToken);

        if (accountInfo == null)
        {
            return 0m;
        }

        var priorMovements = await _treasuryReadService.GetMovementsAsync(
            _tenantContext.RequiredTenantId,
            accountInfo.AccountCode,
            DateTime.SpecifyKind(cashbox.CreatedAt.Date, DateTimeKind.Utc),
            DateTime.SpecifyKind(priorDay, DateTimeKind.Utc),
            cancellationToken);

        return priorMovements.LastOrDefault()?.BalanceAfter ?? 0m;
    }

    private static CashboxTransactionResponse MapLedgerMovementToCashboxTransaction(
        Guid cashboxId,
        TreasuryMovementDto movement)
    {
        var transactionType = MapLedgerMovementType(movement);

        return new CashboxTransactionResponse
        {
            Id = movement.Id,
            CashboxId = cashboxId,
            TransactionDate = GetMovementTimestampUtc(movement),
            TransactionType = transactionType,
            TransactionTypeName = transactionType.ToString(),
            Amount = GetSignedAmount(movement),
            BalanceAfter = movement.BalanceAfter,
            PaymentMethod = PaymentMethod.Cash,
            PaymentMethodName = PaymentMethod.Cash.ToString(),
            ReferenceId = movement.ReferenceId != default ? movement.ReferenceId : movement.JournalId,
            ReferenceNumber = string.IsNullOrWhiteSpace(movement.SourceReference)
                ? movement.JournalNumber
                : movement.SourceReference,
            Description = movement.Description,
            UserName = movement.CreatedByUserName
        };
    }

    private static CashboxTransactionType MapLedgerMovementType(TreasuryMovementDto movement)
    {
        if (movement.SourceReference?.StartsWith("OPEN_CASHBOX:", StringComparison.OrdinalIgnoreCase) == true ||
            movement.SourceReference?.StartsWith("SET_OPENING_BALANCE:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return CashboxTransactionType.OpeningBalance;
        }

        return movement.SourceType switch
        {
            "SalesInvoice" => CashboxTransactionType.Sale,
            "Return" => CashboxTransactionType.SaleRefund,
            "PurchaseInvoice" => CashboxTransactionType.PurchasePayment,
            "SupplierPayment" => CashboxTransactionType.PurchasePayment,
            "Expense" => CashboxTransactionType.Expense,
            "TreasuryExpense" => CashboxTransactionType.Expense,
            "TreasuryCashIn" => CashboxTransactionType.ManualCashIn,
            "TreasuryCashOut" => CashboxTransactionType.ManualCashOut,
            _ when GetSignedAmount(movement) >= 0 => CashboxTransactionType.ManualCashIn,
            _ => CashboxTransactionType.ManualCashOut
        };
    }

    private static decimal GetSignedAmount(TreasuryMovementDto movement)
    {
        return movement.Debit > 0 ? movement.Debit : -movement.Credit;
    }

    private static DateTime GetMovementTimestampUtc(TreasuryMovementDto movement)
    {
        return movement.TimestampUtc == default
            ? movement.Date.EnsureUtc()
            : movement.TimestampUtc.EnsureUtc();
    }

    private static CashboxResponse BuildCashboxResponse(
        Cashbox cashbox,
        decimal ledgerBalance,
        string? warehouseName = null,
        string? openedByUserName = null)
    {
        // DO NOT use CurrentBalance - use ledger.
        return new CashboxResponse
        {
            Id = cashbox.Id,
            Name = cashbox.Name,
            WarehouseId = cashbox.WarehouseId,
            WarehouseName = warehouseName ?? cashbox.Warehouse?.Name ?? "System-wide",
            CurrentBalance = ledgerBalance,
            IsActive = cashbox.IsActive,
            OpenedAt = cashbox.OpenedAt,
            OpenedByUserName = openedByUserName ?? cashbox.OpenedByUser?.Username,
            OpeningBalance = cashbox.OpeningBalance,
            ExpectedBalance = ledgerBalance,
            Description = cashbox.Description,
            OpeningBalanceAmount = cashbox.OpeningBalanceAmount,
            OpeningBalanceDate = cashbox.OpeningBalanceDate,
            CreatedAt = cashbox.CreatedAt
        };
    }

    private static object BuildCashboxSnapshot(Cashbox cashbox, decimal ledgerBalance)
    {
        return new
        {
            cashbox.Id,
            cashbox.Name,
            cashbox.WarehouseId,
            cashbox.IsActive,
            cashbox.OpenedAt,
            cashbox.OpenedByUserId,
            cashbox.OpeningBalance,
            cashbox.OpeningBalanceAmount,
            cashbox.OpeningBalanceDate,
            cashbox.Description,
            ledgerBalance
        };
    }

    private static object BuildOperationAuditData(
        string operationType,
        Guid userId,
        Guid referenceId,
        DateTime timestampUtc,
        object details)
    {
        return new
        {
            operationType,
            userId,
            referenceId,
            timestampUtc,
            details
        };
    }

    private async Task LogCashboxAuditAsync(
        string actionType,
        Cashbox cashbox,
        Guid userId,
        Guid referenceId,
        DateTime performedAtUtc,
        string description,
        object? beforeData,
        object? afterData)
    {
        _logger.LogInformation(
            "Cashbox operation {ActionType} succeeded | CashboxId: {CashboxId} | UserId: {UserId} | ReferenceId: {ReferenceId} | TimestampUtc: {TimestampUtc}",
            actionType,
            cashbox.Id,
            userId,
            referenceId,
            performedAtUtc);

        await _auditService.LogAction(
            actionType,
            nameof(Cashbox),
            cashbox.Id.ToString(),
            description,
            beforeData,
            afterData,
            referenceId.ToString(),
            success: true,
            source: nameof(CashboxController));
    }

    private void AppendCashboxAuditLog(
        string actionType,
        Cashbox cashbox,
        Guid userId,
        Guid referenceId,
        DateTime performedAtUtc,
        string description,
        object? beforeData,
        object? afterData)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            TenantId = _tenantContext.RequiredTenantId,
            TimestampUtc = performedAtUtc,
            UserId = userId,
            ActionType = actionType,
            EntityType = nameof(Cashbox),
            EntityId = cashbox.Id.ToString(),
            Description = description,
            BeforeDataJson = beforeData == null ? null : JsonSerializer.Serialize(beforeData),
            AfterDataJson = afterData == null ? null : JsonSerializer.Serialize(afterData),
            ReferenceNumber = referenceId.ToString(),
            Source = nameof(CashboxController),
            SuccessFlag = true
        });
    }

    private static string BuildOperationReference(string operationType, Guid cashboxId, Guid referenceId)
    {
        return $"{operationType}:{cashboxId}:{referenceId}";
    }

    private bool TryResolveCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out userId))
        {
            userId = default;
            return false;
        }

        return true;
    }
}
