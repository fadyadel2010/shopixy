import { useQuery } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import type { ServiceRequest } from '../types';
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority';

/**
 * Hook for provider's incoming requests.
 * Returns requests with capability flags from backend.
 * 
 * HARDENING:
 * - Uses standardized query keys
 */
export function useIncomingRequests() {
  const authorityScope = useMarketplaceAuthorityScope();

  return useQuery<ServiceRequest[]>({
    queryKey: marketplaceKeys.incomingRequests(authorityScope),
    queryFn: () => marketplaceApi.getIncomingRequests(),
    ...marketplaceAuthorityQueryOptions,
  });
}
