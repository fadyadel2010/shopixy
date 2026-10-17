import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { marketplaceApi } from '../api/marketplaceApi'
import { marketplaceKeys } from '../queryKeys'
import { marketplaceAuthorityQueryOptions, publishMarketplaceAuthorityInvalidation, useMarketplaceAuthorityScope } from '../authority'
import type { MarketplaceProfileInput } from '../types'

export function useMarketplaceProfileStats(enabled: boolean) {
    const authorityScope = useMarketplaceAuthorityScope()
    return useQuery({ queryKey: marketplaceKeys.myProfileStats(authorityScope), queryFn: marketplaceApi.getMyProfileStats,
        enabled, ...marketplaceAuthorityQueryOptions })
}

export function useMarketplaceProfileEditor(onCreated: () => void, onUpdated: () => void) {
    const queryClient = useQueryClient()
    const authorityScope = useMarketplaceAuthorityScope()
    const invalidate = () => {
        publishMarketplaceAuthorityInvalidation(authorityScope)
        queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })
    }
    const createProfileMutation = useMutation({
        mutationFn: (data: MarketplaceProfileInput) => marketplaceApi.createProfile(data),
        onSuccess: () => { queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) }); onCreated() },
        onSettled: invalidate,
    })
    const updateProfileMutation = useMutation({
        mutationFn: (data: MarketplaceProfileInput) => marketplaceApi.updateProfile(data),
        onSuccess: () => { queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) }); onUpdated() },
        onSettled: invalidate,
    })
    return { createProfileMutation, updateProfileMutation }
}
