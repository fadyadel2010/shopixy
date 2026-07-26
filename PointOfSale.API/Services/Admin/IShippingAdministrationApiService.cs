using PointOfSale.API.DTOs.ShippingAdministration;
using PointOfSale.Core.Interfaces.ShippingAdministration;

namespace PointOfSale.API.Services.Admin;

public interface IShippingAdministrationApiService
{
    Task<IReadOnlyList<SupportedShippingCarrierDto>> ListSupportedCarriersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CarrierAccountAdministrationDto>> ListCarrierAccountsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<CarrierAccountAdministrationDto?> GetCarrierAccountAsync(Guid tenantId, Guid carrierAccountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CarrierAdministrationAuditDto>> GetAuditAsync(Guid tenantId, Guid? carrierAccountId, int limit, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> CreateCarrierAccountAsync(Guid tenantId, CreateCarrierAccountCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> UpdateCarrierAccountAsync(Guid tenantId, Guid carrierAccountId, UpdateCarrierAccountCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> UpdateCredentialReferenceAsync(Guid tenantId, Guid carrierAccountId, CredentialReferenceCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> SelectEnvironmentAsync(Guid tenantId, Guid carrierAccountId, EnvironmentCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> AssignWarehouseAsync(Guid tenantId, Guid carrierAccountId, WarehouseAssignmentCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> SetPolicyAsync(Guid tenantId, Guid carrierAccountId, string scope, PolicyCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> SetServiceLevelAsync(Guid tenantId, Guid carrierAccountId, string serviceLevelCode, ServiceLevelCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> RunConnectionTestAsync(Guid tenantId, Guid carrierAccountId, ConnectionTestCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> ActivateAsync(Guid tenantId, Guid carrierAccountId, ReadinessEvidenceCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> SuspendAsync(Guid tenantId, Guid carrierAccountId, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> DisableAsync(Guid tenantId, Guid carrierAccountId, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> RetireAsync(Guid tenantId, Guid carrierAccountId, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
    Task<ShippingAdministrationResultDto> EvaluateHealthAsync(Guid tenantId, Guid carrierAccountId, ReadinessEvidenceCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken);
}
