using MassTransit;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

public sealed class SyntheticQaCollectionAffiliateConsumer(ApplicationDbContext db,
    SyntheticQaCertificationAuthority authority, IAffiliateService affiliate) : IConsumer<SyntheticQaCollectionRecordedEvent>
{
    public async Task Consume(ConsumeContext<SyntheticQaCollectionRecordedEvent> context)
    {
        var fact = context.Message;
        var order = await db.Orders.IgnoreQueryFilters().SingleAsync(x => x.Id == fact.OrderId && x.TenantId == fact.TenantId && !x.IsDeleted, context.CancellationToken);
        var evidence = await authority.RequireEvidenceAsync(order, fact.EvidenceId, "Collection", context.CancellationToken);
        if (evidence.RunId != fact.RunId || evidence.Amount != fact.Amount || evidence.Currency != fact.Currency)
            throw new InvalidOperationException("SYNTHETIC_QA_EVENT_CONFLICT");
        await affiliate.RecordCommissionForOrderAsync(order.Id, context.CancellationToken);
    }
}
