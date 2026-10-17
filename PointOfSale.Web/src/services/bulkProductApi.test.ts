import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const apiPostMock = vi.hoisted(() => vi.fn())

const getSafeApiErrorMessageStub = vi.hoisted(() => vi.fn(
    (error: unknown, fallback = 'Request failed. Please try again.') => {
        const status = (error as { response?: { status?: number } })?.response?.status
        if (typeof status === 'number' && status >= 500) {
            return 'Service is temporarily unavailable. Please try again.'
        }
        if (status === 403) {
            return 'You do not have permission to perform this action.'
        }
        if (status === 400 || status === 409 || status === 422) {
            const backendMessage = (error as { response?: { data?: { message?: unknown } } })
                ?.response?.data?.message
            if (typeof backendMessage === 'string' && backendMessage.trim().length > 0) {
                return backendMessage.trim()
            }
        }
        return fallback
    },
))

vi.mock('./api', () => ({
    default: {
        post: apiPostMock,
    },
    getSafeApiErrorMessage: getSafeApiErrorMessageStub,
}))

type BulkItem = Record<string, unknown>

const transportItem = (productId: string, expectedVersion: string): BulkItem => ({
    productId,
    expectedVersion,
})

const axiosLikeError = ({
    status,
    data,
    configHeaders,
}: {
    status?: number
    data?: unknown
    configHeaders?: Record<string, string>
}) => {
    const error = new Error(
        typeof status === 'number' ? `Request failed with status code ${status}` : 'Network Error',
    ) as Error & {
        isAxiosError?: boolean
        response?: unknown
        config?: unknown
    }

    if (typeof status === 'number') {
        error.response = { status, data, headers: {} }
    }

    error.isAxiosError = true
    error.config = {
        url: '/products/bulk/delete',
        headers: { ...(configHeaders ?? { Authorization: 'Bearer super-secret-token' }) },
    }

    return error
}

const successPayload = () => ({
    requested: 2,
    accepted: 2,
    processed: 1,
    unchanged: 1,
    failed: 0,
    correlationId: 'corr-success-1',
    results: [
        {
            productId: '11111111-1111-1111-1111-111111111111',
            status: 'processed',
            code: null,
            message: null,
            previousVersion: '7',
            newVersion: '8',
        },
        {
            productId: '22222222-2222-2222-2222-222222222222',
            status: 'unchanged',
            code: 'ALREADY_ACTIVE',
            message: 'Product is already active.',
            previousVersion: '3',
            newVersion: '3',
        },
    ],
})

describe('bulkProductApi', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        apiPostMock.mockResolvedValue({ data: successPayload() })
    })

    afterEach(() => {
        vi.unstubAllGlobals()
    })

    describe('routes and request contract', () => {
        it('maps each explicit function to its dedicated bulk route', async () => {
            const {
                bulkActivate,
                bulkDeactivate,
                bulkPublish,
                bulkUnpublish,
                bulkDelete,
            } = await import('./bulkProductApi')

            const items = [transportItem('11111111-1111-1111-1111-111111111111', '7')]

            await bulkActivate(items)
            await bulkDeactivate(items)
            await bulkPublish(items)
            await bulkUnpublish(items)
            await bulkDelete(items)

            expect(apiPostMock.mock.calls.map((call) => call[0])).toEqual([
                '/products/bulk/activate',
                '/products/bulk/deactivate',
                '/products/bulk/publish',
                '/products/bulk/unpublish',
                '/products/bulk/delete',
            ])
        })

        it('sends exactly { items: [{ productId, expectedVersion }] } with no extra fields', async () => {
            const { bulkDelete } = await import('./bulkProductApi')

            await bulkDelete([
                transportItem('11111111-1111-1111-1111-111111111111', '7'),
                transportItem('22222222-2222-2222-2222-222222222222', '3'),
            ])

            expect(apiPostMock).toHaveBeenCalledTimes(1)
            const [route, body, config] = apiPostMock.mock.calls[0]

            expect(route).toBe('/products/bulk/delete')
            expect(body).toEqual({
                items: [
                    { productId: '11111111-1111-1111-1111-111111111111', expectedVersion: '7' },
                    { productId: '22222222-2222-2222-2222-222222222222', expectedVersion: '3' },
                ],
            })
            expect(Object.keys(body as BulkItem)).toEqual(['items'])
            for (const item of (body as { items: BulkItem[] }).items) {
                expect(Object.keys(item).sort()).toEqual(['expectedVersion', 'productId'])
            }
            expect((config as { headers: Record<string, string> }).headers['Idempotency-Key']).toBeTruthy()
            expect((config as { suppressIdempotencyRequestIdMirroring?: boolean })
                .suppressIdempotencyRequestIdMirroring).toBe(true)
            expect((config as { headers: Record<string, string> }).headers['X-Request-Id']).toBeUndefined()
        })

        it('preserves caller order and duplicates instead of collapsing them', async () => {
            const { bulkActivate } = await import('./bulkProductApi')

            const duplicated = [
                transportItem('33333333-3333-3333-3333-333333333333', '7'),
                transportItem('33333333-3333-3333-3333-333333333333', '7'),
            ]

            await bulkActivate(duplicated)

            const body = apiPostMock.mock.calls[0][1] as { items: BulkItem[] }
            expect(body.items).toHaveLength(2)
            expect(body.items).toEqual([
                { productId: '33333333-3333-3333-3333-333333333333', expectedVersion: '7' },
                { productId: '33333333-3333-3333-3333-333333333333', expectedVersion: '7' },
            ])
        })
    })

    describe('idempotency keys', () => {
        it('generates a distinct non-empty key per call when none is supplied', async () => {
            const { bulkPublish } = await import('./bulkProductApi')
            const items = [transportItem('11111111-1111-1111-1111-111111111111', '7')]

            await bulkPublish(items)
            await bulkPublish(items)

            const firstKey = (apiPostMock.mock.calls[0][2] as { headers: Record<string, string> }).headers['Idempotency-Key']
            const secondKey = (apiPostMock.mock.calls[1][2] as { headers: Record<string, string> }).headers['Idempotency-Key']

            expect(typeof firstKey).toBe('string')
            expect(firstKey.length).toBeGreaterThan(0)
            expect(secondKey.length).toBeGreaterThan(0)
            expect(secondKey).not.toBe(firstKey)
        })

        it('sends an explicitly supplied key unchanged on every call', async () => {
            const { bulkUnpublish } = await import('./bulkProductApi')
            const items = [transportItem('11111111-1111-1111-1111-111111111111', '7')]

            await bulkUnpublish(items, 'client-supplied-key_01')
            await bulkUnpublish(items, 'client-supplied-key_01')

            for (const call of apiPostMock.mock.calls) {
                const config = call[2] as { headers: Record<string, string> }
                expect(config.headers['Idempotency-Key']).toBe('client-supplied-key_01')
                const body = call[1] as BulkItem
                expect(Object.keys(body)).toEqual(['items'])
            }
        })

        it('rejects before HTTP when secure UUID generation is unavailable', async () => {
            const { bulkActivate } = await import('./bulkProductApi')

            vi.stubGlobal('crypto', {})

            await expect(bulkActivate([transportItem('11111111-1111-1111-1111-111111111111', '7')]))
                .rejects.toMatchObject({ code: 'IDEMPOTENCY_KEY_GENERATION_UNAVAILABLE' })

            expect(apiPostMock).not.toHaveBeenCalled()
        })

        it.each([null, '', '   ', 123])(
            'rejects an explicitly supplied invalid key instead of replacing it (%s)',
            async (invalidKey) => {
                const { bulkActivate } = await import('./bulkProductApi')

                await expect(bulkActivate(
                    [transportItem('11111111-1111-1111-1111-111111111111', '7')],
                    invalidKey as unknown as string,
                )).rejects.toMatchObject({ code: 'INVALID_IDEMPOTENCY_KEY' })

                expect(apiPostMock).not.toHaveBeenCalled()
            },
        )
    })

    describe('product DTO mapper', () => {
        it('maps the Products DTO id/rowVersion to the transport item', async () => {
            const { toBulkProductItem } = await import('./bulkProductApi')

            expect(toBulkProductItem({
                id: '44444444-4444-4444-4444-444444444444',
                rowVersion: '9',
            })).toEqual({
                productId: '44444444-4444-4444-4444-444444444444',
                expectedVersion: '9',
            })
        })

        it('preserves the string version representation exactly without numeric conversion', async () => {
            const { toBulkProductItem } = await import('./bulkProductApi')

            const mapped = toBulkProductItem({
                id: '44444444-4444-4444-4444-444444444444',
                rowVersion: '007',
            })

            expect(mapped.expectedVersion).toBe('007')
        })

        it('rejects products missing id or rowVersion before any HTTP call', async () => {
            const { toBulkProductItem } = await import('./bulkProductApi')

            expect(() => toBulkProductItem(null as unknown as BulkItem)).toThrow()
            expect(() => toBulkProductItem({ rowVersion: '7' })).toThrow()
            expect(() => toBulkProductItem({ id: '44444444-4444-4444-4444-444444444444' })).toThrow()
            expect(() => toBulkProductItem({
                id: '44444444-4444-4444-4444-444444444444',
                rowVersion: 7,
            })).toThrow()
            expect(apiPostMock).not.toHaveBeenCalled()
        })
    })

    describe('success normalization', () => {
        it('accepts and canonicalizes the live backend PascalCase item statuses', async () => {
            const { bulkActivate } = await import('./bulkProductApi')
            const payload = successPayload()
            payload.results[0].status = 'Processed'
            payload.results[1].status = 'Unchanged'
            apiPostMock.mockResolvedValueOnce({ data: payload })

            const result = await bulkActivate([
                transportItem('11111111-1111-1111-1111-111111111111', '7'),
            ])

            expect(result.results.map((item: { status: string }) => item.status))
                .toEqual(['processed', 'unchanged'])
        })

        it('preserves aggregate fields and all certified per-item fields', async () => {
            const { bulkActivate } = await import('./bulkProductApi')
            const payload = successPayload()

            apiPostMock.mockResolvedValueOnce({ data: payload })

            const result = await bulkActivate([
                transportItem('11111111-1111-1111-1111-111111111111', '7'),
            ])

            expect(Object.keys(result).sort()).toEqual([
                'accepted',
                'correlationId',
                'failed',
                'processed',
                'requested',
                'results',
                'unchanged',
            ])
            expect(result.requested).toBe(payload.requested)
            expect(result.accepted).toBe(payload.accepted)
            expect(result.processed).toBe(payload.processed)
            expect(result.unchanged).toBe(payload.unchanged)
            expect(result.failed).toBe(payload.failed)
            expect(result.correlationId).toBe('corr-success-1')
            expect(result.results).toEqual(payload.results)

            const [firstItem] = result.results
            expect(firstItem.productId).toBe(payload.results[0].productId)
            expect(firstItem.status).toBe('processed')
            expect(firstItem.previousVersion).toBe('7')
            expect(firstItem.newVersion).toBe('8')
            expect(result.results[1].code).toBe('ALREADY_ACTIVE')
            expect(result.results[1].message).toBe('Product is already active.')
        })

        it('rejects a malformed success payload instead of returning a false all-zero success', async () => {
            const { bulkDeactivate } = await import('./bulkProductApi')

            apiPostMock.mockResolvedValueOnce({ data: undefined })

            await expect(bulkDeactivate([
                transportItem('11111111-1111-1111-1111-111111111111', '7'),
            ])).rejects.toMatchObject({ code: 'INVALID_BULK_RESPONSE' })
        })

        it.each([
            ['non-numeric counts', { ...successPayload(), processed: '1' }],
            ['inconsistent aggregate counts', { ...successPayload(), failed: 1 }],
            ['blank correlation id', { ...successPayload(), correlationId: '   ' }],
            ['result count mismatch', { ...successPayload(), results: successPayload().results.slice(0, 1) }],
            ['unknown item status', {
                ...successPayload(),
                results: [
                    { ...successPayload().results[0], status: 'mystery' },
                    successPayload().results[1],
                ],
            }],
            ['non-string version', {
                ...successPayload(),
                results: [
                    { ...successPayload().results[0], newVersion: 8 },
                    successPayload().results[1],
                ],
            }],
        ])('rejects malformed success contracts (%s)', async (_label, payload) => {
            const { bulkActivate } = await import('./bulkProductApi')
            apiPostMock.mockResolvedValueOnce({ data: payload })

            await expect(bulkActivate([
                transportItem('11111111-1111-1111-1111-111111111111', '7'),
            ])).rejects.toMatchObject({ code: 'INVALID_BULK_RESPONSE' })
        })
    })

    describe('error normalization', () => {
        it('normalizes 400 validation errors while preserving safe message, code, correlation, and details', async () => {
            const { bulkDelete } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 400,
                data: {
                    message: 'Two items failed validation.',
                    code: 'VALIDATION_FAILED',
                    correlationId: 'corr-400',
                    errors: [
                        {
                            code: 'MALFORMED_PRODUCT_ID',
                            field: 'items[0].productId',
                            message: 'Invalid product identifier.',
                        },
                    ],
                },
            }))

            const error = await bulkDelete([transportItem('not-a-guid', '7')]).catch(
                (caught: unknown) => caught,
            )

            expect(error).toBeInstanceOf(Error)
            expect(error).toMatchObject({
                status: 400,
                code: 'VALIDATION_FAILED',
                correlationId: 'corr-400',
                message: 'Two items failed validation.',
            })
            expect((error as { details: Array<{ code: string }> }).details[0].code)
                .toBe('MALFORMED_PRODUCT_ID')
            expect((error as { details: unknown }).details).toEqual([
                {
                    code: 'MALFORMED_PRODUCT_ID',
                    field: 'items[0].productId',
                    message: 'Invalid product identifier.',
                },
            ])
        })

        it('normalizes 403 authorization errors preserving the safe backend message and code', async () => {
            const { bulkDelete } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 403,
                data: {
                    message: 'Only owners and admins may delete products in bulk.',
                    code: 'FORBIDDEN',
                    correlationId: 'corr-403',
                },
            }))

            const error = await bulkDelete([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)

            expect(error).toMatchObject({
                status: 403,
                code: 'FORBIDDEN',
                correlationId: 'corr-403',
                message: 'Only owners and admins may delete products in bulk.',
            })
        })

        it('normalizes 409 idempotency/concurrency conflicts preserving backend details', async () => {
            const { bulkActivate } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 409,
                data: {
                    message: 'Bulk request was already completed with a different payload.',
                    code: 'IDEMPOTENCY_CONFLICT',
                    correlationId: 'corr-409',
                    details: 'original-key-fingerprint-mismatch',
                },
            }))

            const error = await bulkActivate([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)

            expect(error).toMatchObject({
                status: 409,
                code: 'IDEMPOTENCY_CONFLICT',
                correlationId: 'corr-409',
                details: 'original-key-fingerprint-mismatch',
                message: 'Bulk request was already completed with a different payload.',
            })
        })

        it('hides backend internals from 500 responses but preserves status and machine code', async () => {
            const { bulkActivate } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 500,
                data: {
                    message: 'System.NullReferenceException: Object reference not set at Foo.Bar() select * from Products',
                    code: 'INTERNAL_SERVER_ERROR',
                    correlationId: 'corr-500',
                    details: { stack: 'at Foo.Bar()' },
                },
            }))

            const error = await bulkActivate([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)

            expect(error).toMatchObject({
                status: 500,
                code: 'INTERNAL_SERVER_ERROR',
                correlationId: 'corr-500',
                message: 'Service is temporarily unavailable. Please try again.',
                details: null,
            })
            expect((error as Error).message).not.toContain('Exception')
            expect((error as Error).message).not.toContain('select * from')
        })

        it('normalizes 503 unavailability with the safe server message', async () => {
            const { bulkUnpublish } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 503,
                data: {
                    message: 'upstream connection reset; retry storm suspected at System.Net sockets layer',
                    errorCode: 'SERVICE_UNAVAILABLE',
                },
            }))

            const error = await bulkUnpublish([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)

            expect(error).toMatchObject({
                status: 503,
                code: 'SERVICE_UNAVAILABLE',
                message: 'Service is temporarily unavailable. Please try again.',
                details: null,
            })
        })

        it('recognizes common backend error-code variants generically', async () => {
            const { bulkPublish } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 400,
                data: { errorCode: 'TOP_LEVEL_ERROR_CODE' },
            }))
            const viaErrorCode = await bulkPublish([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)
            expect((viaErrorCode as { code: string }).code).toBe('TOP_LEVEL_ERROR_CODE')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 400,
                data: { error: { code: 'NESTED_ERROR_CODE' } },
            }))
            const viaNestedError = await bulkPublish([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)
            expect((viaNestedError as { code: string }).code).toBe('NESTED_ERROR_CODE')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 400,
                data: {
                    errors: [{ code: 'FIRST_VALIDATION_CODE', field: 'items[0]', message: 'bad' }],
                },
            }))
            const viaValidationErrors = await bulkPublish([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)
            expect((viaValidationErrors as { code: string }).code).toBe('FIRST_VALIDATION_CODE')
        })

        it('preserves unknown future server codes without a closed code list', async () => {
            const { bulkActivate } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 409,
                data: {
                    message: 'A future conflict occurred.',
                    code: 'FUTURE_SERVER_CODE',
                    correlationId: 'corr-future',
                },
            }))

            const error = await bulkActivate([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)

            expect(error).toMatchObject({
                status: 409,
                code: 'FUTURE_SERVER_CODE',
                correlationId: 'corr-future',
                message: 'A future conflict occurred.',
            })
        })

        it('drops unsafe backend detail content from non-5xx errors', async () => {
            const { bulkActivate } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 409,
                data: {
                    message: 'A conflict occurred.',
                    code: 'CONCURRENCY_CONFLICT',
                    details: { diagnostic: 'System.Exception at ProductService.Update() SQL SELECT * FROM Products' },
                },
            }))

            const error = await bulkActivate([
                transportItem('11111111-1111-1111-1111-111111111111', '7'),
            ]).catch((caught: unknown) => caught)

            expect(error).toMatchObject({
                status: 409,
                code: 'CONCURRENCY_CONFLICT',
                details: null,
            })
            expect(JSON.stringify(error)).not.toContain('ProductService.Update')
        })

        it('never serializes transport internals such as auth tokens into the normalized error', async () => {
            const { bulkDelete, BulkProductApiError } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(axiosLikeError({
                status: 500,
                data: { message: 'System.Exception boom' },
            }))

            const error = await bulkDelete([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)

            expect(error).toBeInstanceOf(BulkProductApiError)
            const serialized = JSON.stringify(error)
            expect(serialized).not.toContain('super-secret-token')
            expect(serialized).not.toContain('isAxiosError')
            expect(Object.prototype.propertyIsEnumerable.call(error, 'cause')).toBe(false)
        })
    })

    describe('transport failures', () => {
        it('propagates network failure as a rejected application error after exactly one call', async () => {
            const { bulkActivate } = await import('./bulkProductApi')

            apiPostMock.mockRejectedValueOnce(new Error('Network Error'))

            const error = await bulkActivate([transportItem('11111111-1111-1111-1111-111111111111', '7')])
                .catch((caught: unknown) => caught)

            expect(error).toBeInstanceOf(Error)
            expect(error).toMatchObject({
                status: null,
                code: null,
                correlationId: null,
                message: 'Request failed. Please try again.',
            })
            expect(apiPostMock).toHaveBeenCalledTimes(1)
        })
    })

    describe('pre-flight validation', () => {
        it('rejects empty or non-array item collections before HTTP', async () => {
            const { bulkActivate } = await import('./bulkProductApi')

            await expect(bulkActivate([])).rejects.toThrow('non-empty array')
            await expect(bulkActivate(null as unknown as BulkItem[])).rejects.toThrow(Error)
            await expect(bulkActivate('nope' as unknown as BulkItem[])).rejects.toThrow(Error)

            expect(apiPostMock).not.toHaveBeenCalled()
        })

        it.each([
            ['missing productId', [{ expectedVersion: '7' }]],
            ['blank productId', [{ productId: '   ', expectedVersion: '7' }]],
            ['null productId', [{ productId: null, expectedVersion: '7' }]],
            ['non-string productId', [{ productId: 7, expectedVersion: '7' }]],
            ['missing expectedVersion', [{ productId: '11111111-1111-1111-1111-111111111111' }]],
            ['null expectedVersion', [{ productId: '11111111-1111-1111-1111-111111111111', expectedVersion: null }]],
            ['undefined expectedVersion', [{ productId: '11111111-1111-1111-1111-111111111111', expectedVersion: undefined }]],
            ['blank expectedVersion', [{ productId: '11111111-1111-1111-1111-111111111111', expectedVersion: '   ' }]],
            ['non-string expectedVersion', [{ productId: '11111111-1111-1111-1111-111111111111', expectedVersion: 7 }]],
            ['non-object item', ['11111111-1111-1111-1111-111111111111']],
        ])('rejects malformed items (%s) before HTTP', async (_label, items) => {
            const { bulkDeactivate } = await import('./bulkProductApi')

            await expect(bulkDeactivate(items as BulkItem[])).rejects.toBeInstanceOf(Error)
            expect(apiPostMock).not.toHaveBeenCalled()
        })
    })

    describe('module surface', () => {
        it('exposes only explicit bulk functions, the mapper, and the error class', async () => {
            const moduleExports = await import('./bulkProductApi')

            expect(Object.keys(moduleExports).sort()).toEqual([
                'BulkProductApiError',
                'bulkActivate',
                'bulkDeactivate',
                'bulkDelete',
                'bulkPublish',
                'bulkUnpublish',
                'default',
                'toBulkProductItem',
            ])
        })
    })

    describe('side effects', () => {
        it('performs no storage or console logging side effects during calls', async () => {
            const storageSpies = [
                vi.spyOn(Storage.prototype, 'setItem'),
                vi.spyOn(Storage.prototype, 'removeItem'),
                vi.spyOn(Storage.prototype, 'clear'),
            ]
            const consoleSpies = [
                vi.spyOn(console, 'log').mockImplementation(() => {}),
                vi.spyOn(console, 'info').mockImplementation(() => {}),
                vi.spyOn(console, 'warn').mockImplementation(() => {}),
                vi.spyOn(console, 'error').mockImplementation(() => {}),
                vi.spyOn(console, 'debug').mockImplementation(() => {}),
            ]

            try {
                const { bulkPublish } = await import('./bulkProductApi')
                const items = [transportItem('11111111-1111-1111-1111-111111111111', '7')]

                await bulkPublish(items)
                apiPostMock.mockRejectedValueOnce(axiosLikeError({ status: 409, data: { message: 'conflict' } }))
                await bulkPublish(items).catch(() => {})

                for (const spy of [...storageSpies, ...consoleSpies]) {
                    expect(spy).not.toHaveBeenCalled()
                }
            } finally {
                for (const spy of [...storageSpies, ...consoleSpies]) {
                    spy.mockRestore()
                }
            }
        })
    })
})
