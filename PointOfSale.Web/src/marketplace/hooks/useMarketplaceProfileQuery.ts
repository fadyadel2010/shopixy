import { useQuery } from '@tanstack/react-query'
import { marketplaceApi } from '../api/marketplaceApi'
import { marketplaceKeys } from '../queryKeys'
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority'

export function useMarketplaceProfileQuery() {
    const authorityScope = useMarketplaceAuthorityScope()
    return useQuery({
        queryKey: marketplaceKeys.myProfile(authorityScope),
        queryFn: marketplaceApi.getMyProfile,
        ...marketplaceAuthorityQueryOptions,
    })
}
