import { useQuery } from '@tanstack/react-query'
import { marketplaceApi } from '../api/marketplaceApi'
import { marketplaceKeys } from '../queryKeys'
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority'

export function useServiceReviews(serviceId: string) {
    const authorityScope = useMarketplaceAuthorityScope()
    return useQuery({
        queryKey: [...marketplaceKeys.service(serviceId, authorityScope), 'reviews'],
        queryFn: () => marketplaceApi.getServiceReviews(serviceId),
        enabled: !!serviceId,
        ...marketplaceAuthorityQueryOptions,
    })
}
