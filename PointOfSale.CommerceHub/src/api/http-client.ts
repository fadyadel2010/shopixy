import axios, { InternalAxiosRequestConfig } from "axios";

import { appendCsrfHeader } from "@/api/csrf";
import { normalizeApiError } from "@/api/api-error";
import { dispatchCustomerSessionAuthorityLost, isCustomerSessionAuthorityEndpoint } from "@/auth/customer-session-authority";
import { markActivePaymentInitiationTrace } from "@/features/checkout/paymentInitiationTrace";
import { getLanguage } from "@/store/language-store";

type RequestMetadata = {
  startedAt: number;
};

type RequestConfigWithMetadata = InternalAxiosRequestConfig & {
  metadata?: RequestMetadata;
};

const resolveBaseUrl = () => import.meta.env.VITE_API_BASE_URL ?? "";

export const httpClient = axios.create({
  baseURL: resolveBaseUrl(),
  timeout: 10000,
  headers: {
    "Content-Type": "application/json"
  },
  withCredentials: true
});

httpClient.interceptors.request.use(
  (config) => {
    const nextConfig = config as RequestConfigWithMetadata;
    nextConfig.baseURL = resolveBaseUrl();
    nextConfig.metadata = {
      startedAt: Date.now()
    };

    nextConfig.headers.set("X-Requested-With", "CommerceHub");

    // Inject current language into every API request
    const language = getLanguage();
    nextConfig.headers.set("Accept-Language", language);
    nextConfig.params = { ...nextConfig.params, lang: language };
    const csrfHeaders = appendCsrfHeader({}, nextConfig.method);
    if (csrfHeaders["X-CSRF-Token"]) {
      nextConfig.headers.set("X-CSRF-Token", csrfHeaders["X-CSRF-Token"]);
    }

    if (nextConfig.url === "/api/commercehub/payments/initiate") {
      markActivePaymentInitiationTrace("http-request-interceptor", {
        method: nextConfig.method?.toUpperCase(),
        url: nextConfig.url,
        baseUrl: nextConfig.baseURL
      });
    }

    return nextConfig;
  },
  (error) => Promise.reject(normalizeApiError(error))
);

httpClient.interceptors.response.use(
  (response) => {
    if (response.config.url === "/api/commercehub/payments/initiate") {
      markActivePaymentInitiationTrace("http-response-received", {
        status: response.status,
        url: response.config.url
      });
    }

    if (import.meta.env.DEV) {
      const metadata = (response.config as RequestConfigWithMetadata).metadata;
      const duration = metadata ? Date.now() - metadata.startedAt : 0;
      console.info("[CommerceHub API]", response.config.url, response.status, `${duration}ms`);
    }

    return response;
  },
  (error) => {
    const status = error?.response?.status;
    const url = error?.config?.url;
    if (url === "/api/commercehub/payments/initiate") {
      markActivePaymentInitiationTrace("http-response-failed", {
        status: status ?? null,
        code: error?.response?.data?.code ?? error?.code ?? null,
        url
      });
    }

    if ((status === 401 || status === 403) && isCustomerSessionAuthorityEndpoint(url)) {
      dispatchCustomerSessionAuthorityLost();
    }

    return Promise.reject(normalizeApiError(error));
  }
);
