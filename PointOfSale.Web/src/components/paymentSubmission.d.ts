import type { AmountParseResult } from '../utils/financialMath';
export function getSaleSubmissionErrorMessage(error: unknown, t: (key: string, fallback?: string) => string): string;
export function parseAmountInput(value: unknown): AmountParseResult;
export function getMixedPaymentState(total: number, cashAmount: unknown): { totalDue: number; normalizedCashAmount: number; normalizedCardAmount: number; totalPaid: number; difference: number; hasInvalidCashAmount: boolean; hasNegativeCashAmount: boolean; hasZeroCashAmount: boolean; hasCashAmountAtOrAboveTotal: boolean; shouldClampCashAmount: boolean };
