import api from './api'

const normalizeLookupResponse = (data, page, pageSize) => {
    const items = Array.isArray(data?.items)
        ? data.items
        : Array.isArray(data)
            ? data
            : []

    const totalPages = Number.isFinite(data?.totalPages) ? data.totalPages : null
    const totalCount = Number.isFinite(data?.totalCount) ? data.totalCount : null
    const hasMore = totalPages != null
        ? page < totalPages
        : totalCount != null
            ? page * pageSize < totalCount
            : items.length === pageSize

    return {
        items,
        hasMore,
    }
}

const searchEntity = async (endpoint, { search = '', page = 1, pageSize = 20, ...extraParams } = {}) => {
    const response = await api.get(endpoint, {
        params: {
            page,
            pageSize,
            ...(search.trim() ? { search: search.trim() } : {}),
            ...extraParams,
        },
    })

    return normalizeLookupResponse(response.data, page, pageSize)
}

export const searchCustomers = (options) => searchEntity('/customers', options)

export const searchSuppliers = (options) => searchEntity('/suppliers', options)

export const searchSalesRepresentatives = (options) => searchEntity('/salesrepresentatives', options)
