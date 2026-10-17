/** Service-facing HTTP contracts. Domain services must not depend on a transport
 * library's instance, header implementation, or interceptor internals. */
export interface HttpRequestOptions {
    headers?: Record<string, string | number | boolean | null | undefined>
    params?: Record<string, unknown> | URLSearchParams
    signal?: AbortSignal
    method?: string
    timeout?: number
    responseType?: 'json' | 'text' | 'blob' | 'arraybuffer' | 'document'
    withCredentials?: boolean
    skipAutomaticRetry?: boolean
}

export interface HttpResponse<T> {
    data: T
    status: number
    statusText: string
    headers: Record<string, unknown>
    config: HttpRequestOptions
}
