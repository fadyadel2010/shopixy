import type { ReactElement } from 'react';
export interface PaymentMethodPresentation { id?: string | null; code: string; displayName?: string; isSupported?: boolean; requiresConfiguration?: boolean; hasTenantPaymentMethod?: boolean; isConfigurationValid?: boolean; configurationErrors?: string[]; isEnabled?: boolean; isSandbox?: boolean; sortOrder?: number; fee?: number; feeType?: string; unsupportedReasonCode?: string | null; unsupportedMessage?: string | null; [key: string]: unknown }
export function isUnsupportedMethod(method: PaymentMethodPresentation | null | undefined): boolean;
export function isValidManualWalletNumber(value: unknown): boolean;
export function unsupportedReason(method: PaymentMethodPresentation | null): { code: string; message: string };
export function mergePaymentMethodCatalog(catalog: PaymentMethodPresentation[] | null, methods: PaymentMethodPresentation[] | null): PaymentMethodPresentation[];
export default function PaymentMethods(): ReactElement;
