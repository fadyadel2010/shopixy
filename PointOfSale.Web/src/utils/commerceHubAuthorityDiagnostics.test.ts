import { describe, expect, it } from 'vitest'
import { deriveCommerceHubAuthorityDiagnostic } from './commerceHubAuthorityDiagnostics'

const diagnose = (query: object, presentation: object | null = null) => (
    deriveCommerceHubAuthorityDiagnostic({
        surface: 'Finance dashboard',
        endpoint: '/api/admin/commercehub/finance/dashboard',
        query,
        presentation,
    })
)

describe('CommerceHub authority diagnostics', () => {
    it('distinguishes network failures', () => {
        expect(diagnose({ error: new Error('Network Error') })).toMatchObject({
            failureKind: 'network',
            status: null,
        })
    })

    it('preserves coded backend rejection context', () => {
        expect(diagnose({
            error: {
                response: {
                    status: 400,
                    data: { code: 'TENANT_REQUIRED', traceId: 'trace-1' },
                    headers: { 'x-request-id': 'request-1' },
                },
            },
        })).toMatchObject({
            failureKind: 'backend-coded-rejection',
            status: 400,
            code: 'TENANT_REQUIRED',
            traceId: 'trace-1',
            requestId: 'request-1',
        })
    })

    it('distinguishes auth and session failures', () => {
        expect(diagnose({ error: { response: { status: 401, data: {} } } })).toMatchObject({
            failureKind: 'auth-session',
            status: 401,
        })
    })

    it('identifies response contract rejection without logging the full DTO', () => {
        expect(diagnose({
            status: 'success',
            data: {
                vendorId: 'a6c9e71e-dd48-49cb-947a-6f460423360e',
                debtStatus: 'UNKNOWN_BACKEND_STATUS',
                sensitiveFinancialRows: [{ amount: 100 }],
            },
        })).toEqual({
            surface: 'Finance dashboard',
            endpoint: '/api/admin/commercehub/finance/dashboard',
            failureKind: 'mapping-contract-rejection',
            status: 'success',
            code: null,
            traceId: null,
            requestId: null,
            vendorId: 'a6c9e71e-dd48-49cb-947a-6f460423360e',
            debtStatus: 'UNKNOWN_BACKEND_STATUS',
        })
    })
})
