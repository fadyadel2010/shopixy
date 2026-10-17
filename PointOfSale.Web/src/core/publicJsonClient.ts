export interface PublicJsonRequestOptions {
    timeout: number
    params?: Record<string, string | number | boolean | undefined>
    signal?: AbortSignal
}

export class PublicJsonError extends Error {
    readonly response?: { status: number; data: unknown }
    constructor(readonly code: string, message: string, response?: { status: number; data: unknown }) {
        super(message)
        this.name = 'PublicJsonError'
        this.response = response
    }
}

/** Anonymous GET transport, intentionally separate from POS/Affiliate session
 * clients: no cookies, auth interceptors, refresh, automatic retry, or mutations. */
async function get(url: string, options: PublicJsonRequestOptions): Promise<{ data: unknown }> {
    if (!url.startsWith('/api/') || url.includes('\\') || !Number.isFinite(options.timeout) || options.timeout <= 0 || options.timeout > 30_000)
        throw new PublicJsonError('INVALID_PUBLIC_REQUEST', 'Invalid public request configuration.')
    const target = new URL(url, window.location.origin)
    for (const [key, value] of Object.entries(options.params || {})) if (value !== undefined) target.searchParams.append(key, String(value))
    const canceled = () => new PublicJsonError('ERR_CANCELED', 'Public request was canceled.')
    if (options.signal?.aborted) throw canceled()
    const controller = new AbortController()
    let rejectBudget!: (reason: Error) => void
    const budget = new Promise<never>((_, reject) => { rejectBudget = reject })
    const abort = () => { rejectBudget(canceled()); controller.abort() }
    options.signal?.addEventListener('abort', abort, { once: true })
    const timeout = window.setTimeout(() => {
        rejectBudget(new PublicJsonError('ECONNABORTED', 'Public request timed out.'))
        controller.abort()
    }, options.timeout)
    try {
        const request = fetch(target.pathname + target.search, { method: 'GET', credentials: 'omit', signal: controller.signal })
            .then(async response => {
                const text = await response.text()
                let data: unknown = null
                try { if (text) data = JSON.parse(text) }
                catch { if (response.ok) throw new PublicJsonError('INVALID_PUBLIC_RESPONSE', 'Invalid public JSON response.') }
                if (!response.ok) throw new PublicJsonError('PUBLIC_HTTP_FAILURE', 'Public discovery request failed.', { status: response.status, data })
                if (!text) throw new PublicJsonError('INVALID_PUBLIC_RESPONSE', 'Empty public JSON response.')
                return { data }
            })
        return await Promise.race([request, budget])
    } finally {
        window.clearTimeout(timeout)
        options.signal?.removeEventListener('abort', abort)
    }
}

export const publicJsonClient = { get }
