import { useMutation, useQueryClient } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import {
  publishMarketplaceAuthorityInvalidation,
  useMarketplaceAuthorityScope,
} from '../authority';
import { deriveServiceRequestLifecycleMutationAuthority } from '../status';

/**
 * Hook for canceling a request (buyer).
 * 
 * HARDENING:
 * - Global invalidation on success
 * - No selective cache patching
 */
export function useCancelRequest() {
  const queryClient = useQueryClient();
  const authorityScope = useMarketplaceAuthorityScope();

  return useMutation({
    mutationFn: async ({ requestId, reason }: { requestId: string; reason?: string }) => {
      const response = await marketplaceApi.cancelRequest(requestId, reason);
      if (!deriveServiceRequestLifecycleMutationAuthority(response, 'cancel')) {
        throw new Error('Invalid request cancellation lifecycle response');
      }
      return response;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
    onSettled: () => {
      publishMarketplaceAuthorityInvalidation(authorityScope);
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
  });
}
