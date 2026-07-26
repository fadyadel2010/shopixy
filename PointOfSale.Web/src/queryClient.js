import { QueryClient } from '@tanstack/react-query'

const createRuntimeQueryClient = () => new QueryClient({
    defaultOptions: {
        queries: {
            staleTime: 30_000,
            retry: 1,
            refetchOnWindowFocus: false,
        },
    },
})

export const queryClient = createRuntimeQueryClient()
export const marketplaceQueryClient = createRuntimeQueryClient()
export const superAdminQueryClient = createRuntimeQueryClient()
