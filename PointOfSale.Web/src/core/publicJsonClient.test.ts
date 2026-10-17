import { afterEach, expect, it, vi } from 'vitest'
import { publicJsonClient } from './publicJsonClient'

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers() })

it('uses GET-only anonymous requests with encoded parameters and no authorization or cookies', async () => {
    const transport = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [] }), { status: 200 }))
    vi.stubGlobal('fetch', transport)
    await expect(publicJsonClient.get('/api/affiliate/discover/public-products', { timeout: 15_000, params: { search: 'a & b', page: 2, absent: undefined } })).resolves.toEqual({ data: { items: [] } })
    expect(transport).toHaveBeenCalledTimes(1)
    const [url, options] = transport.mock.calls[0]
    expect(url).toBe('/api/affiliate/discover/public-products?search=a+%26+b&page=2')
    expect(options).toMatchObject({ method: 'GET', credentials: 'omit' })
    expect(options).not.toHaveProperty('headers')
})

it('preserves an HTTP 401 failure without retry or refresh, even for a non-JSON error body', async () => {
    const transport = vi.fn().mockResolvedValue(new Response('<html>denied</html>', { status: 401 }))
    vi.stubGlobal('fetch', transport)
    await expect(publicJsonClient.get('/api/affiliate/discover/public-products', { timeout: 15_000 })).rejects.toMatchObject({ response: { status: 401 }, code: 'PUBLIC_HTTP_FAILURE' })
    expect(transport).toHaveBeenCalledTimes(1)
})

it('enforces the original finite overall public discovery budget and aborts transport without retries', async () => {
    vi.useFakeTimers()
    const transport = vi.fn<typeof fetch>(() => new Promise<Response>(() => undefined))
    vi.stubGlobal('fetch', transport)
    const pending = publicJsonClient.get('/api/affiliate/discover/public-products', { timeout: 15_000 })
    const result = expect(pending).rejects.toMatchObject({ code: 'ECONNABORTED' })
    await vi.advanceTimersByTimeAsync(15_000); await result
    expect(transport).toHaveBeenCalledTimes(1)
    expect(transport.mock.calls[0][1]?.signal?.aborted).toBe(true)
})

it('honors cancellation before and during a request, then removes its abort listener', async () => {
    const transport = vi.fn(() => new Promise<Response>(() => undefined))
    vi.stubGlobal('fetch', transport)
    const canceled = new AbortController(); canceled.abort()
    await expect(publicJsonClient.get('/api/affiliate/discover/public-products', { timeout: 15_000, signal: canceled.signal })).rejects.toMatchObject({ code: 'ERR_CANCELED' })
    expect(transport).not.toHaveBeenCalled()
    const controller = new AbortController(); const remove = vi.spyOn(controller.signal, 'removeEventListener')
    const pending = publicJsonClient.get('/api/affiliate/discover/public-products', { timeout: 15_000, signal: controller.signal })
    const result = expect(pending).rejects.toMatchObject({ code: 'ERR_CANCELED' })
    controller.abort(); await result
    expect(remove).toHaveBeenCalledWith('abort', expect.any(Function))
})

it('rejects malformed/empty successful bodies and external or unbounded requests', async () => {
    const transport = vi.fn().mockResolvedValueOnce(new Response('not json', { status: 200 })).mockResolvedValueOnce(new Response('', { status: 200 }))
    vi.stubGlobal('fetch', transport)
    await expect(publicJsonClient.get('/api/example', { timeout: 15_000 })).rejects.toMatchObject({ code: 'INVALID_PUBLIC_RESPONSE' })
    await expect(publicJsonClient.get('/api/example', { timeout: 15_000 })).rejects.toMatchObject({ code: 'INVALID_PUBLIC_RESPONSE' })
    await expect(publicJsonClient.get('https://example.invalid/api', { timeout: 15_000 })).rejects.toMatchObject({ code: 'INVALID_PUBLIC_REQUEST' })
    await expect(publicJsonClient.get('/api/example', { timeout: 0 })).rejects.toMatchObject({ code: 'INVALID_PUBLIC_REQUEST' })
    expect(transport).toHaveBeenCalledTimes(2)
})
