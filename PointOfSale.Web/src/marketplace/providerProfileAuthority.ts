import type { MarketplaceProfile } from './types'

const nonEmpty = (value: unknown) => {
  const normalized = typeof value === 'string' ? value.trim() : ''
  return normalized || null
}

const safeWebUrl = (value: unknown) => {
  const normalized = nonEmpty(value)
  if (!normalized) return null

  try {
    const url = new URL(normalized)
    return url.protocol === 'http:' || url.protocol === 'https:' ? normalized : null
  } catch {
    return null
  }
}

const parseStringArray = (rawValue: unknown, maxItems: number) => {
  if (!nonEmpty(rawValue)) return []

  try {
    const values = JSON.parse(String(rawValue))
    if (!Array.isArray(values)) return []

    return values
      .slice(0, maxItems)
      .map(nonEmpty)
      .filter((value): value is string => value !== null)
  } catch {
    return []
  }
}

export type ProviderProfilePresentation = Readonly<{
  hasMarketplaceProfile: boolean
  profileId: string | null
  displayName: string | null
  isActive: boolean
  bio: string | null
  profileImageUrl: string | null
  coverImageUrl: string | null
  location: string | null
  skills: readonly string[]
  portfolioLinks: readonly string[]
}>

export const deriveProviderProfilePresentation = (
  profile: MarketplaceProfile | null | undefined,
): ProviderProfilePresentation | null => {
  if (!profile || typeof profile.hasMarketplaceProfile !== 'boolean' || typeof profile.isActive !== 'boolean') {
    return null
  }

  if (!profile.hasMarketplaceProfile) {
    return {
      hasMarketplaceProfile: false,
      profileId: null,
      displayName: null,
      isActive: false,
      bio: null,
      profileImageUrl: null,
      coverImageUrl: null,
      location: null,
      skills: [],
      portfolioLinks: [],
    }
  }

  const profileId = nonEmpty(profile.profileId)
  const displayName = nonEmpty(profile.displayName)
  if (!profileId || !displayName || profile.isActive !== true) {
    return null
  }

  return {
    hasMarketplaceProfile: true,
    profileId,
    displayName,
    isActive: true,
    bio: nonEmpty(profile.bio),
    profileImageUrl: safeWebUrl(profile.profileImageUrl),
    coverImageUrl: safeWebUrl(profile.coverImageUrl),
    location: nonEmpty(profile.location),
    skills: parseStringArray(profile.skills, 30),
    portfolioLinks: parseStringArray(profile.portfolioLinks, 20)
      .map(safeWebUrl)
      .filter((link): link is string => link !== null),
  }
}
