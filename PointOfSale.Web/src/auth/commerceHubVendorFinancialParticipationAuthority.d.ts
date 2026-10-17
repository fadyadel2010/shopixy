// These validators return the original DTO (not a client-reconstructed balance).
export function deriveVendorFinancialDashboardPresentation<T>(value: T, currentUser: unknown): T | null;
export const deriveVendorEarningsDashboardPresentation: typeof deriveVendorFinancialDashboardPresentation;
export const deriveVendorPaymentsDashboardPresentation: typeof deriveVendorFinancialDashboardPresentation;
export const deriveVendorFinanceStatementPresentation: typeof deriveVendorFinancialDashboardPresentation;
export function deriveVendorPayoutMethodsPresentation<T>(values: T[], currentUser: unknown): T[];
export function deriveVendorPaymentProofPresentation<T>(value: T): T | null;
export function deriveCurrentPaymentMethodSelection<T extends { id: string }>(methodId: unknown, methods: T[] | null | undefined): T | null;
