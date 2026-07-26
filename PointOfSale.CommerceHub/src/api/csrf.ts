const CSRF_COOKIE_NAME = "csrf_token";
const CSRF_HEADER_NAME = "X-CSRF-Token";
const UNSAFE_METHODS = new Set(["post", "put", "patch", "delete"]);

export function appendCsrfHeader(
  headers: Record<string, string>,
  method?: string
): Record<string, string> {
  if (!method || !UNSAFE_METHODS.has(method.toLowerCase())) {
    return headers;
  }

  if (typeof document === "undefined") {
    return headers;
  }

  const cookieValue = document.cookie
    .split(";")
    .map((part) => part.trim())
    .find((part) => part.startsWith(`${CSRF_COOKIE_NAME}=`));

  if (!cookieValue) {
    return headers;
  }

  return {
    ...headers,
    [CSRF_HEADER_NAME]: decodeURIComponent(cookieValue.slice(CSRF_COOKIE_NAME.length + 1))
  };
}
