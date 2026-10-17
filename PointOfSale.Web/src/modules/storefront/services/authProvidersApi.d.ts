import type { ApiJson } from '../../../services/apiJson';
export function getAuthProviders(): Promise<ApiJson>;
export function getAuthProvider(provider: string): Promise<ApiJson>;
export function updateAuthProvider(provider: string, settings: { clientId?: string; clientSecret?: string; additionalScopes?: string }): Promise<ApiJson>;
export function enableAuthProvider(provider: string): Promise<ApiJson>;
export function disableAuthProvider(provider: string, reason?: string | null): Promise<ApiJson>;
export function rotateSecret(provider: string, clientSecret: string): Promise<ApiJson>;
declare const authProvidersApi: { getAuthProviders: typeof getAuthProviders; getAuthProvider: typeof getAuthProvider; updateAuthProvider: typeof updateAuthProvider; enableAuthProvider: typeof enableAuthProvider; disableAuthProvider: typeof disableAuthProvider; rotateSecret: typeof rotateSecret };
export default authProvidersApi;
