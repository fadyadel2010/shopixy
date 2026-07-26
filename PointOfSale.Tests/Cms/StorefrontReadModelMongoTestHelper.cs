using Moq;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Cms;

internal static class StorefrontReadModelMongoTestHelper
{
    /// <summary>
    /// Validates that FindOptions are not used or contain only default settings.
    /// Throws NotSupportedException if production code uses query shaping features
    /// that this test helper doesn't support.
    /// </summary>
    private static void ValidateFindOptions<TDocument, TProjection>(FindOptions<TDocument, TProjection>? options)
    {
        if (options == null)
        {
            return;
        }

        if (options.Sort != null)
        {
            throw new NotSupportedException(
                "FindOptions.Sort is not supported in this test helper. " +
                "The test helper returns unordered results. " +
                "Update the test helper to support sorting or restructure the production code.");
        }

        if (options.Skip.HasValue && options.Skip.Value != 0)
        {
            throw new NotSupportedException(
                $"FindOptions.Skip is not supported in this test helper (requested skip: {options.Skip.Value}). " +
                "Update the test helper to support pagination or restructure the production code.");
        }

        if (options.Limit.HasValue)
        {
            throw new NotSupportedException(
                $"FindOptions.Limit is not supported in this test helper (requested limit: {options.Limit.Value}). " +
                "Update the test helper to support limiting or restructure the production code.");
        }

        if (options.Projection != null)
        {
            throw new NotSupportedException(
                "FindOptions.Projection is not supported in this test helper. " +
                "The test helper returns full documents. " +
                "Update the test helper to support projections or restructure the production code.");
        }

        if (options.BatchSize.HasValue)
        {
            throw new NotSupportedException(
                $"FindOptions.BatchSize is not supported in this test helper (requested batch size: {options.BatchSize.Value}). " +
                "Update the test helper if batch size affects test behavior.");
        }

        if (options.Collation != null)
        {
            throw new NotSupportedException(
                "FindOptions.Collation is not supported in this test helper. " +
                "Update the test helper to support collation or restructure the production code.");
        }

        if (options.MaxTime.HasValue)
        {
            throw new NotSupportedException(
                $"FindOptions.MaxTime is not supported in this test helper (requested max time: {options.MaxTime.Value}). " +
                "Update the test helper if max time affects test behavior.");
        }
    }

    public static IMongoDatabase CreateDatabase(ApplicationDbContext dbContext)
    {
        var database = new Mock<IMongoDatabase>();
        var pageCollection = CreatePageCollection(dbContext);
        var footerCollection = CreateFooterCollection(dbContext);

        database
            .Setup(db => db.GetCollection<StorefrontPageDocument>("storefront_pages", It.IsAny<MongoCollectionSettings>()))
            .Returns(pageCollection.Object);

        database
            .Setup(db => db.GetCollection<StorefrontFooterDocument>("storefront_footer", It.IsAny<MongoCollectionSettings>()))
            .Returns(footerCollection.Object);

        return database.Object;
    }

    private static Mock<IMongoCollection<StorefrontPageDocument>> CreatePageCollection(ApplicationDbContext dbContext)
    {
        var collection = new Mock<IMongoCollection<StorefrontPageDocument>>();

        collection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<StorefrontPageDocument>>(),
                It.IsAny<FindOptions<StorefrontPageDocument, StorefrontPageDocument>>(),
                It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<StorefrontPageDocument> filter, FindOptions<StorefrontPageDocument, StorefrontPageDocument>? options, CancellationToken _) =>
            {
                ValidateFindOptions(options);
                var pages = BuildPublishedPages(dbContext);
                var filtered = pages.Where(page => MatchesStorefrontPageFilter(filter, page)).ToList();
                return Task.FromResult<IAsyncCursor<StorefrontPageDocument>>(new TestAsyncCursor<StorefrontPageDocument>(filtered));
            });

        collection
            .Setup(c => c.FindSync(
                It.IsAny<FilterDefinition<StorefrontPageDocument>>(),
                It.IsAny<FindOptions<StorefrontPageDocument, StorefrontPageDocument>>(),
                It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<StorefrontPageDocument> filter, FindOptions<StorefrontPageDocument, StorefrontPageDocument>? options, CancellationToken _) =>
            {
                ValidateFindOptions(options);
                var pages = BuildPublishedPages(dbContext);
                var filtered = pages.Where(page => MatchesStorefrontPageFilter(filter, page)).ToList();
                return new TestAsyncCursor<StorefrontPageDocument>(filtered);
            });

        return collection;
    }

    private static Mock<IMongoCollection<StorefrontFooterDocument>> CreateFooterCollection(ApplicationDbContext dbContext)
    {
        var collection = new Mock<IMongoCollection<StorefrontFooterDocument>>();

        collection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<StorefrontFooterDocument>>(),
                It.IsAny<FindOptions<StorefrontFooterDocument, StorefrontFooterDocument>>(),
                It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<StorefrontFooterDocument> filter, FindOptions<StorefrontFooterDocument, StorefrontFooterDocument>? options, CancellationToken _) =>
            {
                ValidateFindOptions(options);
                var footers = BuildFooterDocuments(dbContext);
                var filtered = footers.Where(footer => MatchesStorefrontFooterFilter(filter, footer)).ToList();
                return Task.FromResult<IAsyncCursor<StorefrontFooterDocument>>(new TestAsyncCursor<StorefrontFooterDocument>(filtered));
            });

        collection
            .Setup(c => c.FindSync(
                It.IsAny<FilterDefinition<StorefrontFooterDocument>>(),
                It.IsAny<FindOptions<StorefrontFooterDocument, StorefrontFooterDocument>>(),
                It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<StorefrontFooterDocument> filter, FindOptions<StorefrontFooterDocument, StorefrontFooterDocument>? options, CancellationToken _) =>
            {
                ValidateFindOptions(options);
                var footers = BuildFooterDocuments(dbContext);
                var filtered = footers.Where(footer => MatchesStorefrontFooterFilter(filter, footer)).ToList();
                return new TestAsyncCursor<StorefrontFooterDocument>(filtered);
            });

        return collection;
    }

    private static List<StorefrontPageDocument> BuildPublishedPages(ApplicationDbContext dbContext)
    {
        return dbContext.CmsPages
            .AsEnumerable()
            .Where(page => !page.IsDeleted && page.IsPublished)
            .GroupBy(page => new { page.TenantId, page.PageGroupId })
            .Select(group => group
                .OrderByDescending(page => page.Version)
                .ThenByDescending(page => page.PublishedAt)
                .First())
            .Select(page => new StorefrontPageDocument
            {
                TenantId = page.TenantId,
                PageGroupId = page.PageGroupId,
                Slug = page.Slug,
                Title = page.Title,
                ContentType = page.ContentType,
                Content = page.Content,
                Seo = new CmsPageSeo
                {
                    MetaTitle = page.MetaTitle ?? string.Empty,
                    MetaDescription = page.MetaDescription ?? string.Empty,
                    MetaKeywords = page.MetaKeywords,
                    OpenGraphImageUrl = page.OpenGraphImageUrl
                },
                IsPublished = true,
                DisplayOrder = 0,
                PublishedAt = page.PublishedAt,
                UpdatedAt = page.UpdatedAt,
                SchemaVersion = page.SchemaVersion
            })
            .ToList();
    }

    private static List<StorefrontFooterDocument> BuildFooterDocuments(ApplicationDbContext dbContext)
    {
        var sections = dbContext.StorefrontFooterSections
            .AsEnumerable()
            .Select(section => new
            {
                section.TenantId,
                Section = section,
                Links = dbContext.StorefrontFooterLinks
                    .Where(link => link.SectionId == section.Id)
                    .AsEnumerable()
                    .OrderBy(link => link.SortOrder)
                    .ThenBy(link => link.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        var socialLinks = dbContext.StorefrontSocialLinks
            .AsEnumerable()
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Platform, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tenantIds = sections.Select(item => item.TenantId)
            .Union(socialLinks.Select(link => link.TenantId))
            .Distinct()
            .ToList();

        return tenantIds
            .Select(tenantId =>
            {
                var scopedSections = sections
                    .Where(item => item.TenantId == tenantId)
                    .Select(item => new StorefrontFooterSectionDocument
                    {
                        Title = item.Section.Title,
                        TitleAr = item.Section.TitleAr,
                        SortOrder = item.Section.SortOrder,
                        IsActive = item.Section.IsActive,
                        UpdatedAt = item.Section.UpdatedAt,
                        Links = item.Links.Select(link => new StorefrontFooterLinkDocument
                        {
                            Label = link.Label,
                            LabelAr = link.LabelAr,
                            Type = link.LinkType,
                            Url = link.Url,
                            CmsPageSlug = link.CmsPageSlug,
                            OpenInNewTab = link.OpenInNewTab,
                            SortOrder = link.SortOrder,
                            IsActive = link.IsActive,
                            UpdatedAt = link.UpdatedAt
                        }).ToList()
                    })
                    .ToList();

                var scopedSocialLinks = socialLinks
                    .Where(link => link.TenantId == tenantId)
                    .Select(link => new StorefrontSocialLinkDocument
                    {
                        Platform = link.Platform,
                        Url = link.Url,
                        Icon = link.Icon,
                        SortOrder = link.SortOrder,
                        IsActive = link.IsActive,
                        UpdatedAt = link.UpdatedAt
                    })
                    .ToList();

                var timestamps = new List<DateTime>();
                timestamps.AddRange(scopedSections.Select(section => section.UpdatedAt));
                timestamps.AddRange(scopedSections.SelectMany(section => section.Links.Select(link => link.UpdatedAt)));
                timestamps.AddRange(scopedSocialLinks.Select(link => link.UpdatedAt));

                return new StorefrontFooterDocument
                {
                    Id = tenantId?.ToString() ?? "global",
                    TenantId = tenantId,
                    UpdatedAt = timestamps.Count == 0 ? DateTime.UtcNow : timestamps.Max(),
                    Sections = scopedSections,
                    SocialLinks = scopedSocialLinks
                };
            })
            .ToList();
    }

    private static bool MatchesStorefrontPageFilter(
        FilterDefinition<StorefrontPageDocument> filter,
        StorefrontPageDocument page)
    {
        var document = RenderFilter(filter);
        return MatchesFilter(document, (field, value) => field switch
        {
            "TenantId" or "tenantId" => value.AsString == page.TenantId.ToString(),
            "IsPublished" or "isPublished" => value.ToBoolean() == page.IsPublished,
            "Slug" or "slug" => value.AsString == page.Slug,
            "PageGroupId" or "pageGroupId" => value.AsString == page.PageGroupId.ToString(),
            _ => false // Unknown fields should fail the match
        });
    }

    private static bool MatchesStorefrontFooterFilter(
        FilterDefinition<StorefrontFooterDocument> filter,
        StorefrontFooterDocument footer)
    {
        var document = RenderFilter(filter);
        return MatchesFilter(document, (field, value) => field switch
        {
            "_id" or "Id" or "id" => value.AsString == footer.Id,
            "TenantId" or "tenantId" => value.IsBsonNull
                ? footer.TenantId == null
                : value.AsString == footer.TenantId?.ToString(),
            _ => false // Unknown fields should fail the match
        });
    }

    private static BsonDocument RenderFilter<TDocument>(FilterDefinition<TDocument> filter)
    {
        var serializer = BsonSerializer.LookupSerializer<TDocument>();
        return filter.Render(new RenderArgs<TDocument>(serializer, BsonSerializer.SerializerRegistry));
    }

    private static bool MatchesFilter(
        BsonDocument document,
        Func<string, BsonValue, bool> predicate)
    {
        foreach (var element in document.Elements)
        {
            if (element.Name == "$and")
            {
                if (element.Value.AsBsonArray.Any(item => !MatchesFilter(item.AsBsonDocument, predicate)))
                {
                    return false;
                }

                continue;
            }

            if (element.Name == "$or")
            {
                return element.Value.AsBsonArray.Any(item => MatchesFilter(item.AsBsonDocument, predicate));
            }

            // Handle BSON operator nodes like { "TenantId": { "$eq": "..." } }
            var valueToMatch = element.Value;
            
            if (element.Value.IsBsonDocument)
            {
                var operatorDoc = element.Value.AsBsonDocument;
                
                // Resolve common MongoDB operators to their underlying values
                if (operatorDoc.Contains("$eq"))
                {
                    valueToMatch = operatorDoc["$eq"];
                }
                else if (operatorDoc.Contains("$ne"))
                {
                    // For $ne, we need to invert the predicate result
                    var neValue = operatorDoc["$ne"];
                    if (predicate(element.Name, neValue))
                    {
                        return false; // If it matches, $ne should fail
                    }
                    continue; // If it doesn't match, $ne passes
                }
                else if (operatorDoc.Contains("$in"))
                {
                    // For $in, check if any value in the array matches
                    var inArray = operatorDoc["$in"].AsBsonArray;
                    var anyMatch = false;
                    foreach (var inValue in inArray)
                    {
                        if (predicate(element.Name, inValue))
                        {
                            anyMatch = true;
                            break;
                        }
                    }
                    if (!anyMatch)
                    {
                        return false;
                    }
                    continue;
                }
                else if (operatorDoc.Contains("$nin"))
                {
                    // For $nin, check that no value in the array matches
                    var ninArray = operatorDoc["$nin"].AsBsonArray;
                    foreach (var ninValue in ninArray)
                    {
                        if (predicate(element.Name, ninValue))
                        {
                            return false; // If any matches, $nin fails
                        }
                    }
                    continue;
                }
                else if (operatorDoc.Contains("$exists"))
                {
                    // $exists requires access to the actual document to check field presence
                    // This test helper cannot reliably evaluate $exists without document context
                    throw new NotSupportedException(
                        $"The $exists operator is not supported in this test helper for field '{element.Name}'. " +
                        "Consider restructuring the test to avoid $exists or implement proper document-aware filtering.");
                }
                else if (operatorDoc.Contains("$gt"))
                {
                    // Comparison operators require type-aware comparison against actual document field values
                    // This test helper cannot reliably evaluate $gt without document context
                    throw new NotSupportedException(
                        $"The $gt operator is not supported in this test helper for field '{element.Name}'. " +
                        "Consider restructuring the test to avoid comparison operators or implement proper type-aware comparison.");
                }
                else if (operatorDoc.Contains("$gte"))
                {
                    throw new NotSupportedException(
                        $"The $gte operator is not supported in this test helper for field '{element.Name}'. " +
                        "Consider restructuring the test to avoid comparison operators or implement proper type-aware comparison.");
                }
                else if (operatorDoc.Contains("$lt"))
                {
                    throw new NotSupportedException(
                        $"The $lt operator is not supported in this test helper for field '{element.Name}'. " +
                        "Consider restructuring the test to avoid comparison operators or implement proper type-aware comparison.");
                }
                else if (operatorDoc.Contains("$lte"))
                {
                    throw new NotSupportedException(
                        $"The $lte operator is not supported in this test helper for field '{element.Name}'. " +
                        "Consider restructuring the test to avoid comparison operators or implement proper type-aware comparison.");
                }
                else
                {
                    // Unknown operator - treat as the document itself
                    valueToMatch = element.Value;
                }
            }

            if (!predicate(element.Name, valueToMatch))
            {
                return false;
            }
        }

        return true;
    }

    private static Mock<IOrderedFindFluent<TDocument, TDocument>> CreateFindFluent<TDocument>(IReadOnlyCollection<TDocument> items)
    {
        var findFluent = new Mock<IOrderedFindFluent<TDocument, TDocument>>();
        findFluent.SetupGet(f => f.Options).Returns(new FindOptions<TDocument, TDocument>());
        findFluent.SetupProperty(f => f.Filter);

        findFluent
            .Setup(f => f.Sort(It.IsAny<SortDefinition<TDocument>>()))
            .Returns(findFluent.Object);

        findFluent
            .Setup(f => f.Limit(It.IsAny<int?>()))
            .Returns(findFluent.Object);

        findFluent
            .Setup(f => f.Skip(It.IsAny<int?>()))
            .Returns(findFluent.Object);

        findFluent
            .Setup(f => f.ToCursorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestAsyncCursor<TDocument>(items));

        findFluent
            .Setup(f => f.ToCursor(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncCursor<TDocument>(items));

        return findFluent;
    }

    private sealed class TestAsyncCursor<TDocument> : IAsyncCursor<TDocument>
    {
        private readonly IReadOnlyCollection<TDocument> _items;
        private bool _moved;

        public TestAsyncCursor(IReadOnlyCollection<TDocument> items)
        {
            _items = items;
        }

        public IEnumerable<TDocument> Current => _moved ? _items : Array.Empty<TDocument>();

        public bool MoveNext(CancellationToken cancellationToken = default)
        {
            if (_moved)
            {
                return false;
            }

            _moved = true;
            return _items.Count > 0;
        }

        public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MoveNext(cancellationToken));

        public void Dispose()
        {
        }
    }
}
