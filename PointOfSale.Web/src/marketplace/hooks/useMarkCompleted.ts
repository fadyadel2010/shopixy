import { useMutation, useQueryClient } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import {
  publishMarketplaceAuthorityInvalidation,
  useMarketplaceAuthorityScope,
} from '../authority';
import { executeServiceRequestCompletionWorkflow } from '../completionWorkflow';
import { useAuthStore } from '../../store/authStore';

/**
 * Hook for marking request as completed (provider).
 * 
 * HARDENING:
 * - Global invalidation on success
 * - No selective cache patching
 */
export function useMarkCompleted() {
  const queryClient = useQueryClient();
  const authorityScope = useMarketplaceAuthorityScope();
  const { user } = useAuthStore();
  const actorUserId = user?.id ?? user?.userId;

  return useMutation({
    mutationFn: (requestId: string) => executeServiceRequestCompletionWorkflow({
      requestId,
      authorityScope,
      actorUserId,
      kind: 'markCompleted',
      mutate: () => marketplaceApi.markCompleted(requestId),
      revalidate: () => marketplaceApi.getRequestDetails(requestId),
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
    onSettled: () => {
      publishMarketplaceAuthorityInvalidation(authorityScope);
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
  });
}
