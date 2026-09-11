export function deriveCommerceHubAuthorityDiagnostic({
    surface,
    endpoint,
    query,
    presentation,
}) {
    if (query?.isFetching) {
        return null
    }

    const error = query?.error
    if (error) {
        const status = error?.response?.status ?? null
        const code = error?.response?.data?.code ?? null
        const failureKind = !error?.response
            ? 'network'
            : status === 401 || status === 403
                ? 'auth-session'
                : code
                    ? 'backend-coded-rejection'
                    : 'api-response-failure'

        return {
            surface,
            endpoint,
            failureKind,
            status,
            code,
            traceId: error?.response?.data?.traceId ?? null,
            requestId: error?.response?.headers?.['x-request-id'] ?? null,
        }
    }

    if ((query?.isSuccess || query?.status === 'success') && presentation == null) {
        return {
            surface,
            endpoint,
            failureKind: 'mapping-contract-rejection',
            status: query?.status ?? null,
            code: null,
            traceId: null,
            requestId: null,
            vendorId: query.data?.vendorId ?? null,
            debtStatus: query.data?.debtStatus ?? null,
        }
    }

    return null
}
