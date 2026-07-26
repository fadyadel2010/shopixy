import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Bot, PackageX, ShoppingBag, Store } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";

import { CommerceHubApiError } from "@/api/api-error";
import { ProductCard } from "@/commerce/components/ProductCard";
import { ProductGrid } from "@/commerce/components/ProductGrid";
import { ProductGridSkeleton } from "@/commerce/components/ProductGridSkeleton";
import { ProductFaqSection } from "@/features/product-details/components/ProductFaqSection";
import { ProductFeaturesSection } from "@/features/product-details/components/ProductFeaturesSection";
import { ProductDetailsSkeleton } from "@/features/product-details/components/ProductDetailsSkeleton";
import { ProductGallery } from "@/features/product-details/components/ProductGallery";
import { ProductTabs, type ProductTabItem } from "@/features/product-details/components/ProductTabs";
import { QuantitySelector } from "@/features/product-details/components/QuantitySelector";
import { StickyAddToCartBar } from "@/features/product-details/components/StickyAddToCartBar";
import { VariantSelector } from "@/features/product-details/components/VariantSelector";
import { ProductReviewsSection } from "@/features/product-reviews/components/ProductReviewsSection";
import { featuredProductsQueryOptions, productsQueryOptions, useProductDetail } from "@/products";
import { analyticsService } from "@/services";
import { commerceHubService } from "@/services/commercehub-service";
import { EmptyState } from "@/shared/EmptyState";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionHeading } from "@/shared/SectionHeading";
import { selectIsCartLocked, useCartStore, useToastStore } from "@/store";
import type { CommerceHubProduct, CommerceHubProductDetail, CommerceHubProductVariant } from "@/types";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency, isCommerceHubAuthorityCurrency, useDocumentTitle } from "@/utils";
import { sanitizePriceSnapshot } from "@/utils/price";

type AttributeGroup = {
  name: string;
  values: string[];
};

const buildAttributeGroups = (variants: CommerceHubProductVariant[]): AttributeGroup[] => {
  const attributeMap = new Map<string, string[]>();

  variants.forEach((variant) => {
    variant.attributes.forEach((attribute) => {
      const values = attributeMap.get(attribute.name) ?? [];

      if (!values.includes(attribute.value)) {
        values.push(attribute.value);
      }

      attributeMap.set(attribute.name, values);
    });
  });

  return Array.from(attributeMap.entries()).map(([name, values]) => ({
    name,
    values
  }));
};

const buildSelectionFromVariant = (variant?: CommerceHubProductVariant) =>
  Object.fromEntries((variant?.attributes ?? []).map((attribute) => [attribute.name, attribute.value]));

const buildGalleryImages = (
  product: CommerceHubProductDetail | undefined,
  selectedVariant: CommerceHubProductVariant | undefined
) => {
  if (!product) {
    return [];
  }

  const images = [
    selectedVariant?.imageUrl,
    product.image,
    ...product.media
      .filter((media) => media.mediaType.toLowerCase() === "image")
      .map((media) => media.url),
    ...product.images
  ].filter((image): image is string => Boolean(image));

  return Array.from(new Set(images));
};

const buildVariantLabel = (variant: CommerceHubProductVariant) => {
  const values = variant.attributes.map((attribute) => attribute.value);
  return values.length > 0 ? values.join(" / ") : "Default variant";
};

export const ProductDetailsPage = () => {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const productQuery = useProductDetail(id);
  const addItem = useCartStore((state) => state.addItem);
  const isCartLocked = useCartStore(selectIsCartLocked);
  const showToast = useToastStore((state) => state.showToast);
  const [selectedOptions, setSelectedOptions] = useState<Record<string, string>>({});
  const [activeImage, setActiveImage] = useState<string | null>(null);
  const [quantity, setQuantity] = useState(1);

  const product = productQuery.data;
  const variants = product?.variants ?? [];
  const attributeGroups = useMemo(() => buildAttributeGroups(variants), [variants]);

  const initialVariant = useMemo(() => {
    if (!product) {
      return undefined;
    }

    return variants.find((variant) => variant.id === product.defaultVariantId) ?? variants.find((variant) => variant.id === product.initialVariantId);
  }, [product, variants]);

  useEffect(() => {
    if (!product) {
      return;
    }

    setSelectedOptions(buildSelectionFromVariant(initialVariant));
  }, [initialVariant, product]);

  const hasCompleteOptionSelection = product
    ? attributeGroups.length === 0 || Object.keys(selectedOptions).length === attributeGroups.length
    : false;

  const variantResolutionQuery = useQuery({
    queryKey: ["commercehub", "products", "variant-resolution", id, selectedOptions],
    queryFn: ({ signal }) =>
      commerceHubService.resolveProductVariant(
        id,
        {
          selectedOptions
        },
        signal
      ),
    enabled: Boolean(product && id && hasCompleteOptionSelection)
  });

  const selectedVariant = variantResolutionQuery.data?.variant;

  const galleryImages = useMemo(() => buildGalleryImages(product, selectedVariant), [product, selectedVariant]);

  useEffect(() => {
    if (selectedVariant?.imageUrl) {
      setActiveImage(selectedVariant.imageUrl);
      return;
    }

    setActiveImage((currentImage) => {
      if (currentImage && galleryImages.includes(currentImage)) {
        return currentImage;
      }

      return galleryImages[0] ?? null;
    });
  }, [galleryImages, selectedVariant?.id, selectedVariant?.imageUrl]);

  useEffect(() => {
    if (!product) {
      return;
    }

    analyticsService.trackProductViewed(product.productId, selectedVariant?.id ?? product.initialVariantId, "product_page");
  }, [product, selectedVariant?.id]);

  useDocumentTitle(product?.seo?.metaTitle || (product ? product.productName : "Product"));

  const startingPrice = useMemo(() => {
    if (!product || variants.length === 0) {
      return product?.price ?? 0;
    }

    return variants.reduce((lowest, variant) => Math.min(lowest, variant.effectivePrice), variants[0]!.effectivePrice);
  }, [product, variants]);

  const relatedByCategoryQuery = useQuery({
    ...productsQueryOptions({
      categoryId: product?.categoryId || undefined,
      page: 1,
      pageSize: 8
    }),
    enabled: Boolean(product?.categoryId)
  });

  const fallbackProductsQuery = useQuery({
    ...featuredProductsQueryOptions(8),
    enabled: Boolean(product)
  });

  const relatedProducts = useMemo(() => {
    if (!product) {
      return [];
    }

    const categoryProducts = (relatedByCategoryQuery.data?.items ?? []).filter((item) => item.id !== product.id);
    const fallbackProducts = (fallbackProductsQuery.data?.items ?? []).filter(
      (item) => item.id !== product.id && !categoryProducts.some((categoryProduct) => categoryProduct.id === item.id)
    );

    return [...categoryProducts, ...fallbackProducts].slice(0, 4);
  }, [fallbackProductsQuery.data?.items, product, relatedByCategoryQuery.data?.items]);

  const maxSelectableQuantity = selectedVariant?.inStock ? Math.max(1, selectedVariant.totalStock) : 1;

  useEffect(() => {
    setQuantity((currentQuantity) => Math.min(Math.max(1, currentQuantity), maxSelectableQuantity));
  }, [maxSelectableQuantity]);

  const lowStockLabel =
    selectedVariant && selectedVariant.inStock && selectedVariant.totalStock < 5
      ? `Only ${selectedVariant.totalStock} left in stock`
      : undefined;

  const priceLabel = selectedVariant
    ? formatCurrency(selectedVariant.effectivePrice, product?.currency)
    : product?.hasVariants
      ? `From ${formatCurrency(startingPrice, product?.currency)}`
      : product
        ? formatCurrency(product.price, product.currency)
        : "";

  const stockState = selectedVariant
    ? selectedVariant.inStock
      ? { label: `In stock (${selectedVariant.totalStock})`, variant: "success" as const }
      : { label: "Out of stock", variant: "muted" as const }
    : product?.hasVariants
      ? { label: "Select options to confirm stock", variant: "accent" as const }
      : product?.inStock
        ? { label: `In stock (${product.stockQuantity})`, variant: "success" as const }
        : { label: "Out of stock", variant: "muted" as const };

  const hasAuthorityCurrency = isCommerceHubAuthorityCurrency(product?.currency);
  const variantResolutionPending = Boolean(product && hasCompleteOptionSelection && variantResolutionQuery.isPending);
  const variantResolutionFailed = Boolean(product && hasCompleteOptionSelection && variantResolutionQuery.isError);
  const addToCartDisabled =
    isCartLocked ||
    variantResolutionPending ||
    variantResolutionFailed ||
    !hasAuthorityCurrency ||
    !product?.isAvailable ||
    !selectedVariant ||
    !selectedVariant.inStock;
  const addToCartLabel = isCartLocked
    ? "Checkout in progress"
    : variantResolutionPending
      ? "Resolving options"
      : variantResolutionFailed
        ? "Invalid option combination"
    : !product?.isAvailable
    ? "Unavailable"
    : !selectedVariant
      ? "Select options to continue"
      : !selectedVariant.inStock
        ? "Out of stock"
        : quantity > 1
          ? `Add ${quantity} to cart`
          : "Add to cart";

  const handleOptionSelect = (attributeName: string, value: string) => {
    setSelectedOptions((current) => ({
      ...current,
      [attributeName]: value
    }));
  };

  const getOptionState = (attributeName: string, value: string) => {
    const isSelected = selectedOptions[attributeName] === value;

    if (isSelected) {
      return {
        disabled: false,
        selected: true,
        inStock: true
      };
    }

    return {
      disabled: false,
      selected: false,
      inStock: true
    };
  };

  const handleAddToCart = () => {
    if (!product) {
      return;
    }

    if (!isCommerceHubAuthorityCurrency(product.currency)) {
      showToast({
        title: "Product unavailable",
        description: "This listing did not provide CommerceHub EGP currency authority.",
        tone: "error"
      });
      return;
    }

    if (isCartLocked) {
      showToast({
        title: "Cart is locked",
        description: "Checkout is already in progress for this cart. Wait for it to finish before adding more items.",
        tone: "error"
      });
      return;
    }

    if (!selectedVariant) {
      showToast({
        title: "Select a variant first",
        description: "Choose the product options you want before adding this item to your cart.",
        tone: "error"
      });
      return;
    }

    if (!selectedVariant.inStock) {
      showToast({
        title: "This variant is out of stock",
        description: "Pick another option to continue shopping.",
        tone: "error"
      });
      return;
    }

    const variantLabel = buildVariantLabel(selectedVariant);
    const cartName = attributeGroups.length > 0 ? `${product.productName} - ${variantLabel}` : product.productName;

    addItem({
      storeId: product.tenantId,
      listingId: product.id,
      storeName: product.storeName,
      productId: product.productId,
      variantId: selectedVariant.id,
      name: product.productName,
      priceSnapshot: sanitizePriceSnapshot(selectedVariant.effectivePrice),
      quantity,
      imageUrl: selectedVariant.imageUrl ?? activeImage ?? product.image,
      variantLabel: selectedVariant.attributes.length > 0 ? variantLabel : undefined,
      variantAttributes: selectedVariant.attributes.length > 0 ? selectedVariant.attributes : undefined,
      maxQuantity: selectedVariant.totalStock,
      isInStock: selectedVariant.inStock,
      shippingFee: product.storeShippingFee,
      currency: product.currency
    });

    analyticsService.trackProductAddedToCart(product.productId, selectedVariant.id, quantity, "product_page");

    showToast({
      title: "Added to cart",
      description: `${cartName} is ready in your cart.`,
      tone: "success"
    });
  };

  if (!id) {
    return (
      <EmptyState
        title="Product unavailable"
        description="This product link is missing the product identifier."
        actionLabel="Back to products"
        onAction={() => navigate("/products")}
      />
    );
  }

  if (productQuery.isPending) {
    return <ProductDetailsSkeleton />;
  }

  if (productQuery.isError) {
    if (productQuery.error instanceof CommerceHubApiError && productQuery.error.status === 404) {
      return (
        <EmptyState
          icon={<PackageX className="h-6 w-6" />}
          title="Product not found"
          description="This Shopixy listing may have been removed or is no longer available."
          actionLabel="Browse products"
          onAction={() => navigate("/products")}
        />
      );
    }

    return (
      <InlineErrorState
        title="Product unavailable"
        message={productQuery.error.message}
        onRetry={() => void productQuery.refetch()}
      />
    );
  }

  if (!product) {
    return <EmptyState title="Product unavailable" description="We couldn't load the product details right now." />;
  }

  const selectedVariantLabel = selectedVariant
    ? buildVariantLabel(selectedVariant)
    : product.hasVariants
      ? "Select options to continue"
      : "Ready to add";
  const productDescription = product.description?.trim();
  const shippingLabel =
    product.storeShippingFee > 0
      ? `Shipping from ${formatCurrency(product.storeShippingFee, product.currency)}`
      : "Shipping is calculated per store during checkout";
  const topHighlights = product.features.slice(0, 3);
  const detailTabs: ProductTabItem[] = [
    {
      id: "overview",
      label: "Overview",
      content: () => (
        <div className="grid gap-5 xl:grid-cols-[minmax(0,1.12fr)_minmax(18rem,0.88fr)]">
          <Card className="border-white/75 bg-white/92">
            <CardContent className="space-y-5 p-6 md:p-7">
              <div className="space-y-2">
                <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">Overview</p>
                <h2 className="text-2xl font-black tracking-tight text-foreground">About this product</h2>
              </div>
              {productDescription ? <p className="text-sm leading-8 text-muted-foreground">{productDescription}</p> : null}
            </CardContent>
          </Card>

          {topHighlights.length > 0 || product.tags.length > 0 ? (
            <Card className="border-white/75 bg-white/92">
              <CardContent className="space-y-5 p-6 md:p-7">
                {topHighlights.length > 0 ? (
                  <div className="space-y-3">
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">Top highlights</p>
                    <ul className="space-y-3">
                      {topHighlights.map((feature) => (
                        <li key={feature} className="flex gap-3 text-sm leading-7 text-foreground">
                          <span className="mt-3 h-2 w-2 shrink-0 rounded-full bg-primary" />
                          <span>{feature}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                ) : null}

                {product.tags.length > 0 ? (
                  <div className="space-y-3">
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">Tags</p>
                    <div className="flex flex-wrap gap-2">
                      {product.tags.map((tag) => (
                        <Badge key={tag} variant="muted">
                          {tag}
                        </Badge>
                      ))}
                    </div>
                  </div>
                ) : null}
              </CardContent>
            </Card>
          ) : null}
        </div>
      )
    },
    ...(product.features.length > 0
      ? [
          {
            id: "features",
            label: "Features",
            content: () => <ProductFeaturesSection features={product.features} />
          }
        ]
      : []),
    {
      id: "reviews",
      label: "Reviews",
      lazy: true,
      content: () => <ProductReviewsSection productId={product.productId} productName={product.productName} showHeading={false} />
    },
    ...(product.faqs.length > 0
      ? [
          {
            id: "faqs",
            label: "FAQs",
            content: () => <ProductFaqSection faqs={product.faqs} />
          }
        ]
      : [])
  ];

  return (
    <div className="space-y-10 pb-28 lg:pb-0">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Button asChild type="button" variant="outline" size="sm" className="gap-2">
          <Link to="/products">
            <ArrowLeft className="h-4 w-4" />
            Back to products
          </Link>
        </Button>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="accent">Shopixy product detail</Badge>
          {product.aiGenerated ? (
            <Badge variant="muted" className="gap-1">
              <Bot className="h-3.5 w-3.5" />
              AI generated
            </Badge>
          ) : null}
        </div>
      </div>

      <div className="grid gap-8 xl:grid-cols-[minmax(0,1.08fr)_minmax(22rem,0.92fr)] xl:items-start">
        <div>
          <ProductGallery
            images={galleryImages}
            productName={product.productName}
            activeImage={activeImage}
            onActiveImageChange={setActiveImage}
          />
        </div>

        <div className="space-y-5 xl:sticky xl:top-28">
          <Card className="overflow-hidden border-white/80 bg-white/94">
            <div className="brand-gradient h-2 w-full" />
            <CardContent className="space-y-6 p-6 md:p-7">
              <div className="space-y-4">
                <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
                  {product.categoryName ? <Badge variant="muted">{product.categoryName}</Badge> : null}
                  {product.isPublished ? <Badge variant="default">Live listing</Badge> : null}
                  {selectedVariant ? <Badge variant="success">SKU ready</Badge> : null}
                  {product.aiGenerated ? (
                    <Badge variant="muted" className="gap-1">
                      <Bot className="h-3.5 w-3.5" />
                      AI enriched
                    </Badge>
                  ) : null}
                </div>

                <div className="space-y-2">
                  <h1 className="text-3xl font-black tracking-tight text-foreground md:text-4xl">{product.productName}</h1>
                </div>
              </div>

              <div className="rounded-[1.85rem] border border-white/70 bg-secondary/55 p-5">
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div>
                    <p className="text-sm font-medium uppercase tracking-[0.18em] text-muted-foreground">Price</p>
                    <p className="mt-2 text-3xl font-black tracking-tight text-foreground">{priceLabel}</p>
                    <p className="mt-2 text-sm text-muted-foreground">{selectedVariantLabel}</p>
                    {selectedVariant?.sku ? <p className="mt-1 text-sm text-muted-foreground">SKU: {selectedVariant.sku}</p> : null}
                  </div>

                  <Badge variant={stockState.variant}>{stockState.label}</Badge>
                </div>

                {lowStockLabel ? <p className="mt-4 text-sm font-semibold text-amber-600">{lowStockLabel}</p> : null}

                {selectedVariant && selectedVariant.attributes.length > 0 ? (
                  <div className="mt-4 flex flex-wrap gap-2">
                    {selectedVariant.attributes.map((attribute) => (
                      <Badge key={`${attribute.name}-${attribute.value}`} variant="muted">
                        {attribute.name}: {attribute.value}
                      </Badge>
                    ))}
                  </div>
                ) : null}
              </div>

              <Card className="border-white/70 bg-white/88 shadow-none">
                <CardContent className="space-y-4 p-5">
                  <div className="flex items-start gap-3">
                    <div className="rounded-full bg-secondary p-2 text-primary">
                      <Store className="h-4 w-4" />
                    </div>
                    <div className="min-w-0 space-y-1">
                      <p className="text-sm font-medium uppercase tracking-[0.18em] text-muted-foreground">Vendor</p>
                      <p className="text-lg font-bold text-foreground">{product.storeName}</p>
                      <p className="text-sm text-muted-foreground">{shippingLabel}</p>
                    </div>
                  </div>
                </CardContent>
              </Card>

              <div className="space-y-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-base font-semibold text-foreground">Choose your options</p>
                    <p className="text-sm text-muted-foreground">Live stock and gallery updates stay in sync as you switch selections.</p>
                  </div>
                </div>

                <VariantSelector
                  groups={attributeGroups}
                  selectedOptions={selectedOptions}
                  onSelect={handleOptionSelect}
                  getOptionState={getOptionState}
                />
              </div>

              <div className="hidden gap-4 lg:grid lg:grid-cols-[auto_minmax(0,1fr)] lg:items-end">
                <div className="min-w-0">
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Quantity</p>
                  <div className="mt-3">
                    <QuantitySelector value={quantity} max={maxSelectableQuantity} disabled={addToCartDisabled} onChange={setQuantity} />
                  </div>
                </div>

                <div className="flex-1">
                  <Button
                    type="button"
                    className="h-12 w-full gap-2 text-base"
                    disabled={addToCartDisabled}
                    onClick={handleAddToCart}
                  >
                    <ShoppingBag className="h-4 w-4" />
                    {addToCartLabel}
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>

      <ProductTabs items={detailTabs} defaultTabId="overview" />

      <section className="space-y-5">
        <SectionHeading
          eyebrow="More from Shopixy"
          title="Keep browsing with related premium picks"
          description="Related discovery keeps shoppers moving without leaving the current product context."
        />

        {relatedByCategoryQuery.isPending && fallbackProductsQuery.isPending ? <ProductGridSkeleton count={4} /> : null}

        {!relatedByCategoryQuery.isPending && !fallbackProductsQuery.isPending && relatedProducts.length > 0 ? (
          <ProductGrid className="xl:grid-cols-4">
            {relatedProducts.map((relatedProduct: CommerceHubProduct) => (
              <ProductCard
                key={relatedProduct.id}
                product={relatedProduct}
                onClick={(clickedProduct) =>
                  analyticsService.trackProductClicked(clickedProduct.productId, clickedProduct.variantId, "related_products")
                }
              />
            ))}
          </ProductGrid>
        ) : null}
      </section>

      <StickyAddToCartBar
        priceLabel={priceLabel}
        subtitle={selectedVariantLabel}
        quantity={quantity}
        maxQuantity={maxSelectableQuantity}
        disabled={addToCartDisabled}
        buttonLabel={addToCartLabel}
        lowStockLabel={lowStockLabel}
        onQuantityChange={setQuantity}
        onAddToCart={handleAddToCart}
      />
    </div>
  );
};
