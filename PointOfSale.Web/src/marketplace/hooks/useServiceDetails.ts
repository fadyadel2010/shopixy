import { useQuery } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import type { ServiceDetails } from '../types';
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority';

/**
 * Hook for service details.
 * Returns service with pre-computed trust metrics from backend.
 * 
 * HARDENING:
 * - Uses standardized query keys
 */
export function useServiceDetails(serviceId: string) {
  const authorityScope = useMarketplaceAuthorityScope();

  return useQuery<ServiceDetails>({
    queryKey: marketplaceKeys.service(serviceId, authorityScope),
    queryFn: () => marketplaceApi.getServiceDetails(serviceId),
    enabled: !!serviceId,
    ...marketplaceAuthorityQueryOptions,
  });
}
