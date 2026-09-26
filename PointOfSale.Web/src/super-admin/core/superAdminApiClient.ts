import axios, { type AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { appendCsrfHeader } from '../../utils/csrf'
import { useSuperAdminAuthStore } from '../auth/superAdminAuthStore'
import {
    canRefreshSuperAdminSession,
    isSuperAdminAccessTokenExpired,
} from '../auth/superAdminSession'
import {
    isDefinitiveSuperAdminAuthFailure,
    restoreSuperAdminSession,
} from '../auth/superAdminSessionCoordinator'
import type {
    SuperAdminApiErrorResponse,
    SuperAdminApiSuccessResponse,
} from '../auth/superAdminAuth.types'

interface RetryableRequestConfig extends InternalAxiosRequestConfig {
    _retry?: boolean
}

const SUPER_ADMIN_REQUEST_TIMEOUT_MS = 30000
const SUPER_ADMIN_REQUEST_TIMEOUT_MESSAGE = 'Super Admin request timeout after 30 seconds'
const SUPER_ADMIN_RAW_ENDPOINT_ADAPTERS = [
    {
        canonicalPrefix: '/payment-providers',
        backendPrefix: '/api/admin/payment-providers',
    },
] as const

export class SuperAdminHttpError extends Error {
    status?: number
    code?: string
    requestId?: string

    constructor(message: string, options?: { status?: number; code?: string; requestId?: string }) {
        super(message)
        this.name = 'SuperAdminHttpError'
        this.status = options?.status
        this.code = options?.code
        this.requestId = options?.requestId
    }
}

function createRequestId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID()
    }

    return `sa-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function redirectToSuperAdminLogin(): void {
    useSuperAdminAuthStore.getState().logout()
}

export function shouldInvalidateSuperAdminSession(statusCode: number | undefined): boolean {
    return statusCode === 401
}

function normalizeHttpError(error: unknown): SuperAdminHttpError {
    const axiosError = error as AxiosError<SuperAdminApiErrorResponse>

    if (
        axiosError.code === 'ECONNABORTED' ||
        axiosError.code === 'ETIMEDOUT' ||
        axiosError.message === SUPER_ADMIN_REQUEST_TIMEOUT_MESSAGE ||
        axiosError.message?.toLowerCase().includes('timeout')
    ) {
        return new SuperAdminHttpError('Super Admin request timed out. Please try again.', {
            code: 'REQUEST_TIMEOUT',
        })
    }

    if (axiosError.response?.data?.error) {
        return new SuperAdminHttpError(
            axiosError.response.data.error.message || 'Super Admin request failed.',
            {
                status: axiosError.response.status,
                code: axiosError.response.data.error.code,
                requestId: axiosError.response.data.meta?.requestId,
            },
        )
    }

    if (axiosError.response) {
        return new SuperAdminHttpError('Super Admin request failed.', {
            status: axiosError.response.status,
        })
    }

    if (axiosError.request) {
        return new SuperAdminHttpError('Network error while reaching the Super Admin service.')
    }

    return new SuperAdminHttpError('Unexpected Super Admin error.')
}

class SuperAdminApiClient {
    private readonly client: AxiosInstance
    private refreshPromise: Promise<void> | null = null

    constructor() {
        this.client = axios.create({
            baseURL: '/api/super-admin',
            timeout: SUPER_ADMIN_REQUEST_TIMEOUT_MS,
            timeoutErrorMessage: SUPER_ADMIN_REQUEST_TIMEOUT_MESSAGE,
            headers: {
                'Content-Type': 'application/json',
            },
            withCredentials: true,
        })

        this.client.interceptors.request.use(async (config) => {
            const requestUrl = config.url || ''
            const isAuthMutation =
                requestUrl.includes('/auth/login') || requestUrl.includes('/auth/refresh')

            config.headers = appendCsrfHeader(config.headers || {}, config.method)
            config.withCredentials = true

            if (!isAuthMutation && isSuperAdminAccessTokenExpired() && canRefreshSuperAdminSession()) {
                await this.refreshAccessToken()
            } else if (!isAuthMutation && isSuperAdminAccessTokenExpired()) {
                redirectToSuperAdminLogin()
                throw new SuperAdminHttpError('Super Admin session has expired.', { status: 401 })
            }

            if (!config.headers['X-Request-Id']) {
                config.headers['X-Request-Id'] = createRequestId()
            }

            return config
        })

        this.client.interceptors.response.use(
            (response) => response,
            async (error: AxiosError<SuperAdminApiErrorResponse>) => {
                const originalRequest = error.config as RetryableRequestConfig | undefined
                const statusCode = error.response?.status
                const requestUrl = originalRequest?.url || ''
                const isRefreshEndpoint = requestUrl.includes('/auth/refresh')

                if (
                    statusCode === 401 &&
                    originalRequest &&
                    !originalRequest._retry &&
                    !isRefreshEndpoint &&
                    canRefreshSuperAdminSession()
                ) {
                    originalRequest._retry = true

                    try {
                        await this.refreshAccessToken()
                        return this.client(originalRequest)
                    } catch (refreshError) {
                        if (isDefinitiveSuperAdminAuthFailure(refreshError)) {
                            redirectToSuperAdminLogin()
                        }
                        throw normalizeHttpError(refreshError)
                    }
                }

                if (shouldInvalidateSuperAdminSession(statusCode)) {
                    redirectToSuperAdminLogin()
                }

                throw normalizeHttpError(error)
            },
        )
    }

    private async refreshAccessToken(): Promise<void> {
        if (this.refreshPromise) {
            return this.refreshPromise
        }

        if (!canRefreshSuperAdminSession()) {
            throw new SuperAdminHttpError('Super Admin session has expired.', { status: 401 })
        }

        this.refreshPromise = restoreSuperAdminSession()
            .then((session) => {
                useSuperAdminAuthStore.getState().setSession(session)
            })
            .finally(() => {
                this.refreshPromise = null
            })

        return this.refreshPromise
    }

    public get<T>(url: string): Promise<SuperAdminApiSuccessResponse<T>> {
        return this.client
            .get<SuperAdminApiSuccessResponse<T>>(url)
            .then((response) => response.data)
    }

    public getRaw<T>(url: string): Promise<T> {
        return this.client
            .get<T>(this.resolveRawUrl(url))
            .then((response) => response.data)
    }

    public getBlob(url: string): Promise<Blob> {
        return this.client
            .get<Blob>(this.resolveAbsoluteUrl(url), {
                responseType: 'blob',
            })
            .then((response) => response.data)
    }

    public post<TResponse, TBody = unknown>(
        url: string,
        body?: TBody,
    ): Promise<SuperAdminApiSuccessResponse<TResponse>> {
        return this.client
            .post<SuperAdminApiSuccessResponse<TResponse>>(url, body)
            .then((response) => response.data)
    }

    public put<TResponse, TBody = unknown>(
        url: string,
        body?: TBody,
    ): Promise<SuperAdminApiSuccessResponse<TResponse>> {
        return this.client
            .put<SuperAdminApiSuccessResponse<TResponse>>(url, body)
            .then((response) => response.data)
    }

    public putRaw<TResponse, TBody = unknown>(
        url: string,
        body?: TBody,
    ): Promise<TResponse> {
        return this.client
            .put<TResponse>(this.resolveRawUrl(url), body)
            .then((response) => response.data)
    }

    public postRaw<TResponse, TBody = unknown>(
        url: string,
        body?: TBody,
    ): Promise<TResponse> {
        return this.client
            .post<TResponse>(this.resolveRawUrl(url), body)
            .then((response) => response.data)
    }

    public delete<T>(url: string): Promise<SuperAdminApiSuccessResponse<T>> {
        return this.client
            .delete<SuperAdminApiSuccessResponse<T>>(url)
            .then((response) => response.data)
    }

    private resolveAbsoluteUrl(url: string): string {
        const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin
        return new URL(url, origin).toString()
    }

    private resolveRawUrl(url: string): string {
        const adapter = SUPER_ADMIN_RAW_ENDPOINT_ADAPTERS.find(({ canonicalPrefix }) => (
            url === canonicalPrefix || url.startsWith(`${canonicalPrefix}/`)
        ))

        if (adapter) {
            return this.resolveAbsoluteUrl(`${adapter.backendPrefix}${url.slice(adapter.canonicalPrefix.length)}`)
        }

        return this.resolveAbsoluteUrl(url)
    }
}

export const superAdminApiClient = new SuperAdminApiClient()
