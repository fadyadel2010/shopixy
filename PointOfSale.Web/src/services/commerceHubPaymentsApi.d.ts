import type { ApiJson } from './apiJson';
export function createVendorPaymentIdempotencyKey(): string;
export function getCommerceHubPaymentsDashboard(): Promise<ApiJson>;
export function createCommerceHubPaymentRequest(request: { amount: number; methodId: string; transferReference?: string; idempotencyKey: string; proofFile?: Blob }): Promise<ApiJson>;
export function getCommerceHubPaymentProofUrl(paymentRequestId: string): Promise<ApiJson>;
