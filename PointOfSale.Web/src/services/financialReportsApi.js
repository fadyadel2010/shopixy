/**
 * Financial Reports API Service
 * 
 * PHASE 5: Treasury & Finance Control Center
 * 
 * Consumes Phase 4 Financial Reporting Engine APIs.
 * All data sourced from immutable ledger.
 */

import api from './api'

// Cache for dashboard data (30 seconds)
// Bounded LRU cache with TTL eviction
const CACHE_DURATION = 30000 // 30 seconds
const MAX_CACHE_ENTRIES = 100

class BoundedTTLCache {
    constructor(maxSize, ttl) {
        this.cache = new Map()
        this.maxSize = maxSize
        this.ttl = ttl
        this.cleanupTimer = null
        this.startPeriodicCleanup()
    }

    get(key) {
        const entry = this.cache.get(key)
        if (!entry) {
            return null
        }

        const now = Date.now()
        if (now - entry.timestamp >= this.ttl) {
            // Stale entry, remove and treat as miss
            this.cache.delete(key)
            return null
        }

        // Move to end (LRU: most recently used)
        this.cache.delete(key)
        this.cache.set(key, entry)
        return entry.value
    }

    set(key, value) {
        const now = Date.now()
        
        // Remove if already exists (to update position)
        if (this.cache.has(key)) {
            this.cache.delete(key)
        }

        // Evict oldest entry if at capacity
        if (this.cache.size >= this.maxSize) {
            const firstKey = this.cache.keys().next().value
            this.cache.delete(firstKey)
        }

        this.cache.set(key, { value, timestamp: now })
    }

    clear() {
        this.cache.clear()
    }

    startPeriodicCleanup() {
        // Run cleanup every minute to remove stale entries
        this.cleanupTimer = setInterval(() => {
            this.removeStaleEntries()
        }, 60000)
    }

    removeStaleEntries() {
        const now = Date.now()
        const keysToDelete = []

        for (const [key, entry] of this.cache.entries()) {
            if (now - entry.timestamp >= this.ttl) {
                keysToDelete.push(key)
            }
        }

        for (const key of keysToDelete) {
            this.cache.delete(key)
        }
    }

    destroy() {
        if (this.cleanupTimer) {
            clearInterval(this.cleanupTimer)
            this.cleanupTimer = null
        }
        this.cache.clear()
    }
}

const dashboardCache = new BoundedTTLCache(MAX_CACHE_ENTRIES, CACHE_DURATION)

/**
 * Get date range parameters for API calls
 */
const getDateParams = (from, to) => {
    const params = new URLSearchParams()
    if (from) params.append('from', from.toISOString().split('T')[0])
    if (to) params.append('to', to.toISOString().split('T')[0])
    return params.toString()
}

/**
 * Get date params for 'as of' queries
 */
const getAsOfParams = (asOf) => {
    const params = new URLSearchParams()
    if (asOf) params.append('asOf', asOf.toISOString().split('T')[0])
    return params.toString()
}

// ═══════════════════════════════════════════════════════════════════════════
// DASHBOARD
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get dashboard summary with KPIs
 * Cached for 30 seconds per date range.
 */
export const getDashboard = async (from, to, forceRefresh = false, options = {}) => {
    const params = getDateParams(from, to)
    
    // Check cache for this specific date range
    if (!forceRefresh) {
        const cached = dashboardCache.get(params)
        if (cached) {
            return cached
        }
    }
    
    const response = await api.get(`/financialreports/dashboard?${params}`, {
        signal: options.signal,
    })
    
    // Update cache for this date range
    dashboardCache.set(params, response.data)
    
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// TRIAL BALANCE
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get Trial Balance report
 */
export const getTrialBalance = async (from, to, options = {}) => {
    const params = getDateParams(from, to)
    const response = await api.get(`/financialreports/trial-balance?${params}`, {
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// GENERAL LEDGER
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get General Ledger for a specific account
 */
export const getGeneralLedger = async (accountId, from, to, options = {}) => {
    const params = getDateParams(from, to)
    const response = await api.get(`/financialreports/general-ledger/${accountId}?${params}`, {
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// PROFIT & LOSS
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get Profit & Loss (Income Statement) report
 */
export const getProfitAndLoss = async (from, to, options = {}) => {
    const params = getDateParams(from, to)
    const response = await api.get(`/financialreports/profit-loss?${params}`, {
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// BALANCE SHEET
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get Balance Sheet report
 */
export const getBalanceSheet = async (asOf, options = {}) => {
    const params = getAsOfParams(asOf)
    const response = await api.get(`/financialreports/balance-sheet?${params}`, {
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// CASH FLOW
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get Cash Flow Statement
 */
export const getCashFlow = async (from, to, options = {}) => {
    const params = getDateParams(from, to)
    const response = await api.get(`/financialreports/cash-flow?${params}`, {
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// CLEARING AGING
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get Clearing Aging report
 */
export const getClearingAging = async (asOf, options = {}) => {
    const params = getAsOfParams(asOf)
    const response = await api.get(`/financialreports/clearing-aging?${params}`, {
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// ACCOUNT SUMMARIES
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Get all account summaries
 */
export const getAccountSummaries = async (options = {}) => {
    const response = await api.get('/financialreports/accounts', {
        signal: options.signal,
    })
    return response.data
}

// ═══════════════════════════════════════════════════════════════════════════
// HELPER FUNCTIONS
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Clear the dashboard cache
 */
export const clearDashboardCache = () => {
    dashboardCache.clear()
}

/**
 * Get default date range (current month)
 */
export const getDefaultDateRange = () => {
    const today = new Date()
    const from = new Date(today.getFullYear(), today.getMonth(), 1)
    const to = today
    return { from, to }
}

/**
 * Get fiscal year date range
 */
export const getFiscalYearRange = () => {
    const today = new Date()
    const from = new Date(today.getFullYear(), 0, 1) // Jan 1
    const to = today
    return { from, to }
}

export default {
    getDashboard,
    getTrialBalance,
    getGeneralLedger,
    getProfitAndLoss,
    getBalanceSheet,
    getCashFlow,
    getClearingAging,
    getAccountSummaries,
    clearDashboardCache,
    getDefaultDateRange,
    getFiscalYearRange
}
