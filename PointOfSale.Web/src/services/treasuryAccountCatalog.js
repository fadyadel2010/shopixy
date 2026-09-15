/**
 * Treasury account catalog presentation helper.
 *
 * The tenant-scoped GET /api/treasury/accounts response is the only Treasury
 * role authority consumed by the browser. `accountType` owns the role:
 * exact "Cashbox" or "Bank" means available cash, exact "Clearing" means
 * money in transit. Nothing else (code, name, icon, balance sign, parent,
 * IsSystem) grants Treasury authority. Unknown types fail closed.
 */

export const TREASURY_ACCOUNT_TYPES = {
    CASHBOX: 'Cashbox',
    BANK: 'Bank',
    CLEARING: 'Clearing',
}

export const isAvailableCashAccount = (account) =>
    account?.accountType === TREASURY_ACCOUNT_TYPES.CASHBOX ||
    account?.accountType === TREASURY_ACCOUNT_TYPES.BANK

export const isClearingAccount = (account) =>
    account?.accountType === TREASURY_ACCOUNT_TYPES.CLEARING

export const isTransferEligibleAccount = (account) =>
    isAvailableCashAccount(account)

export const getLocalizedTreasuryAccountName = (account, language) => {
    if (!account) return ''
    const isArabic = typeof language === 'string' && language.toLowerCase().startsWith('ar')
    const nameAr = typeof account.nameAr === 'string' ? account.nameAr.trim() : ''
    const name = typeof account.name === 'string' ? account.name.trim() : ''
    if (isArabic && nameAr) return nameAr
    return name || nameAr || account.accountCode || ''
}

export const partitionTreasuryCatalog = (accounts) => {
    const rows = Array.isArray(accounts) ? accounts : []
    return {
        available: rows.filter(isAvailableCashAccount),
        clearing: rows.filter(isClearingAccount),
        unavailable: rows.filter(
            (account) => !isAvailableCashAccount(account) && !isClearingAccount(account),
        ),
    }
}

export const sumTreasuryBalances = (accounts) =>
    (Array.isArray(accounts) ? accounts : []).reduce(
        (sum, account) => sum + (Number(account?.currentBalance) || 0),
        0,
    )

export const findTreasuryAccountByCode = (accounts, accountCode) =>
    (Array.isArray(accounts) ? accounts : []).find(
        (account) => account?.accountCode === accountCode,
    ) || null

export default {
    TREASURY_ACCOUNT_TYPES,
    isAvailableCashAccount,
    isClearingAccount,
    isTransferEligibleAccount,
    getLocalizedTreasuryAccountName,
    partitionTreasuryCatalog,
    sumTreasuryBalances,
    findTreasuryAccountByCode,
}
