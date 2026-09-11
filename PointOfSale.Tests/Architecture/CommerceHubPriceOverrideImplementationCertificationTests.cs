using PointOfSale.Core.Entities;
using PointOfSale.Core.Pricing;

namespace PointOfSale.Tests.Architecture;

[Collection("CommerceHubPriceOverrideRuntimePolicy")]
public sealed class CommerceHubPriceOverrideImplementationCertificationTests
{
    [Fact]
    public async Task Resolver_UsesAllowlistedMerchantOverride_AndKillSwitchFallsBackWithoutDeletingRecord()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new CommerceHubPriceAuthority();

        try
        {
            CommerceHubPriceOverrideRuntimePolicy.Configure(new CommerceHubPriceOverrideRolloutOptions
            {
                Enabled = true,
                TenantAllowlist = new List<Guid> { tenantId }
            });

            var active = await resolver.ResolveAsync(tenantId, 80m, 50m);
            Assert.Equal(50m, active.FinalPrice);
            Assert.Equal(50m, active.OverridePrice);
            Assert.Equal(CommerceHubPriceSource.CommerceHubOverride, active.PriceSource);

            CommerceHubPriceOverrideRuntimePolicy.Configure(new CommerceHubPriceOverrideRolloutOptions());
            var rolledBack = await resolver.ResolveAsync(tenantId, 80m, 50m);
            Assert.Equal(80m, rolledBack.FinalPrice);
            Assert.Null(rolledBack.OverridePrice);
            Assert.Equal(CommerceHubPriceSource.ProductVariantEffectiveOnlinePrice, rolledBack.PriceSource);
        }
        finally
        {
            CommerceHubPriceOverrideRuntimePolicy.Configure(new CommerceHubPriceOverrideRolloutOptions());
        }
    }

    [Fact]
    public void Listing_PersistsSaveUpdateAndRemove_WithActorMetadataAndStaleReplayFence()
    {
        var actorId = Guid.NewGuid();
        var listing = CommerceHubListing.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            50m,
            actorUserId: actorId,
            overrideReason: "initial price");

        Assert.Equal(50m, listing.CommerceHubPrice);
        Assert.Equal(1, listing.CommerceHubPriceOverrideMutationVersion);
        Assert.Equal(actorId, listing.CommerceHubPriceOverrideUpdatedBy);

        Assert.True(listing.Update(
            listing.CommerceHubCategoryId,
            45m,
            actorUserId: actorId,
            overrideReason: "campaign",
            expectedPriceOverrideMutationVersion: 1));
        Assert.Equal(45m, listing.CommerceHubPrice);
        Assert.Equal(2, listing.CommerceHubPriceOverrideMutationVersion);

        Assert.Throws<InvalidOperationException>(() => listing.Update(
            listing.CommerceHubCategoryId,
            null,
            actorUserId: actorId,
            expectedPriceOverrideMutationVersion: 1));

        Assert.True(listing.Update(
            listing.CommerceHubCategoryId,
            null,
            actorUserId: actorId,
            overrideReason: "restore automatic price",
            expectedPriceOverrideMutationVersion: 2));
        Assert.Null(listing.CommerceHubPrice);
        Assert.Equal(3, listing.CommerceHubPriceOverrideMutationVersion);
    }

    [Fact]
    public void RuntimePaths_MustUseSingleResolverAndImmutableOverrideEvents()
    {
        var events = ReadRepoFile("PointOfSale.Core/Events/CommerceHubEvents.cs");
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var settings = ReadRepoFile("PointOfSale.API/appsettings.json");

        Assert.Contains("CommerceHubPriceOverrideSavedEvent", events, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideUpdatedEvent", events, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideRemovedEvent", events, StringComparison.Ordinal);
        Assert.Contains("PreviousValue", events, StringComparison.Ordinal);
        Assert.Contains("NewValue", events, StringComparison.Ordinal);
        Assert.Contains("CorrelationId", handlers, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", cart, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", checkout, StringComparison.Ordinal);
        Assert.Contains("listing.CommerceHubPrice", overlay, StringComparison.Ordinal);
        Assert.Contains("priceResolution.OverridePrice", projection, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": false", settings, StringComparison.Ordinal);
        Assert.Contains("\"TenantAllowlist\": []", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceBaseline_MustKeepIntentionalOverrideNullability()
    {
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");
        var configuration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("CommerceHubPrice = table.Column<decimal>", migration, StringComparison.Ordinal);
        Assert.Contains("nullable: true", migration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubListings_CommerceHubPrice_Positive", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull", migration, StringComparison.Ordinal);
        Assert.Contains("\\\"CommerceHubPrice\\\" IS NULL OR \\\"CommerceHubPrice\\\" > 0", configuration, StringComparison.Ordinal);
        Assert.Contains("CommerceHub price overrides require merchant actor and timestamp audit metadata.", dbContext, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return File.ReadAllText(Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}

[CollectionDefinition("CommerceHubPriceOverrideRuntimePolicy", DisableParallelization = true)]
public sealed class CommerceHubPriceOverrideRuntimePolicyCollection;
