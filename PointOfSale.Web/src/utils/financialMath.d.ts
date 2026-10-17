export function normalizeCurrencyCode(value: unknown): string;
export function assertTwoDecimalCurrency(value: unknown): string;
export function toSafeNumber(value: unknown, fallback?: number): number;
export type AmountParseResult = { isValid: true; value: number; rawValue: string; normalizedValue: string; reason: null }
    | { isValid: false; value: null; rawValue: string; normalizedValue: ''; reason: 'required' | 'invalid_format' | 'negative' | 'too_many_decimals' | 'zero' };
export function parseStrictAmountInput(value: unknown, options?: { allowZero?: boolean; allowNegative?: boolean; maxDecimalPlaces?: number }): AmountParseResult;
export function roundCurrency(value: unknown): number;
export function toCurrencyMinorUnits(value: unknown): number;
export function fromCurrencyMinorUnits(value: unknown): number;
export function formatCurrencyInputValue(value: unknown): string;
export function clampAmount(value: unknown, min: unknown, max: unknown): number;
export function divide(a: unknown, b: unknown): number;
export function sumCurrency(values: readonly unknown[]): number;
export function calculateLineSubtotal(quantity: unknown, unitPrice: unknown): number;
export function calculatePercentageAmount(amount: unknown, rate: unknown): number;
export function calculateLineTax(quantity: unknown, unitPrice: unknown, taxRate: unknown): number;
export const calculateLineTotal: typeof calculateLineTax;
export interface PricedLine { currentUnitPrice?: unknown; unitPrice?: unknown; isStalePrice?: boolean; quantity?: unknown; discountAmount?: unknown }
export function getEffectiveUnitPrice(item: PricedLine | null | undefined): number;
export function clampLineDiscount(quantity: unknown, unitPrice: unknown, discountAmount: unknown): number;
export function getClampedLineDiscountState(quantity: unknown, unitPrice: unknown, discountAmount: unknown): { discountAmount: number; wasAdjusted: boolean };
export function calculateRemainingSubtotal(subtotal: unknown, itemDiscounts: unknown): number;
export function calculateRemainingSubtotalFromItems(items: readonly PricedLine[]): number;
