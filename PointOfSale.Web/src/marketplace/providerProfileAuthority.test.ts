import { describe, expect, it } from 'vitest'
import { deriveProviderProfilePresentation } from './providerProfileAuthority'
import type { MarketplaceProfile } from './types'

const profile = (overrides: Partial<MarketplaceProfile> = {}): MarketplaceProfile => ({
  hasMarketplaceProfile: true,
  profileId: '33333333-3333-4333-8333-333333333333',
  displayName: 'Provider One',
  isActive: true,
  bio: 'Backend profile bio',
  profileImageUrl: 'https://cdn.example.test/profile.png',
  coverImageUrl: 'https://cdn.example.test/cover.png',
  location: 'Cairo',
  skills: '["SEO","Design"]',
  portfolioLinks: '["https://example.test/work","javascript:alert(1)"]',
  ...overrides,
})

describe('provider profile presentation authority', () => {
  it('accepts only current backend profile identity and safe presentation fields', () => {
    expect(deriveProviderProfilePresentation(profile())).toEqual(expect.objectContaining({
      hasMarketplaceProfile: true,
      profileId: '33333333-3333-4333-8333-333333333333',
      displayName: 'Provider One',
      profileImageUrl: 'https://cdn.example.test/profile.png',
      coverImageUrl: 'https://cdn.example.test/cover.png',
      skills: ['SEO', 'Design'],
      portfolioLinks: ['https://example.test/work'],
    }))
  })

  it('represents an explicit backend no-profile response without fabricating identity', () => {
    expect(deriveProviderProfilePresentation(profile({
      hasMarketplaceProfile: false,
      profileId: undefined,
      displayName: undefined,
      isActive: false,
    }))).toEqual(expect.objectContaining({
      hasMarketplaceProfile: false,
      profileId: null,
      displayName: null,
      skills: [],
      portfolioLinks: [],
    }))
  })

  it('fails closed for missing active marketplace profile identity', () => {
    expect(deriveProviderProfilePresentation(profile({ profileId: ' ' }))).toBeNull()
    expect(deriveProviderProfilePresentation(profile({ displayName: '' }))).toBeNull()
    expect(deriveProviderProfilePresentation(profile({ isActive: false }))).toBeNull()
  })

  it('suppresses malformed arrays and unsafe media or portfolio URLs', () => {
    expect(deriveProviderProfilePresentation(profile({
      profileImageUrl: 'javascript:alert(1)',
      coverImageUrl: 'data:text/html,unsafe',
      skills: '{not-json}',
      portfolioLinks: '["ftp://example.test/file"]',
    }))).toEqual(expect.objectContaining({
      profileImageUrl: null,
      coverImageUrl: null,
      skills: [],
      portfolioLinks: [],
    }))
  })
})
