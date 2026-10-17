export function getCsrfToken(): string;
export function isUnsafeHttpMethod(method?: string): boolean;
export function appendCsrfHeader<T extends object>(headers?: T, method?: string): T | (T & { 'X-CSRF-Token': string });
export function getCsrfHeaderName(): 'X-CSRF-Token';
