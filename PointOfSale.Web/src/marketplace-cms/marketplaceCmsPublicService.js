export async function fetchMarketplaceCmsPage(language, slug, preview = false, previewToken = null) {
    const url = new URL(`/api/marketplace/pages/${encodeURIComponent(slug)}`, window.location.origin)
    url.searchParams.set('lang', language)

    if (preview) {
        url.searchParams.set('preview', 'true')
        if (previewToken) {
            url.searchParams.set('previewToken', previewToken)
        }
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
                : 'Marketplace page not found.'

        const error = new Error(message)
        error.status = response.status
        throw error
    }

    return response.json()
}
