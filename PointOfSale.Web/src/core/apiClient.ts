import type { AxiosError } from 'axios';
import api, {
  API_REQUEST_TIMEOUT_MESSAGE,
  isRequestTimeout,
} from '../services/api';

/**
 * Centralized API client for marketplace.
 * Handles auth, errors, and normalization.
 * NO business logic here.
 */
class ApiClient {
  private normalizeError(error: AxiosError): AppError {
    if (isRequestTimeout(error) || error.message === API_REQUEST_TIMEOUT_MESSAGE) {
      return {
        message: 'Request timed out. Please try again.',
        status: 0,
        code: 'REQUEST_TIMEOUT',
      };
    }

    if (error.response) {
      return {
        message: (error.response.data as any)?.error || 'An error occurred',
        status: error.response.status,
        code: (error.response.data as any)?.errorCode,
      };
    }

    if (error.request) {
      return {
        message: 'Network error. Please check your connection.',
        status: 0,
      };
    }

    return {
      message: error.message || 'Unknown error',
      status: 0,
    };
  }

  public get<T>(url: string): Promise<T> {
    return api.get<T>(url).then((res) => res.data).catch((error) => Promise.reject(this.normalizeError(error)));
  }

  public post<T>(url: string, data?: any): Promise<T> {
    return api.post<T>(url, data).then((res) => res.data).catch((error) => Promise.reject(this.normalizeError(error)));
  }

  public put<T>(url: string, data?: any): Promise<T> {
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
