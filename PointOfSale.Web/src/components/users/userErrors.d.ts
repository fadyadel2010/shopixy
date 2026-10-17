export function userErrorKey(error: unknown): 'errors.staleData' | 'errors.accessDenied' | 'errors.notFound' | 'errors.rateLimited' | 'errors.conflict' | 'errors.ownerViolation' | 'errors.targetForbidden' | 'errors.tokenExpired' | 'errors.validation' | 'errors.generic';
export function isVersionConflict(error: unknown): boolean;
export function isAccessDenied(error: unknown): boolean;
