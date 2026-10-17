using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Accounting;

namespace PointOfSale.Infrastructure.Payments;

/// <summary>Tenant-scoped façade. Financial mutation belongs to the canonical payment lifecycle.</summary>
public sealed class CarrierRemittanceService(IPaymentLifecycleService lifecycle, ITenantContext tenantContext) : ICarrierRemittanceService
{
    private void Scope(Guid tenant) => CarrierRemittanceAuthority.Require(tenant != Guid.Empty && tenantContext.TenantId == tenant, "REMITTANCE_TENANT_REQUIRED");

    public Task<CarrierRemittanceSelection> SelectAsync(Guid tenantId, Guid actorId, int page = 1, CancellationToken ct = default)
    {
        Scope(tenantId);
        return lifecycle.SelectCarrierRemittanceAsync(tenantId, actorId, page, ct);
    }

    public Task<CarrierRemittanceResult?> GetAsync(Guid tenantId, Guid actorId, Guid operationId, CancellationToken ct = default)
    {
        Scope(tenantId);
        return lifecycle.GetCarrierRemittanceAsync(tenantId, actorId, operationId, ct);
    }

    public Task<CarrierRemittanceResult> ReceiveAsync(Guid tenantId, Guid actorId, CarrierRemittanceRequest request, CancellationToken ct = default)
    {
        Scope(tenantId);
        return lifecycle.RecordCarrierRemittanceAsync(tenantId, actorId, request, ct);
    }
}
