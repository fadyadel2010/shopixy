import type { OrderDto } from '@/types/order';

// A localized display name is not payment authority. Missing/unknown codes
// remain unknown instead of becoming a manufactured online payment.
export function orderPaymentMethod(code: unknown): OrderDto['paymentMethod'] {
    if (typeof code !== 'string') return 'unknown';
    switch (code.trim().toUpperCase()) {
        case 'COD': return 'cod';
        case 'MANUALWALLET': return 'manual';
        case 'STRIPE': case 'PAYMOB': case 'FAWRY': return 'online';
        default: return 'unknown';
    }
}
