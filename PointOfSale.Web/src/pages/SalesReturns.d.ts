import type { ReactElement } from 'react';
export function roundQuantity(value: unknown): number;
export function calculateReturnTotals(items: { quantity: number; unitPrice?: number; taxRate?: number; originalQuantity?: number; originalUnitPrice?: number; originalDiscountAmount?: number; previouslyReturnedQuantity?: number; originalTaxAmount?: number; discountAmount?: number; [key: string]: unknown }[], originalInvoice: { discountAmount?: number; subtotal?: number; [key: string]: unknown } | null): { lines: { invoiceDiscountMinor: number; invoiceDiscount: number; subtotalMinor: number; subtotal: number; taxMinor: number; tax: number; totalMinor: number; total: number }[]; subtotal: number; tax: number; total: number };
export default function SalesReturns(): ReactElement;
