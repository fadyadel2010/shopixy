export const TREASURY_ACCOUNT_TYPES: { CASHBOX: 'Cashbox'; BANK: 'Bank'; CLEARING: 'Clearing' };
export interface TreasuryCatalogAccount { id?: string; accountCode: string; accountType?: string; name?: string; nameAr?: string; currentBalance?: number | string }
export function isAvailableCashAccount(account: unknown): boolean;
export const isTransferEligibleAccount: typeof isAvailableCashAccount;
export const isClearingAccount: typeof isAvailableCashAccount;
export function getLocalizedTreasuryAccountName(account: TreasuryCatalogAccount | null | undefined, language?: string): string;
export function partitionTreasuryCatalog<T>(accounts: T[]): { available: T[]; clearing: T[]; unavailable: T[] };
export function sumTreasuryBalances(accounts: unknown): number;
export function findTreasuryAccountByCode<T extends TreasuryCatalogAccount>(accounts: T[], accountCode: string): T | null;
declare const catalog: { TREASURY_ACCOUNT_TYPES: typeof TREASURY_ACCOUNT_TYPES; isAvailableCashAccount: typeof isAvailableCashAccount; isTransferEligibleAccount: typeof isTransferEligibleAccount; isClearingAccount: typeof isClearingAccount; getLocalizedTreasuryAccountName: typeof getLocalizedTreasuryAccountName; partitionTreasuryCatalog: typeof partitionTreasuryCatalog; sumTreasuryBalances: typeof sumTreasuryBalances; findTreasuryAccountByCode: typeof findTreasuryAccountByCode };
export default catalog;
