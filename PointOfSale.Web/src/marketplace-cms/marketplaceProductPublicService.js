export async function fetchMarketplaceProduct(productId, language) {
    const url = new URL(`/api/read/products/${encodeURIComponent(productId)}`, window.location.origin)
    url.searchParams.set('lang', language)

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
                : 'Marketplace product not found.'

        const error = new Error(message)
        error.status = response.status
        throw error
    }

    return response.json()
}
