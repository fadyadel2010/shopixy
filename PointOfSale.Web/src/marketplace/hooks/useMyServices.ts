import { useQuery } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import type { MyService } from '../types';
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority';

/**
 * Hook for fetching my services (provider).
 * 
 * HARDENING:
 * - Uses standardized query keys
 */
export function useMyServices() {
  const authorityScope = useMarketplaceAuthorityScope();

  return useQuery<MyService[]>({
    queryKey: marketplaceKeys.myServices(authorityScope),
    queryFn: () => marketplaceApi.getMyServices(),
    ...marketplaceAuthorityQueryOptions,
  });
}
