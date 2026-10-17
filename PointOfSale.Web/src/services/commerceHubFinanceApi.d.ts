import type { ApiJson } from './apiJson';
export const COMMERCEHUB_FINANCE_TRANSACTION_TYPES: string[];
export interface StatementFilters { page?: number; pageSize?: number; fromDate?: string; toDate?: string; transactionTypes?: string[] }
export function buildStatementParams(filters: StatementFilters): URLSearchParams;
export function getCommerceHubFinanceDashboard(): Promise<ApiJson>;
export function getCommerceHubFinanceStatement(filters: StatementFilters): Promise<ApiJson>;
export function downloadCommerceHubFinanceStatementCsv(filters: StatementFilters): Promise<Blob>;
