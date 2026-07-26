namespace PointOfSale.Infrastructure.WhatsApp.Interfaces;

public interface IWhatsAppCircuitBreaker
{
    Task<bool> IsOpenAsync(Guid accountId);
    Task RecordSuccessAsync(Guid accountId);
    Task RecordFailureAsync(Guid accountId);
}
