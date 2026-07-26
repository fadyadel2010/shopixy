export async function fetchMarketplaceFooter(language) {
    const url = new URL('/api/marketplace/footer', window.location.origin)
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
        throw new Error('Unable to load the marketplace footer.')
    }

    return response.json()
}
