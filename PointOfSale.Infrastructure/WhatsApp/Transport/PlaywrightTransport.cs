using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Transport;

/// <summary>
/// Playwright-based transport implementation.
/// Wraps the existing PlaywrightWhatsAppEngine to implement the IWhatsAppTransport interface.
/// This enables gradual migration to Baileys while preserving all existing behavior.
/// </summary>
public class PlaywrightTransport : IWhatsAppTransport
{
    private readonly IWhatsAppWebEngine _engine;
    private readonly IWhatsAppAccountService _accountService;
    private readonly ILogger<PlaywrightTransport> _logger;

    public PlaywrightTransport(
        IWhatsAppWebEngine engine,
        IWhatsAppAccountService accountService,
        ILogger<PlaywrightTransport> logger)
    {
        _engine = engine;
        _accountService = accountService;
        _logger = logger;
    }

    public async Task<WhatsAppSendResult> SendMessageAsync(
        string tenantId,
        string phoneE164,
        string message,
        WhatsAppMessageType messageType,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        try
        {
            var account = await GetAccountForTenantAsync(tenantId, accountId, cancellationToken);
            if (account == null)
            {
                _logger.LogWarning(
                    "[PlaywrightTransport] No authorized active WhatsApp account found | Tenant: {TenantId} | Account: {AccountId}",
                    tenantId, accountId);
                 
                return new WhatsAppSendResult
                {
                    Success = false,
                    ErrorMessage = "Authorized WhatsApp account is not active"
                };
            }

            var connection = await _engine.CheckConnectionAsync(account.Id, cancellationToken);
            if (!connection.IsConnected || !ConnectedPhoneMatches(account.PhoneNumber, connection.ConnectedPhone))
            {
                _logger.LogWarning(
                    "[PlaywrightTransport] Connected phone attestation failed | Tenant: {TenantId} | Account: {AccountId}",
                    tenantId, account.Id);

                return new WhatsAppSendResult
                {
                    Success = false,
                    ErrorMessage = "Connected WhatsApp phone does not match authorized account"
                };
            }

            _logger.LogDebug(
                "[PlaywrightTransport] Sending message | Tenant: {TenantId} | Account: {AccountId} | Phone: {Phone}",
                tenantId, account.Id, MaskPhone(phoneE164));

            // Call existing engine
            var result = await _engine.SendMessageAsync(
                account.Id,
                phoneE164,
                message,
                cancellationToken);

            return new WhatsAppSendResult
            {
                Success = result.Success,
                MessageId = result.MessageId,
                ErrorMessage = result.Error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PlaywrightTransport] Exception sending message for tenant {TenantId}",
                tenantId);
            
            return new WhatsAppSendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppQrResult> GetQrCodeAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await GetAccountForTenantAsync(tenantId, null, cancellationToken);
            if (account == null)
            {
                return new WhatsAppQrResult
                {
                    Success = false,
                    ErrorMessage = "No WhatsApp account configured"
                };
            }

            _logger.LogDebug(
                "[PlaywrightTransport] Generating QR code | Tenant: {TenantId} | Account: {AccountId}",
                tenantId, account.Id);

            var result = await _engine.GenerateQrCodeAsync(account.Id, cancellationToken);

            return new WhatsAppQrResult
            {
                Success = result.Success,
                QrCodeBase64 = result.QrImageBase64,
                ErrorMessage = result.Error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PlaywrightTransport] Exception generating QR code for tenant {TenantId}",
                tenantId);
            
            return new WhatsAppQrResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppStatusResult> GetStatusAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await GetAccountForTenantAsync(tenantId, null, cancellationToken);
            if (account == null)
            {
                return new WhatsAppStatusResult
                {
                    Success = false,
                    Status = WhatsAppConnectionStatus.Unknown,
                    ErrorMessage = "No WhatsApp account configured"
                };
            }

            var result = await _engine.CheckConnectionAsync(account.Id, cancellationToken);

            return new WhatsAppStatusResult
            {
                Success = true,
                Status = MapConnectionStatus(result.Status),
                ErrorMessage = null,
                ConnectedPhone = result.ConnectedPhone
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PlaywrightTransport] Exception checking status for tenant {TenantId}",
                tenantId);
            
            return new WhatsAppStatusResult
            {
                Success = false,
                Status = WhatsAppConnectionStatus.Unknown,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppDisconnectResult> DisconnectAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await GetAccountForTenantAsync(tenantId, null, cancellationToken);
            if (account == null)
            {
                return new WhatsAppDisconnectResult
                {
                    Success = false,
                    ErrorMessage = "No WhatsApp account configured"
                };
            }

            _logger.LogInformation(
                "[PlaywrightTransport] Disconnecting | Tenant: {TenantId} | Account: {AccountId}",
                tenantId, account.Id);

            await _engine.DisconnectAsync(account.Id, cancellationToken);

            return new WhatsAppDisconnectResult
            {
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PlaywrightTransport] Exception disconnecting for tenant {TenantId}",
                tenantId);
            
            return new WhatsAppDisconnectResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Gets the active WhatsApp account for a tenant.
    /// Uses OrderConfirmation use case as default (most common).
    /// </summary>
    private async Task<Core.Entities.WhatsAppAccount?> GetAccountForTenantAsync(
        string tenantId,
        string? accountId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            _logger.LogWarning("[PlaywrightTransport] Invalid tenant ID format: {TenantId}", tenantId);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            if (!Guid.TryParse(accountId, out var accountGuid))
            {
                _logger.LogWarning("[PlaywrightTransport] Invalid account ID format: {AccountId}", accountId);
                return null;
            }

            var authorizedAccount = await _accountService.GetTenantAccountAsync(
                tenantGuid,
                accountGuid,
                cancellationToken);

            return authorizedAccount is
                {
                    Enabled: true,
                    Status: WhatsAppAccountStatus.Connected
                }
                ? authorizedAccount
                : null;
        }

        // Get active account for OrderConfirmation (default use case)
        // This preserves existing behavior where dispatcher uses the primary account
        return await _accountService.GetActiveAccountForUseCaseAsync(
            tenantGuid,
            WhatsAppUseCase.OrderConfirmation,
            cancellationToken);
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

    /// <summary>
    /// Maps WhatsAppAccountStatus to WhatsAppConnectionStatus.
    /// </summary>
    private static WhatsAppConnectionStatus MapConnectionStatus(WhatsAppAccountStatus status)
    {
        return status switch
        {
            WhatsAppAccountStatus.Connected => WhatsAppConnectionStatus.Connected,
            WhatsAppAccountStatus.Disconnected => WhatsAppConnectionStatus.Disconnected,
            WhatsAppAccountStatus.PendingQR => WhatsAppConnectionStatus.QrPending,
            _ => WhatsAppConnectionStatus.Unknown
        };
    }

    /// <summary>
    /// Masks phone number for logging (privacy).
    /// </summary>
    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 6)
            return "****";

        var prefix = phone.Substring(0, 4);
        var suffix = phone.Substring(phone.Length - 3);
        var masked = new string('*', Math.Max(0, phone.Length - 7));
        return $"{prefix}{masked}{suffix}";
    }
}
