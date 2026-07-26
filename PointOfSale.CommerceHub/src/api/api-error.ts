import axios from "axios";

export class CommerceHubApiError extends Error {
  status: number | null;
  code?: string;
  details?: unknown;
  isNetworkError: boolean;
  isRetryable: boolean;

  constructor({
    message,
    status = null,
    code,
    details,
    isNetworkError = false,
    isRetryable = false
  }: {
    message: string;
    status?: number | null;
    code?: string;
    details?: unknown;
    isNetworkError?: boolean;
    isRetryable?: boolean;
  }) {
    super(message);
    this.name = "CommerceHubApiError";
    this.status = status;
    this.code = code;
    this.details = details;
    this.isNetworkError = isNetworkError;
    this.isRetryable = isRetryable;
  }
}

export const normalizeApiError = (error: unknown) => {
  if (axios.isAxiosError(error)) {
    const status = error.response?.status ?? null;
    const payload = error.response?.data as
      | {
          message?: string;
          error?: string;
          code?: string;
          title?: string;
          errors?: unknown;
        }
      | undefined;

    const message =
      payload?.message ??
      payload?.error ??
      payload?.title ??
      (error.code === "ECONNABORTED"
        ? "The request took too long. Please try again."
        : error.message || "Something went wrong while talking to CommerceHub.");

    return new CommerceHubApiError({
      message,
      status,
      code: payload?.code ?? error.code,
      details: payload?.errors ?? payload,
      isNetworkError: !error.response,
      isRetryable: status === null || status >= 500 || error.code === "ECONNABORTED"
    });
  }

  if (error instanceof CommerceHubApiError) {
    return error;
  }

  if (error instanceof Error) {
    return new CommerceHubApiError({
      message: error.message
    });
  }

  return new CommerceHubApiError({
    message: "An unexpected error occurred."
  });
};
