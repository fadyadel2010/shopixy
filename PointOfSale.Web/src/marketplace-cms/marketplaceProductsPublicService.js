export async function fetchMarketplaceProducts({
    language,
    search = '',
    category = '',
    page = 1,
    pageSize = 12,
}) {
    const url = new URL('/api/read/products', window.location.origin)
    url.searchParams.set('lang', language)
    url.searchParams.set('page', String(page))
    url.searchParams.set('pageSize', String(pageSize))

    if (search) {
        url.searchParams.set('search', search)
    }

    if (category) {
        url.searchParams.set('category', category)
    }

    const response = await fetch(url.toString(), {
        method: 'GET',
        credentials: 'include',
        headers: {
            'Content-Type': 'application/json',
            'Accept-Language': language,
        },
    })

    if (!response.ok) {
        const payload = await response.json().catch(() => null)
        const message =
            typeof payload?.message === 'string'
                ? payload.message
                : 'Marketplace products could not be loaded.'

        const error = new Error(message)
        error.status = response.status
        throw error
    }

    return response.json()
}
