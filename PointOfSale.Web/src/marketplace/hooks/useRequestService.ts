import { useMutation, useQueryClient } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import {
  publishMarketplaceAuthorityInvalidation,
  useMarketplaceAuthorityScope,
} from '../authority';
import { deriveCreatedServiceRequestAuthority } from '../requestCreationAuthority';

/**
 * Hook for requesting a service.
 * 
 * HARDENING:
 * - Global invalidation on success
 * - No selective cache patching
 */
export function useRequestService() {
  const queryClient = useQueryClient();
  const authorityScope = useMarketplaceAuthorityScope();

  return useMutation({
    mutationKey: [...marketplaceKeys.scope(authorityScope), 'request-service'],
    mutationFn: async (data: { serviceId: string; requestMessage?: string }) => {
      const response = await marketplaceApi.requestService(data);
      const createdRequest = deriveCreatedServiceRequestAuthority(response, data.serviceId);
      if (!createdRequest) {
        throw new Error('Request creation response did not contain authoritative created-request identity');
      }

      return createdRequest;
    },
    retry: false,
    networkMode: 'always',
    onSuccess: (_result, variables) => {
      publishMarketplaceAuthorityInvalidation(authorityScope, variables.serviceId);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
  });
}
