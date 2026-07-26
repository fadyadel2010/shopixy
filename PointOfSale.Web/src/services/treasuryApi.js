/**
 * Treasury Ledger API Service
 * 
 * PHASE FTM: Frontend Treasury Migration
 * 
 * ALL Treasury data is sourced from the Ledger system:
 * - Accounts (for balances)
 * - JournalEntries (for transaction metadata)
 * - LedgerEntries (for individual debit/credit movements)
 * 
 * ❌ NO CashboxTransactions
 * ❌ NO VisaTransactions
 * ❌ NO TreasuryTransactions
 */

import api from './api'

// ═══════════════════════════════════════════════════════════════════════════
// ACCOUNT CODES (Ledger Chart of Accounts)
// ═══════════════════════════════════════════════════════════════════════════
export const ACCOUNT_CODES = {
    CASHBOX_MAIN: '1010',
    VISA_ACCOUNT: '1020',
    BANK_ACCOUNT: '1030',
    VISA_CLEARING: '1110',
    STRIPE_CLEARING: '1120',
    COD_CLEARING: '1130',
    WALLET_CLEARING: '1140',
    INVENTORY: '1310',
    ACCOUNTS_RECEIVABLE: '1410',
    SUPPLIER_PAYABLES: '2010',
    CUSTOMER_DEPOSITS: '2020',
    POS_SALES: '4010',
    ECOMMERCE_SALES: '4020',
    COGS: '5010',
    EXPENSES: '6100'
}

// Account display names
export const ACCOUNT_NAMES = {
    [ACCOUNT_CODES.CASHBOX_MAIN]: 'Cash',
    [ACCOUNT_CODES.VISA_ACCOUNT]: 'Visa Account',
    [ACCOUNT_CODES.BANK_ACCOUNT]: 'Bank Account',
    [ACCOUNT_CODES.VISA_CLEARING]: 'Visa Clearing',
    [ACCOUNT_CODES.STRIPE_CLEARING]: 'Stripe Clearing',
    [ACCOUNT_CODES.COD_CLEARING]: 'COD Clearing',
    [ACCOUNT_CODES.WALLET_CLEARING]: 'Wallet Clearing'
}

// ═══════════════════════════════════════════════════════════════════════════
// LEDGER BALANCE QUERIES
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get balance for a single account by code.
 * @param {string} accountCode - Account code (e.g., '1010')
 * @returns {Promise<{accountCode: string, balance: number}>}
 */
export const getTreasuryBalance = async (accountCode, options = {}) => {
    const response = await api.get(`/treasury/balance/${accountCode}`, {
        signal: options.signal,
    })
    return response.data
}

/**
 * Get balances for multiple accounts.
 * @param {string[]} accountCodes - Array of account codes
 * @returns {Promise<Object<string, number>>}
 */
export const getTreasuryBalances = async (accountCodes, options = {}) => {
    const response = await api.get('/treasury/balances', {
        params: { accountCodes: accountCodes.join(',') },
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// LEDGER MOVEMENTS (Transaction History)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get movements (ledger entries) for an account.
 * @param {string} accountCode - Account code (e.g., '1010')
 * @param {Date} from - Start date
 * @param {Date} to - End date
 * @returns {Promise<Array<TreasuryMovement>>}
 */
export const getTreasuryMovements = async (accountCode, from = null, to = null, options = {}) => {
    const params = {}
    if (from) params.from = from.toISOString().split('T')[0]
    if (to) params.to = to.toISOString().split('T')[0]

    const response = await api.get(`/treasury/movements/${accountCode}`, {
        params,
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// TREASURY DASHBOARD (Aggregated Metrics)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get Treasury dashboard with all financial metrics.
 * @param {Date} from - Start date
 * @param {Date} to - End date
 * @returns {Promise<TreasuryDashboard>}
 */
export const getTreasuryDashboard = async (from = null, to = null, options = {}) => {
    const params = {}
    if (from) params.from = from.toISOString().split('T')[0]
    if (to) params.to = to.toISOString().split('T')[0]

    const response = await api.get('/treasury/dashboard', {
        params,
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// CONVENIENCE FUNCTIONS
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get cash account balance (1010).
 */
export const getCashBalance = (options = {}) => getTreasuryBalance(ACCOUNT_CODES.CASHBOX_MAIN, options)

/**
 * Get bank account balance (1030).
 */
export const getBankBalance = (options = {}) => getTreasuryBalance(ACCOUNT_CODES.BANK_ACCOUNT, options)

/**
 * Get visa clearing balance (1110).
 */
export const getVisaClearingBalance = (options = {}) => getTreasuryBalance(ACCOUNT_CODES.VISA_CLEARING, options)

/**
 * Get cash account movements.
 */
export const getCashMovements = (from, to, options = {}) =>
    getTreasuryMovements(ACCOUNT_CODES.CASHBOX_MAIN, from, to, options)

/**
 * Get bank account movements.
 */
export const getBankMovements = (from, to, options = {}) =>
    getTreasuryMovements(ACCOUNT_CODES.BANK_ACCOUNT, from, to, options)

/**
 * Get visa clearing movements.
 */
export const getVisaClearingMovements = (from, to, options = {}) =>
    getTreasuryMovements(ACCOUNT_CODES.VISA_CLEARING, from, to, options)

// ═══════════════════════════════════════════════════════════════════════════
// TYPE DEFINITIONS (JSDoc)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * @typedef {Object} TreasuryMovement
 * @property {string} id - Ledger entry ID
 * @property {string} date - Transaction date
 * @property {string} journalNumber - Journal entry number
 * @property {string} description - Transaction description
 * @property {string} sourceType - Source document type
 * @property {string} sourceReference - Source document reference
 * @property {number} debit - Debit amount
 * @property {number} credit - Credit amount
 * @property {number} balanceAfter - Balance after this entry
 */

/**
 * @typedef {Object} TreasuryDashboard
 * @property {number} cashboxBalance - Cash balance
 * @property {number} bankBalance - Bank balance
 * @property {number} totalCashAndBank - Total liquid cash
 * @property {number} revenue - Total revenue in period
 * @property {number} costOfGoodsSold - COGS in period
 * @property {number} expenses - Total expenses in period
 * @property {number} netProfit - Net profit
 * @property {Array<ClearingAccount>} clearingAccounts - Pending clearing accounts
 * @property {number} totalPendingClearing - Total pending settlement
 */

/**
 * @typedef {Object} ClearingAccount
 * @property {string} accountCode - Account code
 * @property {string} name - Account name
 * @property {number} balance - Current balance
 * @property {string} sourceType - Clearing source type
 * @property {string} sourceCode - Provider code
 */

// ═══════════════════════════════════════════════════════════════════════════
// PHASE TOJ: TREASURY OPERATIONS (Ledger-based)
// ALL money mutations go through journals - NO DIRECT DATABASE WRITES
// ═══════════════════════════════════════════════════════════════════════════

/**
 * PHASE TOJ: Deposit money (transfer from one account to another).
 * Creates a balanced journal entry: DEBIT Target, CREDIT Source.
 * @param {Object} request - { sourceAccountCode, targetAccountCode, amount, description, reference, operationId }
 * @returns {Promise<{success: boolean, journalId: string, journalNumber: string, message: string}>}
 */
export const treasuryDeposit = async (request) => {
    const response = await api.post('/treasury/operations/deposit', request)
    return response.data
}

/**
 * PHASE TOJ: Withdraw money from an account.
 * Creates a balanced journal entry: DEBIT Target, CREDIT Source.
 * @param {Object} request - { sourceAccountCode, targetAccountCode, amount, description, reference, operationId }
 * @returns {Promise<{success: boolean, journalId: string, journalNumber: string, message: string}>}
 */
export const treasuryWithdraw = async (request) => {
    const response = await api.post('/treasury/operations/withdraw', request)
    return response.data
}

/**
 * PHASE TOJ: Transfer money between any two ledger accounts.
 * Creates a balanced journal entry: DEBIT ToAccount, CREDIT FromAccount.
 * @param {Object} request - { fromAccountCode, toAccountCode, amount, description, reference, operationId }
 * @returns {Promise<{success: boolean, journalId: string, journalNumber: string, message: string}>}
 */
export const treasuryTransfer = async (request) => {
    const response = await api.post('/treasury/operations/transfer', request)
    return response.data
}

/**
 * PHASE TOJ: Record expense paid from treasury account.
 * Creates a balanced journal entry: DEBIT Expense, CREDIT PaidFrom.
 * @param {Object} request - { paidFromAccountCode, expenseAccountCode, amount, description, reference, operationId }
 * @returns {Promise<{success: boolean, journalId: string, journalNumber: string, message: string}>}
 */
export const treasuryExpense = async (request) => {
    const response = await api.post('/treasury/operations/expense', request)
    return response.data
}

/**
 * PHASE TOJ: Cash In - receive money into treasury.
 * Creates a balanced journal entry: DEBIT Target, CREDIT Credit.
 * @param {Object} request - { targetAccountCode, creditAccountCode, amount, description, reference, operationId }
 * @returns {Promise<{success: boolean, journalId: string, journalNumber: string, message: string}>}
 */
export const treasuryCashIn = async (request) => {
    const response = await api.post('/treasury/operations/cash-in', request)
    return response.data
}

/**
 * PHASE TOJ: Cash Out - pay money from treasury.
 * Creates a balanced journal entry: DEBIT Debit, CREDIT Source.
 * @param {Object} request - { sourceAccountCode, debitAccountCode, amount, description, reference, operationId }
 * @returns {Promise<{success: boolean, journalId: string, journalNumber: string, message: string}>}
 */
export const treasuryCashOut = async (request) => {
    const response = await api.post('/treasury/operations/cash-out', request)
    return response.data
}

export default {
    ACCOUNT_CODES,
    ACCOUNT_NAMES,
    getTreasuryBalance,
    getTreasuryBalances,
    getTreasuryMovements,
    getTreasuryDashboard,
    getCashBalance,
    getBankBalance,
    getVisaClearingBalance,
    getCashMovements,
    getBankMovements,
    getVisaClearingMovements,
    // PHASE TOJ: Treasury Operations
    treasuryDeposit,
    treasuryWithdraw,
    treasuryTransfer,
    treasuryExpense,
    treasuryCashIn,
    treasuryCashOut
}

