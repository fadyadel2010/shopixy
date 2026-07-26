using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.API.Controllers.Admin;

/// <summary>
/// WhatsApp Admin Controller - TRANSPORT AWARE
/// 
/// Routes to Playwright (legacy) or Baileys (new) based on tenant's UseBaileys flag.
/// This enables gradual migration without code changes in the frontend.
/// </summary>
[ApiController]
[Route("api/admin/whatsapp")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class WhatsAppAdminController : ControllerBase
{
    private readonly IWhatsAppAccountService _accountService;
    private readonly IWhatsAppWebEngine _webEngine; // Playwright (legacy)
    private readonly IServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext; 
    private readonly IWhatsAppKillSwitch _killSwitch;
    private readonly IWhatsAppMessageLogService _logService;
    private readonly ILogger<WhatsAppAdminController> _logger;
    private Guid TenantId => _tenantContext.RequiredTenantId;

    public WhatsAppAdminController(
        IWhatsAppAccountService accountService,
        IWhatsAppWebEngine webEngine,
        IServiceProvider serviceProvider,
        ApplicationDbContext dbContext,
        ITenantContext tenantContext,
        IWhatsAppKillSwitch killSwitch,
        IWhatsAppMessageLogService logService,
        ILogger<WhatsAppAdminController> logger)
    {
        _accountService = accountService;
        _webEngine = webEngine;
        _serviceProvider = serviceProvider;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _killSwitch = killSwitch;
        _logService = logService;
        _logger = logger;
    }

    /// <summary>
    /// Check if tenant uses Baileys transport
    /// </summary>
    private async Task<bool> UseBaileysAsync(CancellationToken ct)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == TenantId, ct);
        
        return tenant?.UseBaileys ?? false;
    }

    /// <summary>
    /// Get Baileys transport from DI
    /// </summary>
    private IWhatsAppTransport GetBaileysTransport()
    {
        return _serviceProvider.GetRequiredKeyedService<IWhatsAppTransport>("baileys");
    }

    [HttpGet("accounts")]
    public async Task<ActionResult<List<WhatsAppAccountDto>>> GetAccounts(CancellationToken ct)
    {
        var accounts = await _accountService.GetAccountsAsync(TenantId, ct);
        return Ok(accounts.Select(MapAccount).ToList());
    }
    
    [HttpGet("accounts/{id}")]
    public async Task<ActionResult<WhatsAppAccountDto>> GetAccount(Guid id, CancellationToken ct)
    {
        var account = await _accountService.GetTenantAccountAsync(TenantId, id, ct);
        if (account == null) return NotFound();
        return Ok(MapAccount(account));
    }

    [HttpPost("accounts")]
    public async Task<ActionResult<WhatsAppAccountDto>> CreateAccount(CreateWhatsAppAccountDto dto, CancellationToken ct)
    {
        var account = new WhatsAppAccount
        {
            TenantId = TenantId,
            DisplayName = dto.DisplayName,
            PhoneNumber = dto.PhoneNumber,
            UseForOrderConfirmation = dto.UseForOrderConfirmation,
            UseForOtpVerification = dto.UseForOtpVerification,
            UseForPaymentApproval = dto.UseForPaymentApproval,
            UseForAbandonedCartRecovery = dto.UseForAbandonedCartRecovery,
            Status = WhatsAppAccountStatus.PendingQR,
            Enabled = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        
        var created = await _accountService.CreateAccountAsync(account, ct);
        return CreatedAtAction(nameof(GetAccount), new { id = created.Id }, MapAccount(created));
    }

    [HttpPut("accounts/{id}")]
    public async Task<ActionResult<WhatsAppAccountDto>> UpdateAccount(Guid id, UpdateWhatsAppAccountDto dto, CancellationToken ct)
    {
        var account = await _accountService.GetTenantAccountAsync(TenantId, id, ct);
        if (account == null) return NotFound();

        account.DisplayName = dto.DisplayName;
        account.PhoneNumber = dto.PhoneNumber;
        account.UseForOrderConfirmation = dto.UseForOrderConfirmation;
        account.UseForOtpVerification = dto.UseForOtpVerification;
        account.UseForPaymentApproval = dto.UseForPaymentApproval;
        account.UseForAbandonedCartRecovery = dto.UseForAbandonedCartRecovery;
        
        var updated = await _accountService.UpdateAccountAsync(account, ct);
        return Ok(MapAccount(updated));
    }

    [HttpDelete("accounts/{id}")]
    public async Task<ActionResult> DeleteAccount(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        // TRANSPORT-AWARE: Disconnect using appropriate backend
        if (await UseBaileysAsync(ct))
        {
            _logger.LogInformation("[WhatsApp Admin] Disconnecting via Baileys | AccountId: {AccountId}", id);
            var transport = GetBaileysTransport();
            await transport.DisconnectAsync(tenantId.ToString(), ct);
        }
        else
        {
            _logger.LogInformation("[WhatsApp Admin] Disconnecting via Playwright | AccountId: {AccountId}", id);
            await _webEngine.DisconnectAsync(id, ct);
        }
        
        await _accountService.DeleteAccountAsync(tenantId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Generate QR Code for WhatsApp authentication
    /// TRANSPORT-AWARE: Routes to Baileys or Playwright based on tenant config
    /// </summary>
    [HttpGet("accounts/{id}/qr")]
    public async Task<ActionResult<QrCodeResult>> GenerateQr(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        // TRANSPORT-AWARE: Route to appropriate backend
        if (await UseBaileysAsync(ct))
        {
            _logger.LogInformation("[WhatsApp Admin] Generating QR via Baileys | AccountId: {AccountId}", id);
            var transport = GetBaileysTransport();
            var result = await transport.GetQrCodeAsync(tenantId.ToString(), ct);
            
            return Ok(new QrCodeResult(
                Success: result.Success,
                QrImageBase64: result.QrCodeBase64,
                Error: result.ErrorMessage,
                ExpiresInSeconds: 60,
                AlreadyConnected: result.AlreadyConnected
            ));
        }
        else
        {
            _logger.LogInformation("[WhatsApp Admin] Generating QR via Playwright | AccountId: {AccountId}", id);
            var result = await _webEngine.GenerateQrCodeAsync(id, ct);
            return Ok(result);
        }
    }
    
    [HttpPost("accounts/{id}/disconnect")]
    public async Task<ActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        // TRANSPORT-AWARE: Disconnect using appropriate backend
        if (await UseBaileysAsync(ct))
        {
            _logger.LogInformation("[WhatsApp Admin] Disconnecting via Baileys | AccountId: {AccountId}", id);
            var transport = GetBaileysTransport();
            await transport.DisconnectAsync(tenantId.ToString(), ct);
        }
        else
        {
            _logger.LogInformation("[WhatsApp Admin] Disconnecting via Playwright | AccountId: {AccountId}", id);
            await _webEngine.DisconnectAsync(id, ct);
        }
        
        return Ok();
    }

    /// <summary>
    /// Check account connection status
    /// TRANSPORT-AWARE: Routes to Baileys or Playwright based on tenant config
    /// </summary>
    [HttpGet("accounts/{id}/status")]
    public async Task<ActionResult<ConnectionStatus>> CheckStatus(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        ConnectionStatus status;
        
        // TRANSPORT-AWARE: Check status using appropriate backend
        if (await UseBaileysAsync(ct))
        {
            _logger.LogDebug("[WhatsApp Admin] Checking status via Baileys | AccountId: {AccountId}", id);
            var transport = GetBaileysTransport();
            var result = await transport.GetStatusAsync(tenantId.ToString(), ct);
            var identityMatches = result.Status == WhatsAppConnectionStatus.Connected &&
                                  ConnectedPhoneMatches(account.PhoneNumber, result.ConnectedPhone);
            
            status = new ConnectionStatus(
                IsConnected: identityMatches,
                Status: identityMatches
                    ? WhatsAppAccountStatus.Connected
                    : WhatsAppAccountStatus.Disconnected,
                LastActivity: DateTime.UtcNow
            );
        }
        else
        {
            _logger.LogDebug("[WhatsApp Admin] Checking status via Playwright | AccountId: {AccountId}", id);
            var playwrightStatus = await _webEngine.CheckConnectionAsync(id, ct);
            var identityMatches = playwrightStatus.IsConnected &&
                                  ConnectedPhoneMatches(account.PhoneNumber, playwrightStatus.ConnectedPhone);

            status = identityMatches
                ? playwrightStatus
                : new ConnectionStatus(
                    false,
                    playwrightStatus.Status == WhatsAppAccountStatus.PendingQR
                        ? WhatsAppAccountStatus.PendingQR
                        : WhatsAppAccountStatus.Disconnected,
                    playwrightStatus.LastActivity,
                    playwrightStatus.ConnectedPhone);
        }
        
        // Persist status change to database if different
        if (account.Status != status.Status)
        {
            account.Status = status.Status;
            if (status.IsConnected)
            {
                account.LastConnectedAtUtc = DateTime.UtcNow;
            }
            account.UpdatedAtUtc = DateTime.UtcNow;
            await _accountService.UpdateAccountAsync(account, ct);
        }
        
        return Ok(status);
    }
    
    private static WhatsAppAccountStatus MapBaileysStatusToAccountStatus(WhatsAppConnectionStatus status)
    {
        return status switch
        {
            WhatsAppConnectionStatus.Connected => WhatsAppAccountStatus.Connected,
            WhatsAppConnectionStatus.QrPending => WhatsAppAccountStatus.PendingQR,
            WhatsAppConnectionStatus.Disconnected => WhatsAppAccountStatus.Disconnected,
            WhatsAppConnectionStatus.Unknown => WhatsAppAccountStatus.Disconnected,
            _ => WhatsAppAccountStatus.Disconnected
        };
    }

    [HttpPost("accounts/{id}/enable")]
    public async Task<ActionResult> EnableAccount(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        // Can only enable if Connected
        if (account.Status != Core.Enums.WhatsAppAccountStatus.Connected)
        {
            return BadRequest(new { Error = "Account must be connected before enabling. Please scan QR code first." });
        }
        
        await _accountService.ToggleAccountStatusAsync(tenantId, id, true, ct);
        return Ok(new { Message = "Account enabled successfully" });
    }

    private static bool ConnectedPhoneMatches(string configuredPhone, string? connectedPhone)
    {
        if (string.IsNullOrWhiteSpace(connectedPhone))
        {
            return false;
        }

        static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
        return string.Equals(Digits(configuredPhone), Digits(connectedPhone), StringComparison.Ordinal);
    }

    [HttpPost("accounts/{id}/disable")]
    public async Task<ActionResult> DisableAccount(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        await _accountService.ToggleAccountStatusAsync(tenantId, id, false, ct);
        return Ok(new { Message = "Account disabled successfully" });
    }

    [HttpGet("logs")]
    public async Task<ActionResult<object>> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? messageType = null,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await _logService.GetLogsAsync(
            TenantId,
            page, 
            pageSize, 
            status, 
            messageType, 
            ct);
        
        var summaries = items.Select(log => new WhatsAppMessageLogSummary
        {
            Id = log.Id,
            TargetPhone = log.TargetPhone,
            MessageType = log.MessageType,
            Status = log.Status,
            CreatedAtUtc = log.CreatedAtUtc,
            SentAtUtc = log.SentAtUtc,
            FailureReason = log.FailureReason
        }).ToList();
        
        return Ok(new { 
            Items = summaries, 
            TotalCount = totalCount, 
            Page = page, 
            PageSize = pageSize 
        });
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<WhatsAppAnalytics>> GetAnalytics(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var analytics = await _logService.GetAnalyticsAsync(TenantId, from, to, ct);
        
        return Ok(new WhatsAppAnalytics
        {
            TotalMessagesSent = (int)analytics.TotalMessagesSent,
            TotalMessagesFailed = (int)analytics.TotalMessagesFailed,
            TotalMessagesSkipped = 0,
            SuccessRate = (decimal)analytics.SuccessRate,
            MessagesByType = analytics.MessagesByType.ToDictionary(x => x.Key, x => (int)x.Value)
        });
    }
    [HttpPost("killswitch/activate")]
    public async Task<ActionResult> ActivateKillSwitch([FromBody] KillSwitchRequest request, CancellationToken ct)
    {
        // =====================================================================
        // PHASE: WhatsApp Final Hardening - Real Admin Identity for Audit Trail
        // Extract actual user identity from JWT claims for accountability
        // =====================================================================
        var killedBy = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value 
                    ?? User.FindFirst("sub")?.Value 
                    ?? User.FindFirst("email")?.Value
                    ?? "UnknownAdmin";
        
        await _killSwitch.KillAsync(TenantId, request.Reason, killedBy);
        return Ok(new { Message = "Kill switch activated", ActivatedBy = killedBy });
    }

    [HttpPost("killswitch/deactivate")]
    public async Task<ActionResult> DeactivateKillSwitch(CancellationToken ct)
    {
        // Extract actual user identity from JWT claims for accountability
        var revivedBy = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value 
                     ?? User.FindFirst("sub")?.Value 
                     ?? User.FindFirst("email")?.Value
                     ?? "UnknownAdmin";
        
        await _killSwitch.ReviveAsync(TenantId, revivedBy);
        return Ok(new { Message = "Kill switch deactivated", DeactivatedBy = revivedBy });
    }

    [HttpGet("killswitch/status")]
    public async Task<ActionResult> GetKillSwitchStatus(CancellationToken ct)
    {
        var isKilled = await _killSwitch.IsKilledAsync(TenantId);
        return Ok(new { IsKilled = isKilled });
    }

    // ==================================================================================
    // Template Management Endpoints
    // ==================================================================================

    [HttpGet("templates")]
    public async Task<ActionResult<List<WhatsAppTemplateDto>>> GetTemplates(CancellationToken ct)
    {
        var templates = await _accountService.GetTemplatesAsync(TenantId, ct);
        return Ok(templates.Select(MapTemplate).ToList());
    }

    [HttpGet("templates/{id}")]
    public async Task<ActionResult<WhatsAppTemplateDto>> GetTemplate(Guid id, CancellationToken ct)
    {
        var template = await _accountService.GetTenantTemplateAsync(TenantId, id, ct);
        if (template == null) return NotFound();
        return Ok(MapTemplate(template));
    }

    [HttpPost("templates")]
    public async Task<ActionResult<WhatsAppTemplateDto>> CreateTemplate([FromBody] CreateWhatsAppTemplateDto dto, CancellationToken ct)
    {
        var template = new WhatsAppMessageTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Type = dto.Type,
            Language = dto.Language,
            Content = dto.Content,
            IsActive = dto.IsActive,
            IsDefault = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var created = await _accountService.CreateTemplateAsync(template, ct);
        return CreatedAtAction(nameof(GetTemplate), new { id = created.Id }, MapTemplate(created));
    }

    [HttpPut("templates/{id}")]
    public async Task<ActionResult<WhatsAppTemplateDto>> UpdateTemplate(Guid id, [FromBody] UpdateWhatsAppTemplateDto dto, CancellationToken ct)
    {
        var existing = await _accountService.GetTenantTemplateAsync(TenantId, id, ct);
        if (existing == null) return NotFound();

        existing.Type = dto.Type;
        existing.Language = dto.Language;
        existing.Content = dto.Content;
        existing.IsActive = dto.IsActive;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        var updated = await _accountService.UpdateTemplateAsync(existing, ct);
        return Ok(MapTemplate(updated));
    }

    [HttpDelete("templates/{id}")]
    public async Task<ActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var existing = await _accountService.GetTenantTemplateAsync(tenantId, id, ct);
        if (existing == null) return NotFound();

        await _accountService.DeleteTemplateAsync(tenantId, id, ct);
        return NoContent();
    }

    // ==================================================================================
    // Transport Info Endpoint (for debugging)
    // ==================================================================================
    
    [HttpGet("transport-info")]
    public async Task<ActionResult> GetTransportInfo(CancellationToken ct)
    {
        var useBaileys = await UseBaileysAsync(ct);
        
        return Ok(new
        {
            TenantId,
            Transport = useBaileys ? "Baileys" : "Playwright",
            UseBaileys = useBaileys,
            Message = useBaileys 
                ? "Using Baileys (lightweight Node.js transport)" 
                : "Using Playwright (browser-based transport)"
        });
    }

    private static WhatsAppAccountDto MapAccount(WhatsAppAccount account)
    {
        return new WhatsAppAccountDto
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            PhoneNumber = account.PhoneNumber,
            Status = account.Status.ToString(),
            Enabled = account.Enabled,
            UseForOrderConfirmation = account.UseForOrderConfirmation,
            UseForOtpVerification = account.UseForOtpVerification,
            UseForPaymentApproval = account.UseForPaymentApproval,
            UseForAbandonedCartRecovery = account.UseForAbandonedCartRecovery,
            LastConnectedAtUtc = account.LastConnectedAtUtc,
            LastDisconnectedAtUtc = account.LastDisconnectedAtUtc,
            StatusReason = account.StatusReason,
            CreatedAtUtc = account.CreatedAtUtc,
            UpdatedAtUtc = account.UpdatedAtUtc
        };
    }

    private static WhatsAppTemplateDto MapTemplate(WhatsAppMessageTemplate template)
    {
        return new WhatsAppTemplateDto
        {
            Id = template.Id,
            Type = template.Type,
            Language = template.Language,
            Content = template.Content,
            IsActive = template.IsActive,
            IsDefault = template.IsDefault,
            CreatedAtUtc = template.CreatedAtUtc,
            UpdatedAtUtc = template.UpdatedAtUtc
        };
    }
}

public record KillSwitchRequest(string Reason);

public record CreateWhatsAppAccountDto(
    [property: Required]
    [property: StringLength(100)]
    string DisplayName,
    [property: Required]
    [property: StringLength(32)]
    string PhoneNumber,
    bool UseForOrderConfirmation,
    bool UseForOtpVerification,
    bool UseForPaymentApproval,
    bool UseForAbandonedCartRecovery);

public record UpdateWhatsAppAccountDto(
    [property: Required]
    [property: StringLength(100)]
    string DisplayName,
    [property: Required]
    [property: StringLength(32)]
    string PhoneNumber,
    bool UseForOrderConfirmation,
    bool UseForOtpVerification,
    bool UseForPaymentApproval,
    bool UseForAbandonedCartRecovery);

public record CreateWhatsAppTemplateDto(
    [property: Required]
    [property: StringLength(100)]
    string Type,
    [property: Required]
    [property: StringLength(10)]
    string Language,
    [property: Required]
    [property: StringLength(4000)]
    string Content,
    bool IsActive);

public record UpdateWhatsAppTemplateDto(
    [property: Required]
    [property: StringLength(100)]
    string Type,
    [property: Required]
    [property: StringLength(10)]
    string Language,
    [property: Required]
    [property: StringLength(4000)]
    string Content,
    bool IsActive);

public sealed class WhatsAppAccountDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool UseForOrderConfirmation { get; set; }
    public bool UseForOtpVerification { get; set; }
    public bool UseForPaymentApproval { get; set; }
    public bool UseForAbandonedCartRecovery { get; set; }
    public DateTime? LastConnectedAtUtc { get; set; }
    public DateTime? LastDisconnectedAtUtc { get; set; }
    public string? StatusReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class WhatsAppTemplateDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class WhatsAppMessageLogSummary
{
    public string Id { get; set; } = string.Empty;
    public string TargetPhone { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? FailureReason { get; set; }
}

public class WhatsAppAnalytics
{
    public int TotalMessagesSent { get; set; }
    public int TotalMessagesFailed { get; set; }
    public int TotalMessagesSkipped { get; set; }
    public decimal SuccessRate { get; set; }
    public Dictionary<string, int> MessagesByType { get; set; } = new();
}

