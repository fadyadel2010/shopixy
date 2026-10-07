using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.API.Controllers.Admin;

/// <summary>
/// WhatsApp Admin Controller.
/// Baileys is the sole WhatsApp runtime transport.
/// </summary>
[ApiController]
[Route("api/admin/whatsapp")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class WhatsAppAdminController : ControllerBase
{
    private readonly IWhatsAppAccountService _accountService;
    private readonly IWhatsAppTransport _transport;
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext; 
    private readonly IWhatsAppKillSwitch _killSwitch;
    private readonly IWhatsAppMessageLogService _logService;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly ILogger<WhatsAppAdminController> _logger;
    private Guid TenantId => _tenantContext.RequiredTenantId;

    public WhatsAppAdminController(
        IWhatsAppAccountService accountService,
        [FromKeyedServices("baileys")] IWhatsAppTransport transport,
        ApplicationDbContext dbContext,
        ITenantContext tenantContext,
        IWhatsAppKillSwitch killSwitch,
        IWhatsAppMessageLogService logService,
        IPhoneNumberNormalizer phoneNumberNormalizer,
        ILogger<WhatsAppAdminController> logger)
    {
        _accountService = accountService;
        _transport = transport;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _killSwitch = killSwitch;
        _logService = logService;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _logger = logger;
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
        var normalizedPhone = await TryNormalizeConfiguredPhoneAsync(dto.PhoneNumber, dto.CountryCode, ct);
        if (normalizedPhone == null)
        {
            return BadRequest(new ApiErrorResponse(
                "WHATSAPP_INVALID_PHONE",
                "Enter a valid phone number for the selected or configured country.",
                HttpContext.TraceIdentifier));
        }

        var displayName = dto.DisplayName.Trim();
        var existing = (await _accountService.GetAccountsAsync(TenantId, ct)).FirstOrDefault();
        if (existing != null)
        {
            if (AccountMatches(existing, displayName, normalizedPhone, dto))
            {
                return Ok(MapAccount(existing));
            }

            return Conflict(new ApiErrorResponse(
                "WHATSAPP_ACCOUNT_EXISTS",
                "This store already has a WhatsApp account. Delete it before creating another account.",
                HttpContext.TraceIdentifier));
        }

        var account = new WhatsAppAccount
        {
            TenantId = TenantId,
            DisplayName = displayName,
            PhoneNumber = normalizedPhone,
            UseForOrderConfirmation = dto.UseForOrderConfirmation,
            UseForOtpVerification = dto.UseForOtpVerification,
            UseForPaymentApproval = dto.UseForPaymentApproval,
            Status = WhatsAppAccountStatus.PendingQR,
            Enabled = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        
        try
        {
            var created = await _accountService.CreateAccountAsync(account, ct);
            return CreatedAtAction(nameof(GetAccount), new { id = created.Id }, MapAccount(created));
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("one WhatsApp account", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new ApiErrorResponse(
                "WHATSAPP_ACCOUNT_EXISTS",
                ex.Message,
                HttpContext.TraceIdentifier));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new ApiErrorResponse(
                "WHATSAPP_ACCOUNT_EXISTS",
                "This WhatsApp account already exists for the store.",
                HttpContext.TraceIdentifier));
        }
    }

    [HttpPut("accounts/{id}")]
    public async Task<ActionResult<WhatsAppAccountDto>> UpdateAccount(Guid id, UpdateWhatsAppAccountDto dto, CancellationToken ct)
    {
        var account = await _accountService.GetTenantAccountAsync(TenantId, id, ct);
        if (account == null) return NotFound();

        var normalizedPhone = await TryNormalizeConfiguredPhoneAsync(dto.PhoneNumber, dto.CountryCode, ct);
        if (normalizedPhone == null)
        {
            return BadRequest(new ApiErrorResponse(
                "WHATSAPP_INVALID_PHONE",
                "Enter a valid phone number for the selected or configured country.",
                HttpContext.TraceIdentifier));
        }

        account.DisplayName = dto.DisplayName.Trim();
        account.PhoneNumber = normalizedPhone;
        account.UseForOrderConfirmation = dto.UseForOrderConfirmation;
        account.UseForOtpVerification = dto.UseForOtpVerification;
        account.UseForPaymentApproval = dto.UseForPaymentApproval;
        
        var updated = await _accountService.UpdateAccountAsync(account, ct);
        return Ok(MapAccount(updated));
    }

    [HttpDelete("accounts/{id}")]
    public async Task<ActionResult> DeleteAccount(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        _logger.LogInformation("[WhatsApp Admin] Disconnecting via Baileys | AccountId: {AccountId}", id);
        var disconnected = await _transport.DisconnectAsync(tenantId.ToString(), ct);
        if (!disconnected.Success)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse(
                "WHATSAPP_PROVIDER_UNAVAILABLE",
                "WhatsApp is temporarily unavailable. The account was not deleted so its session remains recoverable.",
                HttpContext.TraceIdentifier));
        }
        
        await _accountService.DeleteAccountAsync(tenantId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Generate QR Code for WhatsApp authentication
    /// QR codes are generated by the Baileys service.
    /// </summary>
    [HttpGet("accounts/{id}/qr")]
    public async Task<ActionResult<QrCodeResult>> GenerateQr(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        _logger.LogInformation("[WhatsApp Admin] Generating QR via Baileys | AccountId: {AccountId}", id);
        var result = await _transport.GetQrCodeAsync(tenantId.ToString(), ct);

        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse(
                "WHATSAPP_PROVIDER_UNAVAILABLE",
                "WhatsApp linking is temporarily unavailable. Please try again.",
                HttpContext.TraceIdentifier));
        }

        if (result.AlreadyConnected)
        {
            var status = await _transport.GetStatusAsync(tenantId.ToString(), ct);
            if (!status.Success || status.Status == WhatsAppConnectionStatus.Unknown)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse(
                    "WHATSAPP_PROVIDER_UNAVAILABLE",
                    "WhatsApp connection status is temporarily unavailable.",
                    HttpContext.TraceIdentifier));
            }

            if (!ConnectedPhoneMatches(account.PhoneNumber, status.ConnectedPhone))
            {
                return Conflict(new ApiErrorResponse(
                    "WHATSAPP_CONNECTED_PHONE_MISMATCH",
                    "The linked WhatsApp session belongs to a different phone number. Disconnect it, then scan a new QR code.",
                    HttpContext.TraceIdentifier));
            }

            account.Status = WhatsAppAccountStatus.Connected;
            account.LastConnectedAtUtc = DateTime.UtcNow;
            account.StatusReason = null;
            await _accountService.UpdateAccountAsync(account, ct);
        }

        return Ok(new QrCodeResult(
            Success: result.Success,
            QrImageBase64: result.QrCodeBase64,
            Error: result.ErrorMessage,
            ExpiresInSeconds: 60,
            AlreadyConnected: result.AlreadyConnected
        ));
    }
    
    [HttpPost("accounts/{id}/disconnect")]
    public async Task<ActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        _logger.LogInformation("[WhatsApp Admin] Disconnecting via Baileys | AccountId: {AccountId}", id);
        var disconnected = await _transport.DisconnectAsync(tenantId.ToString(), ct);
        if (!disconnected.Success)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse(
                "WHATSAPP_PROVIDER_UNAVAILABLE",
                "WhatsApp is temporarily unavailable and the account could not be disconnected.",
                HttpContext.TraceIdentifier));
        }
        
        account.Status = WhatsAppAccountStatus.Disconnected;
        account.Enabled = false;
        account.LastDisconnectedAtUtc = DateTime.UtcNow;
        account.StatusReason = "Disconnected by admin";
        await _accountService.UpdateAccountAsync(account, ct);

        return Ok();
    }

    /// <summary>
    /// Check account connection status
    /// Connection status is read from the Baileys service.
    /// </summary>
    [HttpGet("accounts/{id}/status")]
    public async Task<ActionResult<ConnectionStatus>> CheckStatus(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var account = await _accountService.GetTenantAccountAsync(tenantId, id, ct);
        if (account == null) return NotFound();
        
        var previousStatusReason = account.StatusReason;

        _logger.LogDebug("[WhatsApp Admin] Checking status via Baileys | AccountId: {AccountId}", id);
        var result = await _transport.GetStatusAsync(tenantId.ToString(), ct);
        if (!result.Success || result.Status == WhatsAppConnectionStatus.Unknown)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse(
                "WHATSAPP_PROVIDER_UNAVAILABLE",
                "WhatsApp connection status is temporarily unavailable.",
                HttpContext.TraceIdentifier));
        }
        var identityMatches = result.Status == WhatsAppConnectionStatus.Connected &&
                              ConnectedPhoneMatches(account.PhoneNumber, result.ConnectedPhone);

        var status = new ConnectionStatus(
            IsConnected: identityMatches,
            Status: identityMatches
                ? WhatsAppAccountStatus.Connected
                : result.Status == WhatsAppConnectionStatus.QrPending
                    ? WhatsAppAccountStatus.PendingQR
                    : WhatsAppAccountStatus.Disconnected,
            LastActivity: DateTime.UtcNow,
            ConnectedPhone: result.ConnectedPhone
        );

        if (result.Status == WhatsAppConnectionStatus.Connected && !identityMatches)
        {
            account.StatusReason = "Linked WhatsApp phone does not match the configured phone number";
        }
        else
        {
            account.StatusReason = null;
        }
        
        // Persist status change to database if different
        var statusChanged = account.Status != status.Status;
        var reasonChanged = account.StatusReason != previousStatusReason;
        var mustDisable = status.Status == WhatsAppAccountStatus.Disconnected && account.Enabled;
        if (statusChanged || reasonChanged || mustDisable)
        {
            account.Status = status.Status;
            if (status.IsConnected)
            {
                account.LastConnectedAtUtc = DateTime.UtcNow;
            }
            else if (status.Status == WhatsAppAccountStatus.Disconnected)
            {
                account.Enabled = false;
                account.LastDisconnectedAtUtc = DateTime.UtcNow;
            }
            account.UpdatedAtUtc = DateTime.UtcNow;
            await _accountService.UpdateAccountAsync(account, ct);
        }
        
        return Ok(status);
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
        var result = templates.Select(MapTemplate).ToList();

        foreach (var systemTemplate in WhatsAppSystemTemplates.All)
        {
            var hasOverride = result.Any(template =>
                string.Equals(template.Type, systemTemplate.Type, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(template.Language, systemTemplate.Language, StringComparison.OrdinalIgnoreCase));
            if (!hasOverride)
            {
                result.Add(new WhatsAppTemplateDto
                {
                    Id = systemTemplate.Id,
                    Type = systemTemplate.Type,
                    Language = systemTemplate.Language,
                    Content = systemTemplate.Content,
                    IsActive = true,
                    IsDefault = true,
                    CreatedAtUtc = DateTime.UnixEpoch,
                    UpdatedAtUtc = DateTime.UnixEpoch
                });
            }
        }

        return Ok(result
            .OrderBy(template => template.Type)
            .ThenBy(template => template.Language)
            .ToList());
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

        try
        {
            var created = await _accountService.CreateTemplateAsync(template, ct);
            return CreatedAtAction(nameof(GetTemplate), new { id = created.Id }, MapTemplate(created));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(
                "WHATSAPP_TEMPLATE_INVALID",
                ex.Message,
                HttpContext.TraceIdentifier));
        }
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

        try
        {
            var updated = await _accountService.UpdateTemplateAsync(existing, ct);
            return Ok(MapTemplate(updated));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(
                "WHATSAPP_TEMPLATE_INVALID",
                ex.Message,
                HttpContext.TraceIdentifier));
        }
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
    // Transport Info Endpoint
    // ==================================================================================
    
    [HttpGet("transport-info")]
    public ActionResult GetTransportInfo()
    {
        return Ok(new
        {
            TenantId,
            Transport = "Baileys",
            UseBaileys = true,
            Message = "Using Baileys (authoritative WhatsApp transport)"
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
            LastConnectedAtUtc = account.LastConnectedAtUtc,
            LastDisconnectedAtUtc = account.LastDisconnectedAtUtc,
            StatusReason = account.StatusReason,
            CreatedAtUtc = account.CreatedAtUtc,
            UpdatedAtUtc = account.UpdatedAtUtc
        };
    }

    private async Task<string?> TryNormalizeConfiguredPhoneAsync(
        string input,
        string? countryCode,
        CancellationToken ct)
    {
        try
        {
            return string.IsNullOrWhiteSpace(countryCode)
                ? await _phoneNumberNormalizer.NormalizeAsync(input, TenantId, ct)
                : _phoneNumberNormalizer.Normalize(input, countryCode);
        }
        catch (PhoneNumberNormalizationException ex)
        {
            _logger.LogWarning(
                "[WhatsApp Admin] Invalid account phone | TenantId: {TenantId} | Reason: {Reason}",
                TenantId,
                ex.Reason);
            return null;
        }
    }

    private static bool AccountMatches(
        WhatsAppAccount account,
        string displayName,
        string phoneNumber,
        CreateWhatsAppAccountDto dto) =>
        account.DisplayName == displayName &&
        account.PhoneNumber == phoneNumber &&
        account.UseForOrderConfirmation == dto.UseForOrderConfirmation &&
        account.UseForOtpVerification == dto.UseForOtpVerification &&
        account.UseForPaymentApproval == dto.UseForPaymentApproval;

    private static WhatsAppTemplateDto MapTemplate(WhatsAppMessageTemplate template)
    {
        return new WhatsAppTemplateDto
        {
            Id = template.Id,
            Type = template.Type.Equals("OTP", StringComparison.OrdinalIgnoreCase) ? "Otp" : template.Type,
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

public sealed class CreateWhatsAppAccountDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, StringLength(32, MinimumLength = 7)]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(2, MinimumLength = 2)]
    [RegularExpression(@"^[A-Za-z]{2}$", ErrorMessage = "Country code must use ISO alpha-2 format.")]
    public string? CountryCode { get; set; }

    public bool UseForOrderConfirmation { get; set; }
    public bool UseForOtpVerification { get; set; }
    public bool UseForPaymentApproval { get; set; }
}

public sealed class UpdateWhatsAppAccountDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, StringLength(32, MinimumLength = 7)]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(2, MinimumLength = 2)]
    [RegularExpression(@"^[A-Za-z]{2}$", ErrorMessage = "Country code must use ISO alpha-2 format.")]
    public string? CountryCode { get; set; }

    public bool UseForOrderConfirmation { get; set; }
    public bool UseForOtpVerification { get; set; }
    public bool UseForPaymentApproval { get; set; }
}

public sealed class CreateWhatsAppTemplateDto
{
    [Required, StringLength(100)]
    public string Type { get; set; } = string.Empty;

    [Required, StringLength(10)]
    public string Language { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Content { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class UpdateWhatsAppTemplateDto
{
    [Required, StringLength(100)]
    public string Type { get; set; } = string.Empty;

    [Required, StringLength(10)]
    public string Language { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Content { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

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
