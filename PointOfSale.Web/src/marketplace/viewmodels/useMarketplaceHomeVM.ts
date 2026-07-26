import { useMarketplaceSearch } from '../hooks/useMarketplaceSearch';
import type { ServiceSearchRequest } from '../types';

/**
 * useMarketplaceHomeVM - ViewModel for MarketplaceHome page.
 * RULES:
 * - Consumes hooks
 * - Maps to UI props
 * - Adds loading + empty flags
 * - NO domain logic
 */
export function useMarketplaceHomeVM(searchParams: ServiceSearchRequest) {
  const { data, isLoading, error } = useMarketplaceSearch(searchParams);

  const services = data?.items || [];
  const totalCount = data?.totalCount || 0;
  const isEmpty = !isLoading && services.length === 0;
  const hasFilters = !!(searchParams.query || searchParams.minRating || searchParams.trustBadge);

  return {
    services,
    totalCount,
    isLoading,
    isEmpty,
    hasFilters,
    error,
    page: data?.page || searchParams.page,
    pageSize: data?.pageSize || searchParams.pageSize,
  };
}
