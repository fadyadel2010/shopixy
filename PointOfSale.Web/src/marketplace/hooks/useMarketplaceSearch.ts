import { useQuery } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import type { ServiceSearchRequest, ServiceSearchResponse } from '../types';
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority';

/**
 * Hook for marketplace search.
 * Returns exactly what API returns - NO client-side filtering or sorting.
 * 
 * HARDENING:
 * - Uses standardized query keys
 * - No ad-hoc keys
 */
export function useMarketplaceSearch(request: ServiceSearchRequest) {
  const authorityScope = useMarketplaceAuthorityScope();

  return useQuery<ServiceSearchResponse>({
    queryKey: marketplaceKeys.search(request, authorityScope),
    queryFn: () => marketplaceApi.searchServices(request),
    ...marketplaceAuthorityQueryOptions,
  });
}
