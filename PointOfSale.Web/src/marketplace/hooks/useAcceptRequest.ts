import { useMutation, useQueryClient } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import {
  publishMarketplaceAuthorityInvalidation,
  useMarketplaceAuthorityScope,
} from '../authority';
import { deriveServiceRequestLifecycleMutationAuthority } from '../status';

/**
 * Hook for accepting a service request (provider).
 * 
 * HARDENING:
 * - Global invalidation on success (invalidates all marketplace queries)
 * - No selective cache patching
 * - No optimistic updates
 */
export function useAcceptRequest() {
  const queryClient = useQueryClient();
  const authorityScope = useMarketplaceAuthorityScope();

  return useMutation({
    mutationFn: async (requestId: string) => {
      const response = await marketplaceApi.acceptRequest(requestId);
      if (!deriveServiceRequestLifecycleMutationAuthority(response, 'accept')) {
        throw new Error('Invalid request accept lifecycle response');
      }
      return response;
    },
    onSuccess: () => {
      // Global invalidation - let React Query refetch what's needed
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
    onSettled: () => {
      publishMarketplaceAuthorityInvalidation(authorityScope);
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
  });
}
