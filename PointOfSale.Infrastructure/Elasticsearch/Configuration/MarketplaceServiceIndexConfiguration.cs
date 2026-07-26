using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Configuration;

/// <summary>
/// Elasticsearch index configuration for marketplace services.
/// Defines mappings, analyzers, and index settings.
/// </summary>
public static class MarketplaceServiceIndexConfiguration
{
    public const string IndexName = "marketplace_services";

    public static CreateIndexDescriptor GetIndexDescriptor()
    {
        return new CreateIndexDescriptor(IndexName)
            .Settings(s => s
                .NumberOfShards(3)
                .NumberOfReplicas(1)
                .Analysis(a => a
                    .Analyzers(an => an
                        .Standard("standard", sa => sa
                            .StopWords("_english_")
                        )
                    )
                )
            )
            .Map<MarketplaceServiceDocument>(m => m
                .AutoMap()
                .Properties(p => p
                    .Keyword(k => k.Name(n => n.ServiceId))
                    .Keyword(k => k.Name(n => n.TenantId))
                    .Text(t => t
                        .Name(n => n.Title)
                        .Analyzer("standard")
                        .Boost(4.0)
                    )
                    .Text(t => t
                        .Name(n => n.Description)
                        .Analyzer("standard")
                        .Boost(2.0)
                    )
                    .Keyword(k => k.Name(n => n.CategoryId))
                    .Text(t => t
                        .Name(n => n.CategoryPath)
                        .Analyzer("standard")
                        .Boost(2.0)
                    )
                    .Keyword(k => k.Name(n => n.ProviderProfileId))
                    .Text(t => t
                        .Name(n => n.ProviderDisplayName)
                        .Analyzer("standard")
                    )
                    .Object<MarketplaceServiceDocument.TrustMetrics>(n => n
                        .Name(nn => nn.Trust)
                        .Properties(tp => tp
                            .Number(nu => nu
                                .Name(tn => tn.AverageRating)
                                .Type(NumberType.Double)
                            )
                            .Number(nu => nu
                                .Name(tn => tn.TotalReviews)
                                .Type(NumberType.Integer)
                            )
                            .Keyword(k => k.Name(tn => tn.TrustBadge))
                        )
                    )
                    .Object<MarketplaceServiceDocument.ServiceStatistics>(n => n
                        .Name(nn => nn.ServiceStats)
                        .Properties(sp => sp
                            .Number(nu => nu
                                .Name(sn => sn.TotalRequests)
                                .Type(NumberType.Integer)
                            )
                            .Number(nu => nu
                                .Name(sn => sn.CompletedRequests)
                                .Type(NumberType.Integer)
                            )
                            .Number(nu => nu
                                .Name(sn => sn.CompletionRate)
                                .Type(NumberType.Double)
                            )
                        )
                    )
                    .Date(d => d.Name(n => n.CreatedAt))
                    .Boolean(b => b.Name(n => n.IsActive))
                )
            );
    }
}
