using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Tests.Infrastructure;

public sealed class DerivativeProjectionMutationGuardTests
{
    [Fact]
    public void RequireReviewedMutation_AcceptsReviewedSqlFreshFailClosedPrimitive()
    {
        var request = ValidRequest();

        var accepted = DerivativeProjectionMutationGuard.RequireReviewedMutation(request);

        Assert.Equal(request, accepted);
    }

    [Fact]
    public void RequireReviewedMutation_RejectsFullReplacementWithoutSqlAuthority()
    {
        var request = ValidRequest() with { HasSqlAuthority = false };

        var ex = Assert.Throws<InvalidOperationException>(
            () => DerivativeProjectionMutationGuard.RequireReviewedMutation(request));

        Assert.Equal("DERIVATIVE_MUTATION_SQL_AUTHORITY_REQUIRED", ex.Message);
    }

    [Fact]
    public void RequireReviewedMutation_RejectsMutationWithoutFreshnessGuard()
    {
        var request = ValidRequest() with { HasFreshnessGuard = false };

        var ex = Assert.Throws<InvalidOperationException>(
            () => DerivativeProjectionMutationGuard.RequireReviewedMutation(request));

        Assert.Equal("DERIVATIVE_MUTATION_FRESHNESS_GUARD_REQUIRED", ex.Message);
    }

    [Fact]
    public void RequireReviewedMutation_RejectsMutationThatDoesNotFailClosed()
    {
        var request = ValidRequest() with { FailsClosedWhenAuthorityMissing = false };

        var ex = Assert.Throws<InvalidOperationException>(
            () => DerivativeProjectionMutationGuard.RequireReviewedMutation(request));

        Assert.Equal("DERIVATIVE_MUTATION_FAIL_CLOSED_REQUIRED", ex.Message);
    }

    private static ReviewedDerivativeProjectionMutation ValidRequest() =>
        new(
            DangerousProjectionPrimitive.MongoFullDocumentReplace,
            "ReviewedProjectionConsumer",
            "ReviewedDocument",
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            7,
            SqlAuthorityOverlayKind.StorefrontFooter,
            HasSqlAuthority: true,
            HasFreshnessGuard: true,
            FailsClosedWhenAuthorityMissing: true,
            "Reviewed SQL recomposition boundary before dangerous primitive.");
}
