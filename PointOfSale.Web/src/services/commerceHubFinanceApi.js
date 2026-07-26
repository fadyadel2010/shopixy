import api from './api'

export const COMMERCEHUB_FINANCE_TRANSACTION_TYPES = [
    'OrderPayment',
    'Commission',
    'Refund',
    'Payout',
    'Adjustment',
    'Settlement',
]

const DEFAULT_PAGE_SIZE = 25

function appendDate(params, key, value, endOfDay = false) {
    if (!value) {
        return
    }

    const suffix = endOfDay ? 'T23:59:59.999Z' : 'T00:00:00.000Z'
    params.set(key, `${value}${suffix}`)
}

export function buildStatementParams({
    page = 1,
    pageSize = DEFAULT_PAGE_SIZE,
    fromDate,
    toDate,
    transactionTypes = [],
}) {
    const params = new URLSearchParams()
    params.set('page', String(page))
    params.set('size', String(pageSize))

    appendDate(params, 'fromDate', fromDate)
    appendDate(params, 'toDate', toDate, true)

    transactionTypes.forEach((type) => {
        params.append('transactionTypes', type)
    })

    return params
}

export async function getCommerceHubFinanceDashboard() {
    const response = await api.get('/admin/commercehub/finance/dashboard')
    return response.data
}

export async function getCommerceHubFinanceStatement({
    page = 1,
    pageSize = DEFAULT_PAGE_SIZE,
    fromDate,
    toDate,
    transactionTypes = [],
}) {
    const params = buildStatementParams({
        page,
        pageSize,
        fromDate,
        toDate,
        transactionTypes,
    })
    const response = await api.get(`/admin/commercehub/finance/statement?${params.toString()}`)
    return response.data
}

export async function downloadCommerceHubFinanceStatementCsv({
    fromDate,
    toDate,
    transactionTypes = [],
}) {
    const params = buildStatementParams({
        page: 1,
        pageSize: DEFAULT_PAGE_SIZE,
        fromDate,
        toDate,
        transactionTypes,
    })

    const response = await api.get(`/commercehub/finance/vendor/export?${params.toString()}`, {
        responseType: 'blob',
    })

    return response.data
}
