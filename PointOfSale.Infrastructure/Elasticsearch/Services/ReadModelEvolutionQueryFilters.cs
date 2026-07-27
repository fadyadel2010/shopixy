using Nest;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

public static class ReadModelEvolutionQueryFilters
{
    public static QueryContainer RequiredBoolean<TDocument>(
        QueryContainerDescriptor<TDocument> query,
        string fieldName,
        bool requiredValue,
        ReadModelCompatibilityDecision compatibility)
        where TDocument : class
    {
        return RequiredValue(query, fieldName, requiredValue, compatibility);
    }

    public static QueryContainer RequiredValue<TDocument>(
        QueryContainerDescriptor<TDocument> query,
        string fieldName,
        object requiredValue,
        ReadModelCompatibilityDecision compatibility)
        where TDocument : class
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        }

        ArgumentNullException.ThrowIfNull(compatibility);

        if (compatibility.StrictFilteringEnabled)
        {
            return query.Term(term => term.Field(fieldName).Value(requiredValue));
        }

        return query.Bool(boolean => boolean
            .Should(
                should => should.Term(term => term.Field(fieldName).Value(requiredValue)),
                should => should.Bool(inner => inner.MustNot(mustNot => mustNot.Exists(exists => exists.Field(fieldName)))))
            .MinimumShouldMatch(1));
    }

    public static QueryContainer RequiredExists<TDocument>(
        QueryContainerDescriptor<TDocument> query,
        string fieldName,
        ReadModelCompatibilityDecision compatibility)
        where TDocument : class
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        }

        ArgumentNullException.ThrowIfNull(compatibility);

        return compatibility.StrictFilteringEnabled
            ? query.Exists(exists => exists.Field(fieldName))
            : query.MatchAll();
    }
}
