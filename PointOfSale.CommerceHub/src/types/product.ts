export type CommerceHubProduct = {
  id: string;
  tenantId: string;
  storeName: string;
  storeShippingFee: number;
  productId: string;
  variantId: string;
  productName: string;
  image?: string | null;
  price: number;
  currency: string;
  categoryId: string;
  categoryName?: string | null;
  categoryPath?: string | null;
  inStock: boolean;
  availableStock: number;
  isPublished: boolean;
};

export type CommerceHubProductSort = "relevance" | "price_asc" | "price_desc" | "newest";

export type CommerceHubProductVariantAttribute = {
  name: string;
  value: string;
};

export type CommerceHubProductVariant = {
  id: string;
  sku?: string | null;
  price: number;
  onlinePrice?: number | null;
  effectivePrice: number;
  mediaId?: string | null;
  imageUrl?: string | null;
  totalStock: number;
  inStock: boolean;
  isDefault: boolean;
  isActive: boolean;
  attributes: CommerceHubProductVariantAttribute[];
};

export type CommerceHubVariantResolutionRequest = {
  selectedOptions: Record<string, string>;
};

export type CommerceHubVariantResolutionResponse = {
  productId: string;
  listingId: string;
  resolvedVariantId: string;
  variant: CommerceHubProductVariant;
};

export type CommerceHubProductMedia = {
  id: string;
  mediaType: string;
  url: string;
  provider?: string | null;
  position: number;
};

export type CommerceHubProductSeo = {
  metaTitle?: string | null;
  metaDescription?: string | null;
};

export type CommerceHubProductFaq = {
  question: string;
  answer: string;
};

export type CommerceHubProductDetail = {
  id: string;
  tenantId: string;
  storeName: string;
  storeShippingFee: number;
  productId: string;
  initialVariantId: string;
  productName: string;
  description?: string | null;
  image?: string | null;
  price: number;
  currency: string;
  categoryId: string;
  categoryName?: string | null;
  categoryPath?: string | null;
  inStock: boolean;
  stockQuantity: number;
  isPublished: boolean;
  isAvailable: boolean;
  hasVariants: boolean;
  defaultVariantId?: string | null;
  features: string[];
  faqs: CommerceHubProductFaq[];
  seo: CommerceHubProductSeo;
  tags: string[];
  aiGenerated: boolean;
  aiMetadata?: Record<string, unknown> | null;
  media: CommerceHubProductMedia[];
  images: string[];
  variants: CommerceHubProductVariant[];
};

export type CommerceHubProductsQuery = {
  page?: number;
  pageSize?: number;
  categoryId?: string;
  tenantId?: string;
  inStock?: boolean;
  minPrice?: number;
  maxPrice?: number;
  search?: string;
  sort?: CommerceHubProductSort;
};

export type CommerceHubProductListResponse = {
  items: CommerceHubProduct[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type ProductDiscoveryFilters = {
  categoryId?: string;
  search?: string;
  inStock?: boolean;
  minPrice?: number;
  maxPrice?: number;
  sort?: CommerceHubProductSort;
};
