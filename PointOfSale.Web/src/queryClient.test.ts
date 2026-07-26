import { describe, expect, it } from 'vitest'
import { marketplaceQueryClient, queryClient, superAdminQueryClient } from './queryClient'

describe('runtime query domain isolation', () => {
    it('uses distinct caches for POS, Service Marketplace, and Super Admin authority', () => {
        expect(queryClient).not.toBe(marketplaceQueryClient)
        expect(queryClient).not.toBe(superAdminQueryClient)
        expect(marketplaceQueryClient).not.toBe(superAdminQueryClient)
    })
})
