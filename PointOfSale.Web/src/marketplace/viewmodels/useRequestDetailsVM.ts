import { useRequestDetails } from '../hooks/useRequestDetails';
import { formatDistanceToNow } from 'date-fns';
import { deriveServiceRequestDetailsLifecyclePresentation } from '../status';
import { deriveServiceRequestCompletionEligibility } from '../completionEligibility';
import { deriveServiceRequestCompletionStatePresentation } from '../completionStateAuthority';
import { deriveServiceRequestRatingEligibility } from '../ratingEligibility';
import { deriveServiceRequestRatingStatePresentation } from '../ratingStateAuthority';
import { useAuthStore } from '../../store/authStore';

/**
 * useRequestDetailsVM - ViewModel for request details.
 * RULES:
 * - Consumes hooks
 * - Maps to UI props
 * - Adds formatted text
 * - NO domain logic
 */
export function useRequestDetailsVM(requestId: string) {
  const {
    data: request,
    isLoading,
    isFetching,
    isError,
    error,
  } = useRequestDetails(requestId);
  const { user } = useAuthStore();
  const currentUserId = user?.id ?? user?.userId;
  const lifecycleCandidate = request ? deriveServiceRequestDetailsLifecyclePresentation(request) : null;
  const completionState = request
    ? deriveServiceRequestCompletionStatePresentation(request, lifecycleCandidate, currentUserId)
    : null;
  const lifecycle = completionState?.lifecycle ?? null;
  const completionEligibility = request && lifecycle
    ? deriveServiceRequestCompletionEligibility(request, lifecycle, currentUserId)
    : null;
  const ratingEligibility = request && lifecycle
    ? deriveServiceRequestRatingEligibility(request, lifecycle, currentUserId)
    : null;
  const ratingState = request && lifecycle && !isLoading && !isFetching && !isError
    ? deriveServiceRequestRatingStatePresentation(request, lifecycle, currentUserId)
    : null;
  const completionEvidence = request && !isLoading && !isFetching && !isError
    ? completionState?.completionEvidence ?? null
    : null;

  const formattedDate = request && lifecycle ? formatDistanceToNow(new Date(request.createdAt), { addSuffix: true }) : '';

  return {
    request: lifecycle ? request : undefined,
    isLoading,
    isFetching,
    error: error || (request && !lifecycle ? new Error('Invalid request lifecycle presentation') : null),
    formattedDate,
    lifecycle,
    canChat: lifecycle?.canSendChat ?? false,
    canViewChatHistory: lifecycle?.canViewChatHistory ?? false,
    canAccept: lifecycle?.canAccept ?? false,
    canMarkCompleted: completionEligibility?.canMarkCompleted ?? false,
    canConfirmCompletion: completionEligibility?.canConfirmCompletion ?? false,
    canCancel: lifecycle?.canCancel ?? false,
    canReview: ratingEligibility?.canRate ?? false,
    ratingState,
    completionEvidence,
  };
}
