import type { MyService } from './types'

const pricingTypes = new Set(['Fixed', 'Hourly', 'Package'])
const serviceMediaPrefix = 'marketplace/services/'

const nonEmpty = (value: unknown) => {
  const normalized = typeof value === 'string' ? value.trim() : ''
  return normalized || null
}

const finiteNumber = (value: unknown) => {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

const parsePrimaryImage = (rawImages: unknown) => {
  if (!nonEmpty(rawImages)) return null

  try {
    const images = JSON.parse(String(rawImages))
    if (!Array.isArray(images) || typeof images[0] !== 'string') return null

    const storageKey = images[0].trim().replaceAll('\\', '/')
    if (
      !storageKey.startsWith(serviceMediaPrefix)
      || storageKey.includes('..')
      || storageKey.includes('?')
      || storageKey.includes('#')
    ) {
      return null
    }

    return `/${storageKey.split('/').map(encodeURIComponent).join('/')}`
  } catch {
    return null
  }
}

export type ProviderServicePresentation = Readonly<{
  serviceId: string
  title: string
  description: string
  category: string | null
  isActive: boolean
  startingPrice: number | null
  pricingType: 'Fixed' | 'Hourly' | 'Package' | null
  currency: string | null
  primaryImage: string | null
  source: MyService
}>

export const deriveProviderServicePresentation = (
  service: MyService,
): ProviderServicePresentation | null => {
  const serviceId = nonEmpty(service.serviceId)
  const title = nonEmpty(service.title)
  const description = nonEmpty(service.description)

  if (!serviceId || !title || !description || typeof service.isActive !== 'boolean') {
    return null
  }

  const startingPrice = service.startingPrice == null
    ? null
    : finiteNumber(service.startingPrice)
  const currency = nonEmpty(service.currency)?.toUpperCase() ?? null
  const pricingType = pricingTypes.has(service.pricingType ?? '')
    ? service.pricingType as ProviderServicePresentation['pricingType']
    : null
  const hasValidPricing = startingPrice !== null
    && startingPrice >= 0
    && currency !== null
    && /^[A-Z]{3}$/.test(currency)
    && pricingType !== null

  return {
    serviceId,
    title,
    description,
    category: nonEmpty(service.category),
    isActive: service.isActive,
    startingPrice: hasValidPricing ? startingPrice : null,
    pricingType: hasValidPricing ? pricingType : null,
    currency: hasValidPricing ? currency : null,
    primaryImage: parsePrimaryImage(service.serviceImages),
    source: service,
  }
}
