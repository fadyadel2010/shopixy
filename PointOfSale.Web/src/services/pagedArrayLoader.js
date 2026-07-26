import api from './api'

export async function fetchAllPagedArray(path, params = {}, options = {}) {
    const pageSize = Math.max(1, Math.min(options.pageSize ?? 100, 100))
    const signal = options.signal
    const items = []
    let page = 1

    while (true) {
        const response = await api.get(path, {
            params: {
                ...params,
                page,
                pageSize,
            },
            signal,
        })

        const batch = Array.isArray(response.data) ? response.data : []
        items.push(...batch)

        const totalPages = Number(response.headers['x-total-pages'] ?? 0)
        const totalCount = Number(response.headers['x-total-count'] ?? 0)

        if (totalPages > 0) {
            if (page >= totalPages) {
                break
            }
        } else if (batch.length === 0 || batch.length < pageSize || (totalCount > 0 && items.length >= totalCount)) {
            break
        }

        page += 1
    }

    return items
}
