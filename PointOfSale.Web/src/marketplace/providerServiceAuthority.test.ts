import { describe, expect, it } from 'vitest'
import { deriveProviderServicePresentation } from './providerServiceAuthority'
import type { MyService } from './types'

const service = (overrides: Partial<MyService> = {}): MyService => ({
  serviceId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  title: 'Provider service',
  description: 'Current backend description',
  category: 'Development',
  isActive: true,
  createdAt: '2026-06-15T00:00:00Z',
  startingPrice: 0,
  pricingType: 'Fixed',
  currency: 'usd',
  serviceImages: '["marketplace/services/provider/card.png"]',
  ...overrides,
})

describe('provider service presentation authority', () => {
  it('renders provider-owned services through bounded backend presentation', () => {
    expect(deriveProviderServicePresentation(service())).toEqual(expect.objectContaining({
      serviceId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      title: 'Provider service',
      description: 'Current backend description',
      isActive: true,
      startingPrice: 0,
      pricingType: 'Fixed',
      currency: 'USD',
      primaryImage: '/marketplace/services/provider/card.png',
    }))
  })

  it('fails closed for malformed required identity or lifecycle fields', () => {
    expect(deriveProviderServicePresentation(service({ serviceId: ' ' }))).toBeNull()
    expect(deriveProviderServicePresentation(service({ title: '' }))).toBeNull()
    expect(deriveProviderServicePresentation(service({ description: '' }))).toBeNull()
    expect(deriveProviderServicePresentation(service({ isActive: 'true' as unknown as boolean }))).toBeNull()
  })

  it('suppresses malformed pricing and replayed media keys without repairing truth', () => {
    expect(deriveProviderServicePresentation(service({
      startingPrice: -1,
      currency: 'not-currency',
      serviceImages: '["https://attacker.example.test/card.png"]',
    }))).toEqual(expect.objectContaining({
      startingPrice: null,
      pricingType: null,
      currency: null,
      primaryImage: null,
    }))

    expect(deriveProviderServicePresentation(service({
      serviceImages: '["marketplace/services/../secret.png"]',
    }))?.primaryImage).toBeNull()
  })
})
