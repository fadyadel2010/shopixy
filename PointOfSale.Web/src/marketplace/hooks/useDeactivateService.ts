import { useMutation, useQueryClient } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import {
  publishMarketplaceAuthorityInvalidation,
  useMarketplaceAuthorityScope,
} from '../authority';

/**
 * Hook for deactivating a service (provider).
 * 
 * HARDENING:
 * - Global invalidation on success
 * - No selective cache patching
 */
export function useDeactivateService() {
  const queryClient = useQueryClient();
  const authorityScope = useMarketplaceAuthorityScope();

  return useMutation({
    mutationFn: (serviceId: string) => marketplaceApi.deactivateService(serviceId),
    onSuccess: (_result, serviceId) => {
      publishMarketplaceAuthorityInvalidation(authorityScope, serviceId);
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
  });
}
