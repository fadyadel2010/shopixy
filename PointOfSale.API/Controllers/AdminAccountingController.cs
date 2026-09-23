using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;

namespace PointOfSale.Api.Controllers;

/// <summary>
/// Admin endpoints for managing the Unified Financial Engine.
/// </summary>
[ApiController]
[Route("api/admin/accounting")]
public class AdminAccountingController : ControllerBase
{
    private readonly ITenantAccountSeeder _seeder;
    private readonly ITreasuryTransferService _transferService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAccountingController> _logger;
    
    public AdminAccountingController(
        ITenantAccountSeeder seeder,
        ITreasuryTransferService transferService,
        ITenantContext tenantContext,
        ILogger<AdminAccountingController> logger)
    {
        _seeder = seeder;
        _transferService = transferService;
        _tenantContext = tenantContext;
        _logger = logger;
    }
    
    private Guid TenantId => _tenantContext.RequiredTenantId;
    
    /// <summary>
    /// Seeds the Chart of Accounts, Payment Methods, and Treasury Accounts for the current tenant.
    /// Idempotent - safe to call multiple times.
    /// </summary>
    [HttpPost("seed")]
    public async Task<IActionResult> SeedAccounts(CancellationToken ct)
    {
        var tenantId = TenantId;

        _logger.LogInformation("Admin seeding accounts for tenant {TenantId}", tenantId);

        await _seeder.SeedAsync(tenantId, ct);

        return Ok(new { message = "Chart of accounts seeded successfully", tenantId });
    }
    
    /// <summary>
    /// Seeds only the Chart of Accounts.
    /// </summary>
    [HttpPost("seed/accounts")]
    public async Task<IActionResult> SeedAccountsOnly(CancellationToken ct)
    {
        var tenantId = TenantId;

        await _seeder.SeedAccountsAsync(tenantId, ct);

        return Ok(new { message = "Accounts seeded successfully", tenantId });
    }
    
    /// <summary>
    /// Seeds only Payment Methods.
    /// </summary>
    [HttpPost("seed/payment-methods")]
    public async Task<IActionResult> SeedPaymentMethodsOnly(CancellationToken ct)
    {
        var tenantId = TenantId;

        await _seeder.SeedPaymentMethodsAsync(tenantId, ct);

        return Ok(new { message = "Payment methods seeded successfully", tenantId });
    }
    
    /// <summary>
    /// Seeds only Treasury Accounts.
    /// </summary>
    [HttpPost("seed/treasury")]
    public async Task<IActionResult> SeedTreasuryOnly(CancellationToken ct)
    {
        var tenantId = TenantId;

        await _seeder.SeedTreasuryAccountsAsync(tenantId, ct);

        return Ok(new { message = "Treasury accounts seeded successfully", tenantId });
    }
    
    /// <summary>
    /// Transfers funds between accounts.
    /// </summary>
    [HttpPost("treasury/transfer")]
    public async Task<IActionResult> TreasuryTransfer([FromBody] TreasuryTransferRequest request, CancellationToken ct)
    {
        var tenantId = TenantId;

        if (request.Amount <= 0)
            return BadRequest("Amount must be positive");
        if (request.TransferId == Guid.Empty)
            return BadRequest("TransferId is required for replay-safe treasury mutation");
        
        var journalId = await _transferService.TransferAsync(
            tenantId,
            request.FromAccountId,
            request.ToAccountId,
            request.Amount,
            request.Reference ?? "Manual transfer",
            request.TransferId,
            ct);
        
        return Ok(new { journalId, message = "Transfer completed successfully" });
    }
    
    /// <summary>
    /// Settles a clearing account to a target account.
    /// </summary>
    [HttpPost("treasury/settle")]
    public async Task<IActionResult> SettleClearing([FromBody] SettlementRequest request, CancellationToken ct)
    {
        await Task.CompletedTask;
        return StatusCode(StatusCodes.Status410Gone, new
        {
            code = "SETTLEMENT_AUTHORITY_REQUIRED",
            message = "Manual clearing settlement is disabled. Use the certified payment settlement workflow."
        });
    }
    
    /// <summary>
    /// Deposits cash from cashbox to bank.
    /// </summary>
    [HttpPost("treasury/deposit")]
    public async Task<IActionResult> DepositToBank([FromBody] BankDepositRequest request, CancellationToken ct)
    {
        var tenantId = TenantId;

        if (request.Amount <= 0)
            return BadRequest("Amount must be positive");
        if (request.DepositId == Guid.Empty)
            return BadRequest("DepositId is required for replay-safe deposit lineage");
        
        var journalId = await _transferService.DepositToBankAsync(
            tenantId,
            request.CashboxAccountId,
            request.BankAccountId,
            request.Amount,
            request.Reference ?? "Bank deposit",
            request.DepositId,
            ct);
        
        return Ok(new { journalId, message = "Deposit completed successfully" });
    }
}

// Request DTOs
public record TreasuryTransferRequest(
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string? Reference = null,
    Guid TransferId = default);

public record SettlementRequest(
    Guid ClearingAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string? Reference = null,
    Guid SettlementId = default);

public record BankDepositRequest(
    Guid CashboxAccountId,
    Guid BankAccountId,
    decimal Amount,
    string? Reference = null,
    Guid DepositId = default);
