import { useQuery } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import type { ServiceRequestDetails } from '../types';
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority';

/**
 * Hook for service request details.
 * Returns request with capabilities object from backend.
 * UI renders buttons ONLY based on these flags.
 * 
 * HARDENING:
 * - Uses standardized query keys
 */
export function useRequestDetails(requestId: string) {
  const authorityScope = useMarketplaceAuthorityScope();

  return useQuery<ServiceRequestDetails>({
    queryKey: marketplaceKeys.request(requestId, authorityScope),
    queryFn: () => marketplaceApi.getRequestDetails(requestId),
    enabled: !!requestId,
    ...marketplaceAuthorityQueryOptions,
    refetchInterval: 15_000,
  });
}
