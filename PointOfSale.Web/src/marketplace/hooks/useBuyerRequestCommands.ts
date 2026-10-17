import { useMutation, useQueryClient } from '@tanstack/react-query'
import { marketplaceApi } from '../api/marketplaceApi'
import { marketplaceKeys } from '../queryKeys'
import { publishMarketplaceAuthorityInvalidation, useMarketplaceAuthorityScope } from '../authority'
import { executeServiceRequestCompletionWorkflow } from '../completionWorkflow'
import { executeServiceRequestRatingWorkflow } from '../ratingWorkflow'
import type { CreateReviewRequest } from '../types'

export function useBuyerRequestCommands(actorUserId: unknown) {
    const queryClient = useQueryClient()
    const authorityScope = useMarketplaceAuthorityScope()
    const onSuccess = () => { queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) }) }
    const onSettled = () => {
        publishMarketplaceAuthorityInvalidation(authorityScope)
        queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
    }
    const confirmCompletionMutation = useMutation({ mutationFn: (requestId: string) => executeServiceRequestCompletionWorkflow({
        requestId, authorityScope, actorUserId, kind: 'confirmCompletion',
        mutate: () => marketplaceApi.confirmCompletion(requestId), revalidate: () => marketplaceApi.getRequestDetails(requestId),
    }), onSuccess, onSettled })
    const submitReviewMutation = useMutation({ mutationFn: (review: CreateReviewRequest) => executeServiceRequestRatingWorkflow({
        review, authorityScope, actorUserId, revalidate: () => marketplaceApi.getRequestDetails(review.serviceRequestId),
        mutate: validatedReview => marketplaceApi.createReview(validatedReview),
    }), retry: false, networkMode: 'online', onSuccess, onSettled })
    return { confirmCompletionMutation, submitReviewMutation }
}
