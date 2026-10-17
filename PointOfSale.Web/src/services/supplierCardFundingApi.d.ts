import type { ApiJson } from './apiJson';
export const SUPPLIER_CARD_ROLES: string[];
export const SUPPLIER_CARD_ADMIN_ROLES: string[];
export function canAdministerSupplierCards(role: unknown): boolean;
export function canAccessSupplierCards(role: unknown): boolean;
export interface SupplierCardAuthority { id: string; authorityVersion: string | number; displayName: string; last4?: string | null }
export function formatSupplierCardLabel(instrument: Pick<SupplierCardAuthority, 'displayName' | 'last4'> | null): string;
export function purchaseRequiresSupplierCardInstrument(request: { paymentMethod?: string; paidAmount?: number | string; initialPaymentMethod?: string | null; cardAmount?: number | string }): boolean;
export function getSupplierCardAuthorityFields(required: boolean, instrument: Pick<SupplierCardAuthority, 'id' | 'authorityVersion'> | null | undefined): { fundingInstrumentId: string | null; fundingInstrumentVersion: string | number | null };
export function createSupplierCardOperationId(prefix?: string): string;
export interface SupplierCardAttempt { fingerprint: string; idempotencyKey: string }
export function resolveSupplierCardAttempt(previousAttempt: SupplierCardAttempt | null | undefined, fingerprint: string, prefix?: string): SupplierCardAttempt;
export function getSupplierCardErrorMessage(error: unknown, t: (key: string) => string, fallbackKey?: string): string;
export const supplierCardFundingApi: {
    list(options?: { activeOnly?: boolean; signal?: AbortSignal }): Promise<ApiJson[]>;
    listFundingSources(options?: { signal?: AbortSignal }): Promise<ApiJson[]>;
    create(data: ApiJson): Promise<ApiJson>; update(id: string, data: ApiJson): Promise<ApiJson>;
    fund(id: string, data: ApiJson, idempotencyKey: string): Promise<ApiJson>;
    statement(id: string, options?: { from?: string; to?: string; signal?: AbortSignal }): Promise<ApiJson>;
};
export default supplierCardFundingApi;
