using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces.WhatsApp;

public interface IWhatsAppAccountService
{
    Task<WhatsAppAccount?> GetTenantAccountAsync(Guid tenantId, Guid accountId, CancellationToken ct = default);
    Task<List<WhatsAppAccount>> GetAccountsAsync(Guid tenantId, CancellationToken ct = default);
    Task<WhatsAppAccount> CreateAccountAsync(WhatsAppAccount account, CancellationToken ct = default);
    Task<WhatsAppAccount> UpdateAccountAsync(WhatsAppAccount account, CancellationToken ct = default);
    Task DeleteAccountAsync(Guid tenantId, Guid accountId, CancellationToken ct = default);
    
    /// <summary>
    /// Enables or disables an account.
    /// </summary>
    Task ToggleAccountStatusAsync(Guid tenantId, Guid accountId, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Gets the active account for a specific use case.
    /// Returns null if no account is configured or enabled for the use case.
    /// </summary>
    Task<WhatsAppAccount?> GetActiveAccountForUseCaseAsync(Guid tenantId, PointOfSale.Core.Enums.WhatsAppUseCase useCase, CancellationToken ct = default);

    // ==================================================================================
    // Template Management
    // ==================================================================================
    
    Task<List<WhatsAppMessageTemplate>> GetTemplatesAsync(Guid tenantId, CancellationToken ct = default);
    Task<WhatsAppMessageTemplate?> GetTenantTemplateAsync(Guid tenantId, Guid templateId, CancellationToken ct = default);
    Task<WhatsAppMessageTemplate> CreateTemplateAsync(WhatsAppMessageTemplate template, CancellationToken ct = default);
    Task<WhatsAppMessageTemplate> UpdateTemplateAsync(WhatsAppMessageTemplate template, CancellationToken ct = default);
    Task DeleteTemplateAsync(Guid tenantId, Guid templateId, CancellationToken ct = default);
}
