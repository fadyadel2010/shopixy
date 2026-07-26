import { QueryClientProvider } from '@tanstack/react-query'
import { marketplaceQueryClient, superAdminQueryClient } from '../queryClient'

export function MarketplaceQueryBoundary({ children }) {
    return (
        <QueryClientProvider client={marketplaceQueryClient}>
            {children}
        </QueryClientProvider>
    )
}

export function SuperAdminQueryBoundary({ children }) {
    return (
        <QueryClientProvider client={superAdminQueryClient}>
            {children}
        </QueryClientProvider>
    )
}
