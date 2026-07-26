import type {
    PaymentProviderDashboardProvider,
    ProviderDashboardFiltersState,
} from './paymentProviderTypes'

export const defaultProviderDashboardFilters: ProviderDashboardFiltersState = {
    provider: '',
    environment: '',
    status: '',
    health: '',
    enabled: 'all',
    capability: '',
    search: '',
    sort: 'provider',
}

export function filterDashboardProviders(
    providers: PaymentProviderDashboardProvider[],
    filters: ProviderDashboardFiltersState,
) {
    const search = filters.search.trim().toLowerCase()

    return providers
        .filter((provider) => !filters.provider || provider.providerCode === filters.provider)
        .filter((provider) => !filters.environment || provider.environment === filters.environment)
        .filter((provider) => !filters.status || provider.status === filters.status)
        .filter((provider) => !filters.health || provider.health.status === filters.health)
        .filter((provider) => {
            if (filters.enabled === 'enabled') {
                return provider.enabled
            }

            if (filters.enabled === 'disabled') {
                return !provider.enabled
            }

            return true
        })
        .filter((provider) => !filters.capability || provider.capabilities.some((capability) => capability.code === filters.capability))
        .filter((provider) => {
            if (!search) {
                return true
            }

            return [
                provider.providerCode,
                provider.displayName,
                provider.description,
                provider.status,
                provider.environment,
                provider.health.status,
                provider.version ?? '',
                ...provider.capabilities.map((capability) => capability.displayName),
            ].some((value) => value.toLowerCase().includes(search))
        })
        .sort((left, right) => compareDashboardProviders(left, right, filters.sort))
}

function compareDashboardProviders(
    left: PaymentProviderDashboardProvider,
    right: PaymentProviderDashboardProvider,
    sort: ProviderDashboardFiltersState['sort'],
) {
    if (sort === 'status') {
        return left.status.localeCompare(right.status) || left.displayName.localeCompare(right.displayName)
    }

    if (sort === 'environment') {
        return left.environment.localeCompare(right.environment) || left.displayName.localeCompare(right.displayName)
    }

    if (sort === 'health') {
        return left.health.status.localeCompare(right.health.status) || left.displayName.localeCompare(right.displayName)
    }

    if (sort === 'lastCheck') {
        return getTime(right.lastCheckUtc) - getTime(left.lastCheckUtc) || left.displayName.localeCompare(right.displayName)
    }

    return left.displayName.localeCompare(right.displayName)
}

function getTime(value?: string | null) {
    if (!value) {
        return 0
    }

    const parsed = new Date(value).getTime()
    return Number.isNaN(parsed) ? 0 : parsed
}
