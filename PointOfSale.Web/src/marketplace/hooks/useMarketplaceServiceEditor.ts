import { useMutation, useQueryClient } from '@tanstack/react-query'
import { marketplaceApi } from '../api/marketplaceApi'
import { marketplaceKeys } from '../queryKeys'
import { publishMarketplaceAuthorityInvalidation, useMarketplaceAuthorityScope } from '../authority'
import type { CreateServiceRequest } from '../types'

export function useMarketplaceServiceEditor(onCreated: () => void, onUpdated: () => void) {
    const queryClient = useQueryClient()
    const authorityScope = useMarketplaceAuthorityScope()
    const invalidate = () => {
        publishMarketplaceAuthorityInvalidation(authorityScope)
        queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
    }
    const createServiceMutation = useMutation({
        mutationFn: (data: CreateServiceRequest) => marketplaceApi.createService(data),
        onSuccess: () => { queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) }); onCreated() },
        onSettled: invalidate,
    })
    const updateServiceMutation = useMutation({
        mutationFn: ({ id, data }: { id: string; data: CreateServiceRequest }) => marketplaceApi.updateService(id, data),
        onSuccess: () => { queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) }); onUpdated() },
        onSettled: invalidate,
    })
    return { createServiceMutation, updateServiceMutation }
}
