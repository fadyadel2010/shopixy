import type { ServiceRequest, ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'

const pricingTypes = new Set(['Fixed', 'Hourly', 'Package'])
const contractVisibleStatuses = new Set(['accepted', 'completedByProvider', 'completedByBuyer'])

export type ServiceRequestContractPresentation = Readonly<{
    requestId: string
    serviceId: string
    title: string
    category: string | null
    scope: string | null
    price: number | null
    pricingType: 'Fixed' | 'Hourly' | 'Package' | null
    currency: string | null
    packages: readonly ServiceRequestContractPackagePresentation[]
}>

export type ServiceRequestContractPackagePresentation = Readonly<{
    name: string
    description: string | null
    price: number | null
    deliveryDays: number | null
    revisions: number | null
}>

type ContractSource = Pick<
    ServiceRequest | ServiceRequestDetails,
    'requestId' | 'serviceId' | 'serviceTitle' | 'serviceCategory' | 'requestMessage' | 'startingPrice' | 'pricingType' | 'currency' | 'packages'
>

const nonEmpty = (value: unknown) => {
    if (typeof value !== 'string') return null
    const trimmed = value.trim()
    return trimmed.length > 0 ? trimmed : null
}

const validMoney = (value: unknown): value is number => (
    typeof value === 'number'
    && Number.isFinite(value)
    && value >= 0
    && value <= 1_000_000_000
)

const validWholeNumber = (value: unknown): value is number => (
    typeof value === 'number'
    && Number.isInteger(value)
    && value >= 0
    && value <= 100_000
)

function derivePackages(packagesJson: unknown): readonly ServiceRequestContractPackagePresentation[] {
    const rawPackages = nonEmpty(packagesJson)
    if (!rawPackages) return []

    try {
        const parsed = JSON.parse(rawPackages)
        if (!Array.isArray(parsed)) return []

        return parsed.slice(0, 10).flatMap((entry): ServiceRequestContractPackagePresentation[] => {
            if (!entry || typeof entry !== 'object') return []
            const value = entry as Record<string, unknown>
            const name = nonEmpty(value.name ?? value.title)
            if (!name) return []

            return [{
                name,
                description: nonEmpty(value.description),
                price: validMoney(value.price) ? value.price : null,
                deliveryDays: validWholeNumber(value.deliveryDays) ? value.deliveryDays : null,
                revisions: validWholeNumber(value.revisions) ? value.revisions : null,
            }]
        })
    } catch {
        return []
    }
}

export function deriveServiceRequestContractPresentation(
    request: ContractSource,
    lifecycle: ServiceRequestLifecyclePresentation | null,
): ServiceRequestContractPresentation | null {
    if (!lifecycle || !contractVisibleStatuses.has(lifecycle.statusKey)) {
        return null
    }

    const requestId = nonEmpty(request.requestId)
    const serviceId = nonEmpty(request.serviceId)
    const title = nonEmpty(request.serviceTitle)
    if (!requestId || !serviceId || !title) {
        return null
    }

    const normalizedCurrency = nonEmpty(request.currency)?.toUpperCase() ?? null
    const pricingType = pricingTypes.has(request.pricingType ?? '')
        ? request.pricingType as ServiceRequestContractPresentation['pricingType']
        : null
    const hasPrice = validMoney(request.startingPrice)
    const hasCompletePricing = hasPrice && pricingType !== null && normalizedCurrency !== null && /^[A-Z]{3}$/.test(normalizedCurrency)

    return {
        requestId,
        serviceId,
        title,
        category: nonEmpty(request.serviceCategory),
        scope: nonEmpty(request.requestMessage),
        price: hasCompletePricing ? request.startingPrice as number : null,
        pricingType: hasCompletePricing ? pricingType : null,
        currency: hasCompletePricing ? normalizedCurrency : null,
        packages: derivePackages(request.packages),
    }
}
