using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.WhatsApp;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

public class WhatsAppFeatureGateService : IWhatsAppFeatureGateService
{
    private readonly IWhatsAppAccountService _accountService;
    private readonly ILogger<WhatsAppFeatureGateService> _logger;

    public WhatsAppFeatureGateService(
        IWhatsAppAccountService accountService,
        ILogger<WhatsAppFeatureGateService> logger)
    {
        _accountService = accountService;
        _logger = logger;
    }

    public async Task<bool> IsOrderConfirmationEnabledAsync(Guid tenantId, CancellationToken ct = default)
    {
        var account = await _accountService.GetActiveAccountForUseCaseAsync(
            tenantId, 
            WhatsAppUseCase.OrderConfirmation, 
            ct);
            
        return account != null;
    }

    public async Task<bool> IsOtpVerificationEnabledAsync(Guid tenantId, CancellationToken ct = default)
    {
        var account = await _accountService.GetActiveAccountForUseCaseAsync(
            tenantId, 
            WhatsAppUseCase.OtpVerification, 
            ct);
            
        return account != null;
    }

    public async Task<bool> IsPaymentApprovalEnabledAsync(Guid tenantId, CancellationToken ct = default)
    {
        var account = await _accountService.GetActiveAccountForUseCaseAsync(
            tenantId, 
            WhatsAppUseCase.PaymentApproval, 
            ct);
            
        return account != null;
    }

    public async Task<WhatsAppAccount?> GetActiveAccountAsync(
        Guid tenantId, 
        WhatsAppUseCase useCase, 
        CancellationToken ct = default)
    {
        return await _accountService.GetActiveAccountForUseCaseAsync(tenantId, useCase, ct);
    }

    public async Task<bool> IsAbandonedCartRecoveryEnabledAsync(Guid tenantId, CancellationToken ct = default)
    {
        var account = await _accountService.GetActiveAccountForUseCaseAsync(
            tenantId, 
            WhatsAppUseCase.AbandonedCartRecovery, 
            ct);
            
        return account != null;
    }
}
