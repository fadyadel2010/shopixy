import type { ApiJson } from '../../../services/apiJson';
export const PUBLIC_PRODUCT_LIST_ROUTE: '/api/affiliate/discover/public-products';
export const PUBLIC_SORT_OPTIONS: { value: string; label: string }[];
export const PUBLIC_SORT_VALUES: Set<string>;
export const DEFAULT_PUBLIC_SORT: 'newest';
export function isValidPublicSort(sort: unknown): boolean;
export function discoverPublicProducts(options?: { page?: number; pageSize?: number; search?: string; categorySlug?: string; sort?: string; signal?: AbortSignal }): Promise<ApiJson>;
export function discoverPublicProductDetail(productId: string, options?: { signal?: AbortSignal }): Promise<ApiJson>;
