import { useMutation, useQueryClient } from '@tanstack/react-query'
import { marketplaceApi } from '../api/marketplaceApi'
import { marketplaceKeys } from '../queryKeys'
import { publishMarketplaceAuthorityInvalidation, useMarketplaceAuthorityScope } from '../authority'
import { deriveServiceRequestLifecycleMutationAuthority } from '../status'
import { executeServiceRequestCompletionWorkflow } from '../completionWorkflow'

export function useIncomingRequestCommands(actorUserId: unknown) {
    const queryClient = useQueryClient()
    const authorityScope = useMarketplaceAuthorityScope()
    const onSuccess = async () => { await queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) }) }
    const onSettled = async () => {
        publishMarketplaceAuthorityInvalidation(authorityScope)
        await queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
    }
    const acceptMutation = useMutation({ mutationFn: async (requestId: string) => {
        const response = await marketplaceApi.acceptRequest(requestId)
        if (!deriveServiceRequestLifecycleMutationAuthority(response, 'accept')) throw new Error('Invalid request accept lifecycle response')
        return response
    }, onSuccess, onSettled })
    const markCompletedMutation = useMutation({ mutationFn: (requestId: string) => executeServiceRequestCompletionWorkflow({
        requestId, authorityScope, actorUserId, kind: 'markCompleted',
        mutate: () => marketplaceApi.markCompleted(requestId), revalidate: () => marketplaceApi.getRequestDetails(requestId),
    }), onSuccess, onSettled })
    const rejectMutation = useMutation({ mutationFn: async (requestId: string) => {
        const response = await marketplaceApi.rejectRequest(requestId)
        if (!deriveServiceRequestLifecycleMutationAuthority(response, 'reject')) throw new Error('Invalid request rejection lifecycle response')
        return response
    }, onSuccess, onSettled })
    return { acceptMutation, markCompletedMutation, rejectMutation }
}
