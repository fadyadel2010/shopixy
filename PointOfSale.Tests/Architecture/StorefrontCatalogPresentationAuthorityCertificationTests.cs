namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCatalogPresentationAuthorityCertificationTests
{
    [Fact]
    public void ProductPresentationCachesMustReenterCurrentSqlAuthorityBeforeReturning()
    {
        var productService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");

        Assert.Contains("RecomposeCachedProductDetailFromSqlAuthorityAsync", productService, StringComparison.Ordinal);
        Assert.Contains("RecomposeCachedProductListFromSqlAuthorityAsync", productService, StringComparison.Ordinal);
        Assert.Contains("ApplyCurrentSqlProductDetailAuthorityAsync", productService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productService, StringComparison.Ordinal);
        Assert.Contains("expectedSlug", productService, StringComparison.Ordinal);
        Assert.Contains("affiliateModeOnly && !current.IsAffiliateEnabled", productService, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductIdentitySlugReadsMustRejectStaleSearchOrCacheIdentityAfterSqlRecomposition()
    {
        var productService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var publicAffiliateProductsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PublicAffiliateProductsController.cs");

        Assert.Contains("expectedSlug", productService, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(current.Slug, expectedSlug, StringComparison.OrdinalIgnoreCase)", productService, StringComparison.Ordinal);

        Assert.Contains("RecomposePublicAffiliateProductFromSqlAuthorityAsync", publicAffiliateProductsController, StringComparison.Ordinal);
        Assert.Contains("IsCurrentCanonicalProductSlug(slug, document.ProductSlug)", publicAffiliateProductsController, StringComparison.Ordinal);
        Assert.Contains("Public affiliate product stale slug suppressed by SQL authority", publicAffiliateProductsController, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", publicAffiliateProductsController, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductDetailFieldsMustBeBackendComposedAndFrontendPresentationOnly()
    {
        var storefrontProductService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var storefrontProductPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/ProductDetailsPage.tsx");
        var sectionRenderer = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx");
        var runtimeSectionContainer = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx");
        var productHero = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx");
        var productSpecs = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/defaults/product-specs.tsx");
        var productVideo = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/defaults/product-video.tsx");

        Assert.Contains("ApplyCurrentSqlProductDetailAuthorityAsync", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("Description = document.Description", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("CategoryName = document.CategoryName", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("Media = media", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("Variants = variants", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("HasVariants = document.HasVariants", storefrontProductService, StringComparison.Ordinal);

        Assert.Contains("fetchProductBySlug(slug)", storefrontProductPage, StringComparison.Ordinal);
        Assert.Contains("sectionProps={{ product }}", storefrontProductPage, StringComparison.Ordinal);
        Assert.Contains("<RuntimeSectionContainer", sectionRenderer, StringComparison.Ordinal);
        Assert.Contains("sectionProps={{ ...section.data, sectionId: section.id, ...sectionProps }}", sectionRenderer, StringComparison.Ordinal);

        Assert.Contains("parseProductNarrative(localizedDescription || product?.description)", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("productHeroModel?: ProductHeroPresentationModel", productHero, StringComparison.Ordinal);
        Assert.Contains("model.summaryParagraphs", productHero, StringComparison.Ordinal);
        Assert.DoesNotContain("Discover something extraordinary", productHero, StringComparison.Ordinal);
        Assert.DoesNotContain("No description available", productHero, StringComparison.Ordinal);

        Assert.Contains("parseProductNarrative(localize(product, 'description') || product.description)", productSpecs, StringComparison.Ordinal);
        Assert.DoesNotContain("items?: Array<{ label?: string; value?: string }>", productSpecs, StringComparison.Ordinal);
        Assert.DoesNotContain("normalizedItems", productSpecs, StringComparison.Ordinal);

        Assert.Contains("product?.media", productVideo, StringComparison.Ordinal);
        Assert.Contains("mediaType?.toLowerCase() === 'video'", productVideo, StringComparison.Ordinal);
        Assert.DoesNotContain("videoUrl?: string", productVideo, StringComparison.Ordinal);
        Assert.DoesNotContain("product?.videoUrl", productVideo, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductVariantResolutionMustBeginAtBackendAuthorityAndFrontendSubmitsOptionCandidatesOnly()
    {
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontProductsController.cs");
        var storefrontProductService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var storefrontApi = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/services/api/storefrontApi.ts");
        var useVariantSelector = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/hooks/useVariantSelector.ts");
        var productHero = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx");

        Assert.Contains("[HttpPost(\"by-id/{id:guid}/variant-resolution\")]", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ApplyNoStoreHeaders", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ResolveProductVariantAsync", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("GetProductByIdAsync(", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("ResolveExactOptionCombination", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("HasExactOptionCombination", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("matches.Count == 1", storefrontProductService, StringComparison.Ordinal);

        Assert.Contains("resolveProductVariant", storefrontApi, StringComparison.Ordinal);
        Assert.Contains("selectedOptions: selectedAttributes", useVariantSelector, StringComparison.Ordinal);
        Assert.Contains("setSelectedVariant(response.variant)", useVariantSelector, StringComparison.Ordinal);
        Assert.Contains("const isValueAvailable = useCallback((): boolean => true, []);", useVariantSelector, StringComparison.Ordinal);
        Assert.Contains("hasVariantResolutionError", productHero, StringComparison.Ordinal);
        Assert.DoesNotContain("matchedBySku", useVariantSelector, StringComparison.Ordinal);
        Assert.DoesNotContain("matchedVariant", useVariantSelector, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductPricingPresentationMustBeBackendComposedAndFrontendFormattedOnly()
    {
        var storefrontDtos = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs");
        var storefrontProductService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var productDiscoveryDtos = CertificationSource.ReadRepoFile("PointOfSale.Core/DTOs/ProductDiscoveryDtos.cs");
        var productDiscoveryService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");
        var useVariantSelector = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/hooks/useVariantSelector.ts");
        var runtimeSectionContainer = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx");
        var productHero = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx");
        var productBadges = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductBadges.tsx");
        var productCardDefault = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductCardDefault.tsx");
        var productCardCompact = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductCardCompact.tsx");
        var productCardDetailed = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductCardDetailed.tsx");
        var productCardRuntime = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx");

        Assert.Contains("ProductPricingPresentationDto", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("StorefrontPricingPresentationMapper.Build", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("CurrentPrice", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("CompareAtPrice", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("DiscountedPrice", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("DiscountBadge", storefrontDtos, StringComparison.Ordinal);

        Assert.Contains("ProductDiscoveryPricingDto", productDiscoveryDtos, StringComparison.Ordinal);
        Assert.Contains("BuildPricingPresentation", productDiscoveryService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productDiscoveryService, StringComparison.Ordinal);

        Assert.Contains("const pricing = selectedVariant?.pricing ?? product?.pricing", useVariantSelector, StringComparison.Ordinal);
        Assert.Contains("pricing.currentPrice", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("pricing.compareAtPrice", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("model.pricing.current", productHero, StringComparison.Ordinal);
        Assert.Contains("useProductCardRuntime", productCardDefault, StringComparison.Ordinal);
        Assert.Contains("useProductCardRuntime", productCardCompact, StringComparison.Ordinal);
        Assert.Contains("useProductCardRuntime", productCardDetailed, StringComparison.Ordinal);
        Assert.Contains("product.pricing.currentPrice", productCardRuntime, StringComparison.Ordinal);
        Assert.Contains("product.pricing?.discountBadge ?? product.promotionBadge", productBadges, StringComparison.Ordinal);

        Assert.DoesNotContain("calculateDiscountedPrice", useVariantSelector, StringComparison.Ordinal);
        Assert.DoesNotContain("calculateDiscountedPrice", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.DoesNotContain("calculateDiscountedPrice", productCardDefault, StringComparison.Ordinal);
        Assert.DoesNotContain("calculateDiscountedPrice", productCardCompact, StringComparison.Ordinal);
        Assert.DoesNotContain("calculateDiscountedPrice", productCardDetailed, StringComparison.Ordinal);
        Assert.DoesNotContain("calculateDiscountedPrice", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("promotionDiscountValue / 100", useVariantSelector, StringComparison.Ordinal);
        Assert.DoesNotContain("promotionDiscountValue / 100", productCardDefault, StringComparison.Ordinal);
        Assert.DoesNotContain("promotionDiscountValue / 100", productCardCompact, StringComparison.Ordinal);
        Assert.DoesNotContain("promotionDiscountValue / 100", productCardDetailed, StringComparison.Ordinal);
        Assert.DoesNotContain("promotionDiscountValue / 100", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.max(0, basePrice", productCardDefault, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.max(0, basePrice", productCardCompact, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.max(0, basePrice", productCardDetailed, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.max(0, basePrice", productCardRuntime, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductAvailabilityAndPurchasabilityMustBeBackendComposedAndFrontendPresentationOnly()
    {
        var storefrontDtos = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs");
        var storefrontProductService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var productTypes = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/types/product.ts");
        var productHero = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx");
        var runtimeSectionContainer = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx");
        var productCardRuntime = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx");
        var useVariantSelector = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/hooks/useVariantSelector.ts");

        Assert.Contains("ProductAvailabilityPresentationDto", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("CanPurchase", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("CanAddToCart", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("CanBuyNow", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("MaximumQuantity", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("StorefrontAvailabilityPresentationMapper.Build", storefrontProductService, StringComparison.Ordinal);

        Assert.Contains("ProductAvailabilityPresentationDto", productTypes, StringComparison.Ordinal);
        Assert.Contains("availability?: ProductAvailabilityPresentationDto", productTypes, StringComparison.Ordinal);
        Assert.Contains("const availability = hasVariants ? selectedVariant?.availability : product.availability", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("availability?.canAddToCart === true", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("stockLabel", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("model.canAddToCart", productHero, StringComparison.Ordinal);
        Assert.Contains("product.availability?.canAddToCart === true", productCardRuntime, StringComparison.Ordinal);
        Assert.Contains("setSelectedVariant(response.variant)", useVariantSelector, StringComparison.Ordinal);
        Assert.DoesNotContain("product.isAvailable && isInStock", productHero, StringComparison.Ordinal);
        Assert.DoesNotContain("product.isAvailable && isInStock", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedVariant.totalStock", runtimeSectionContainer, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCartIntentAndAuthorityMustRemainBackendOwnedAndFailClosed()
    {
        var cartController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var cartService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cart/CartService.cs");
        var cartRecoveryController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs");
        var cartStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/cartStore.ts");
        var cartStateResolver = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/cartStateResolver.ts");
        var productHero = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx");
        var runtimeSectionContainer = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx");

        Assert.Contains("[HttpPost(\"items\")]", cartController, StringComparison.Ordinal);
        Assert.Contains("ValidateHeadersAsync(traceId)", cartController, StringComparison.Ordinal);
        Assert.Contains("_cartService.AddItemAsync", cartController, StringComparison.Ordinal);
        Assert.Contains("_cartService.UpdateItemByVariantAsync", cartController, StringComparison.Ordinal);
        Assert.Contains("_cartService.RemoveItemByVariantAsync", cartController, StringComparison.Ordinal);
        Assert.Contains("_cartService.MergeCartAsync", cartController, StringComparison.Ordinal);

        Assert.Contains("ExecuteWithCartMutationLockAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("ValidateAndFixCartAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("RevalidateCartProductsAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant)", cartService, StringComparison.Ordinal);
        Assert.Contains("SaveCartToRedisAsync", cartService, StringComparison.Ordinal);

        Assert.Contains("_liveCartService.GetCartAsync", cartRecoveryController, StringComparison.Ordinal);
        Assert.Contains("stored snapshot is not cart authority", cartRecoveryController, StringComparison.Ordinal);
        Assert.Contains("live_cart_unavailable", cartRecoveryController, StringComparison.Ordinal);
        Assert.DoesNotContain("stored_snapshot_sanitized", cartRecoveryController, StringComparison.Ordinal);

        Assert.Contains("await addCartItem({ productId, quantity, variantId })", cartStore, StringComparison.Ordinal);
        Assert.Contains("await updateCartItemByVariant(variantId, { quantity })", cartStore, StringComparison.Ordinal);
        Assert.Contains("await removeCartItemByVariant(variantId)", cartStore, StringComparison.Ordinal);
        Assert.Contains("isInitialized: false", cartStore, StringComparison.Ordinal);
        Assert.Contains("skipNextFetch: false", cartStore, StringComparison.Ordinal);

        Assert.Contains("nextCart: emptyCart", cartStateResolver, StringComparison.Ordinal);
        Assert.Contains("preserveLastValidCart: false", cartStateResolver, StringComparison.Ordinal);
        Assert.DoesNotContain("Showing your last saved cart", cartStateResolver, StringComparison.Ordinal);

        Assert.Contains("const success = await addItem(product.id, 1, variantId);", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.DoesNotContain("set({", productHero, StringComparison.Ordinal);
        Assert.DoesNotContain("set({", runtimeSectionContainer, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductMediaPresentationMustBeBackendComposedAndFrontendPresentationOnly()
    {
        var storefrontDtos = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs");
        var storefrontProductService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var mediaConsumer = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs");
        var overlay = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs");
        var mediaUtils = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/productMediaGalleryUtils.ts");
        var mediaGallery = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductMediaGallery.tsx");
        var mediaViewer = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductMediaViewer.tsx");
        var galleryModal = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductGalleryModal.tsx");
        var productVideo = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/sections/defaults/product-video.tsx");

        Assert.Contains("ProductMediaDto", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("PresentationUrl", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("ThumbnailUrl", storefrontDtos, StringComparison.Ordinal);
        Assert.Contains("Media = media", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("Images = images", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("BuildProductMediaPresentationUrl", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("BuildProductMediaThumbnailUrl", storefrontProductService, StringComparison.Ordinal);

        Assert.Contains("product.Media = sqlProduct.Media", overlay, StringComparison.Ordinal);
        Assert.Contains("IsStorefrontProductMediaType(media.MediaType)", overlay, StringComparison.Ordinal);
        Assert.Contains("string.Equals(mediaType, \"video\", StringComparison.OrdinalIgnoreCase)", overlay, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(item.MediaType, \"image\")", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(item.MediaType, \"video\")", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("OrderBy(item => item.Position)", mediaConsumer, StringComparison.Ordinal);

        Assert.Contains("product.media ?? []", mediaUtils, StringComparison.Ordinal);
        Assert.Contains("return left.id.localeCompare(right.id);", mediaUtils, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-image", mediaUtils, StringComparison.Ordinal);
        Assert.DoesNotContain("product.images ||", mediaUtils, StringComparison.Ordinal);
        Assert.DoesNotContain("getYouTubeEmbedUrl", mediaUtils, StringComparison.Ordinal);
        Assert.DoesNotContain("getYouTubeThumbnail", mediaUtils, StringComparison.Ordinal);
        Assert.DoesNotContain("youtube.com/embed", mediaUtils, StringComparison.Ordinal);
        Assert.DoesNotContain("img.youtube.com", mediaUtils, StringComparison.Ordinal);

        Assert.Contains("mediaItems.find((item) => item.mediaType?.toLowerCase() === 'image')?.url ?? null", mediaGallery, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveProductImage(product)", mediaGallery, StringComparison.Ordinal);
        Assert.Contains("media?.presentationUrl", mediaViewer, StringComparison.Ordinal);
        Assert.Contains("item.thumbnailUrl || null", mediaGallery, StringComparison.Ordinal);
        Assert.Contains("activeMedia?.presentationUrl", galleryModal, StringComparison.Ordinal);
        Assert.Contains("item.thumbnailUrl || null", galleryModal, StringComparison.Ordinal);
        Assert.Contains("productVideo?.presentationUrl", productVideo, StringComparison.Ordinal);
        Assert.DoesNotContain("videoUrl?: string", productVideo, StringComparison.Ordinal);
        Assert.DoesNotContain("getYoutubeEmbedUrl", productVideo, StringComparison.Ordinal);
        Assert.DoesNotContain("youtube.com/embed", productVideo, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchDiscoveryHomepageAndCategoriesMustRemainDerivativeSqlOverlayOnly()
    {
        var searchController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontSearchController.cs");
        var discoveryService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");
        var homeService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontHomeService.cs");
        var categoryService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs");
        var overlay = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs");

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", searchController, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.RequireLiveCategoryAsync", searchController, StringComparison.Ordinal);
        Assert.Contains("RecomposeHeaderSuggestionsBySqlAuthorityAsync", searchController, StringComparison.Ordinal);

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", discoveryService, StringComparison.Ordinal);
        Assert.Contains("IsActive: true, IsOnlineVisible: true", discoveryService, StringComparison.Ordinal);

        Assert.Contains("RecomposeProductCardsFromSqlAuthorityAsync", homeService, StringComparison.Ordinal);
        Assert.Contains("RecomposeHomepageCategoriesFromSqlAuthorityAsync", homeService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", homeService, StringComparison.Ordinal);
        Assert.Contains("purchasability.IsAvailable", homeService, StringComparison.Ordinal);

        Assert.Contains("BuildSqlCategoryTreeAsync", categoryService, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCategoryBySlugAsync", categoryService, StringComparison.Ordinal);
        Assert.Contains("lineage.IsLiveInLineage", categoryService, StringComparison.Ordinal);

        Assert.Contains("sqlProduct.PublishStatus == PublishStatus.Published", overlay, StringComparison.Ordinal);
        Assert.Contains("ResolveLiveCategoryNameAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("sqlProduct.HasVariants", overlay, StringComparison.Ordinal);
        Assert.Contains("PromotionVisibilityProjectionSupport.SelectWinningProductPromotion", overlay, StringComparison.Ordinal);
    }
}
