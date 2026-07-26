using System.ComponentModel.DataAnnotations;
using System.Reflection;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Services;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// Phase MR-1C: Architecture guard tests.
/// These tests verify that domain invariants remain structurally enforced.
/// If any of these tests fail, a critical invariant has been silently eroded.
/// 
/// See: /docs/architecture/marketplace-invariants.md
/// </summary>
public class MarketplaceInvariantTests
{
    // ─── Refinement #3: Marketing Evidence Invariant ───

    /// <summary>
    /// Ensures the marketing evidence enforcement method exists on ServiceReviewService.
    /// If this test fails, the invariant location has been moved or removed.
    /// </summary>
    [Fact]
    public void MarketingEvidenceInvariant_MustBeEnforcedInServiceLayer()
    {
        var serviceType = typeof(ServiceReviewService);
        var method = serviceType.GetMethod("CreateReviewAsync");

        Assert.NotNull(method);
    }

    /// <summary>
    /// Ensures the DTO never exposes server-computed fields.
    /// If OrdersCount, RevenueAmount, or EvidenceType appear on the DTO,
    /// a developer has accidentally introduced a client forgery vector.
    /// </summary>
    [Fact]
    public void MarketingEvidenceDto_MustNotExposeServerFields()
    {
        var dtoType = typeof(CreateMarketingEvidenceDto);

        var forbiddenFields = new[] { "OrdersCount", "RevenueAmount", "EvidenceType" };

        foreach (var fieldName in forbiddenFields)
        {
            var property = dtoType.GetProperty(fieldName);
            Assert.Null(property);
        }
    }

    /// <summary>
    /// Ensures MarketingReviewEvidence has no Update method.
    /// If an Update method is added, immutability invariant is broken.
    /// </summary>
    [Fact]
    public void MarketingEvidence_MustBeImmutable()
    {
        var entityType = typeof(MarketingReviewEvidence);
        var updateMethod = entityType.GetMethod("Update", BindingFlags.Public | BindingFlags.Instance);

        Assert.Null(updateMethod);
    }

    /// <summary>
    /// Ensures MarketingReviewEvidence has only static factory methods.
    /// No public constructors should exist.
    /// </summary>
    [Fact]
    public void MarketingEvidence_MustNotHavePublicConstructor()
    {
        var constructors = typeof(MarketingReviewEvidence)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(constructors);
    }

    /// <summary>
    /// Ensures factory methods for evidence creation exist.
    /// </summary>
    [Fact]
    public void MarketingEvidence_MustHaveFactoryMethods()
    {
        var entityType = typeof(MarketingReviewEvidence);

        var createSelfReported = entityType.GetMethod("CreateSelfReported", BindingFlags.Public | BindingFlags.Static);
        var createPosVerified = entityType.GetMethod("CreatePosVerified", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(createSelfReported);
        Assert.NotNull(createPosVerified);
    }

    // ─── Refinement #1: Rating Validation ───

    /// <summary>
    /// Ensures CreateReviewDto.Rating has [Range(1,5)] validation attribute.
    /// Without this, invalid ratings reach the service layer.
    /// </summary>
    [Fact]
    public void CreateReviewDto_Rating_MustHaveRangeAttribute()
    {
        var ratingProperty = typeof(CreateReviewDto).GetProperty("Rating");
        Assert.NotNull(ratingProperty);

        var rangeAttribute = ratingProperty!.GetCustomAttribute<RangeAttribute>();
        Assert.NotNull(rangeAttribute);
        Assert.Equal(1, rangeAttribute!.Minimum);
        Assert.Equal(5, rangeAttribute.Maximum);
    }

    /// <summary>
    /// Ensures CreateReviewDto.Comment has [StringLength] validation attribute.
    /// Without this, arbitrarily large comments reach the database.
    /// </summary>
    [Fact]
    public void CreateReviewDto_Comment_MustHaveStringLengthAttribute()
    {
        var commentProperty = typeof(CreateReviewDto).GetProperty("Comment");
        Assert.NotNull(commentProperty);

        var stringLengthAttribute = commentProperty!.GetCustomAttribute<StringLengthAttribute>();
        Assert.NotNull(stringLengthAttribute);
        Assert.Equal(5000, stringLengthAttribute!.MaximumLength);
    }

    // ─── Trust Aggregate Guard ───

    /// <summary>
    /// Ensures MarketingReviewEvidence inherits from TenantEntity.
    /// Without this, cross-tenant queries break.
    /// </summary>
    [Fact]
    public void MarketingEvidence_MustInheritTenantEntity()
    {
        Assert.True(
            typeof(TenantEntity).IsAssignableFrom(typeof(MarketingReviewEvidence)),
            "MarketingReviewEvidence must inherit from TenantEntity"
        );
    }

    /// <summary>
    /// Ensures ProviderTrustAggregate has the marketing extension method.
    /// </summary>
    [Fact]
    public void ProviderTrustAggregate_MustHaveApplyReviewWithMarketingParams()
    {
        var method = typeof(ProviderTrustAggregate).GetMethod("ApplyReview");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        // Must accept: rating, isMarketingReview, evidenceType, ordersCount, revenueAmount
        Assert.True(parameters.Length >= 5,
            "ApplyReview must accept marketing evidence parameters (rating, isMarketingReview, evidenceType, ordersCount, revenueAmount)");
    }
}
