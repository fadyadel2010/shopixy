namespace PointOfSale.Infrastructure.WhatsApp.Interfaces;

public interface IWhatsAppKillSwitch
{
    Task<bool> IsKilledAsync(Guid tenantId);
    Task KillAsync(Guid tenantId, string reason, string killedBy);
    Task ReviveAsync(Guid tenantId, string revivedBy);
}
