import api, {
  API_REQUEST_TIMEOUT_MESSAGE,
  isRequestTimeout,
} from '../services/api';

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/**
 * Centralized API client for marketplace.
 * Handles auth, errors, and normalization.
 * NO business logic here.
 */
class ApiClient {
  private normalizeError(value: unknown): AppError {
    const error = isRecord(value) ? value : {};
    if (isRequestTimeout(value) || error.message === API_REQUEST_TIMEOUT_MESSAGE) {
      return {
        message: 'Request timed out. Please try again.',
        status: 0,
        code: 'REQUEST_TIMEOUT',
      };
    }

    if (isRecord(error.response)) {
      const data = isRecord(error.response.data) ? error.response.data : {};
      return {
        message: typeof data.error === 'string' && data.error ? data.error : 'An error occurred',
        status: typeof error.response.status === 'number' && Number.isFinite(error.response.status) ? error.response.status : 0,
        code: typeof data.errorCode === 'string' ? data.errorCode : undefined,
      };
    }

    if (error.request) {
      return {
        message: 'Network error. Please check your connection.',
        status: 0,
      };
    }

    return {
      message: typeof error.message === 'string' && error.message ? error.message : 'Unknown error',
      status: 0,
    };
  }

  public get<T>(url: string): Promise<T> {
    return api.get<T>(url).then((res) => res.data).catch((error) => Promise.reject(this.normalizeError(error)));
  }

  public post<T>(url: string, data?: unknown): Promise<T> {
    return api.post<T>(url, data).then((res) => res.data).catch((error) => Promise.reject(this.normalizeError(error)));
  }

  public put<T>(url: string, data?: unknown): Promise<T> {
    return api.put<T>(url, data).then((res) => res.data).catch((error) => Promise.reject(this.normalizeError(error)));
  }

  public delete<T>(url: string): Promise<T> {
    return api.delete<T>(url).then((res) => res.data).catch((error) => Promise.reject(this.normalizeError(error)));
  }
}

export interface AppError {
  message: string;
  status: number;
  code?: string;
}

export const apiClient = new ApiClient();
