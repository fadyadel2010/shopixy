import { useMutation, useQueryClient } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import type { CreateReviewRequest } from '../types';
import { useMarketplaceAuthorityScope } from '../authority';
import { publishMarketplaceAuthorityInvalidation } from '../authority';
import { useAuthStore } from '../../store/authStore';
import { executeServiceRequestRatingWorkflow } from '../ratingWorkflow';

/**
 * Hook for creating a review.
 * 
 * HARDENING:
 * - Global invalidation on success (trust metrics may change)
 * - No selective cache patching
 */
export function useCreateReview() {
  const queryClient = useQueryClient();
  const authorityScope = useMarketplaceAuthorityScope();
  const { user } = useAuthStore();
  const currentUserId = user?.id ?? user?.userId;

  return useMutation({
    mutationFn: (data: CreateReviewRequest) => executeServiceRequestRatingWorkflow({
      review: data,
      authorityScope,
      actorUserId: currentUserId,
      revalidate: () => marketplaceApi.getRequestDetails(data.serviceRequestId),
      mutate: (validatedReview) => marketplaceApi.createReview(validatedReview),
    }),
    retry: false,
    networkMode: 'online',
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
    onSettled: () => {
      publishMarketplaceAuthorityInvalidation(authorityScope);
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
  });
}
