import { getScopedStorageKey, safeWriteJson } from './storageScope'

const HELD_SALES_STORAGE_PREFIX = 'pos_held_sales_v2'
const LEGACY_HELD_SALES_STORAGE_KEY = 'pos_held_sales'
const CART_BACKUP_STORAGE_PREFIX = 'pos_cart_backup_v2'
const LEGACY_CART_BACKUP_STORAGE_KEY = 'pos_cart_backup'

const getHeldSalesStorageKey = () => getScopedStorageKey(HELD_SALES_STORAGE_PREFIX)
const getCartBackupStorageKey = () => getScopedStorageKey(CART_BACKUP_STORAGE_PREFIX)

const migrateLegacyValue = (scopedKey, legacyKey) => {
    if (typeof localStorage === 'undefined') {
        return null
    }

    const scopedValue = localStorage.getItem(scopedKey)
    if (scopedValue) {
        if (localStorage.getItem(legacyKey)) {
            localStorage.removeItem(legacyKey)
        }

        return scopedValue
    }

    const legacyValue = localStorage.getItem(legacyKey)
    if (!legacyValue) {
        return null
    }

    localStorage.setItem(scopedKey, legacyValue)
    localStorage.removeItem(legacyKey)
    return legacyValue
}

const readScopedValue = (scopedKey, legacyKey, fallback) => {
    if (typeof localStorage === 'undefined') {
        return fallback
    }

    const migratedValue = migrateLegacyValue(scopedKey, legacyKey)
    const rawValue = localStorage.getItem(scopedKey) || migratedValue
    if (!rawValue) {
        return fallback
    }

    try {
        return JSON.parse(rawValue)
    } catch (error) {
        console.error(`Error parsing browser storage value for "${scopedKey}":`, error)
        localStorage.removeItem(scopedKey)
        return fallback
    }
}

/**
 * Save a held sale to localStorage
 * @throws {Error} If persistence fails after 3 attempts
 */
export const saveHeldSale = (sale) => {
    const heldSalesStorageKey = getHeldSalesStorageKey()
    for (let attempt = 0; attempt < 3; attempt += 1) {
        const held = getHeldSales()
        // Remove all entries with the same id (handles duplicates) and append the new sale
        const mergedHeld = [...held.filter(existingSale => existingSale.id !== sale.id), sale]

        safeWriteJson(localStorage, heldSalesStorageKey, mergedHeld)
        
        // Verify the write by deep comparison
        const saved = getHeldSales()
        const persisted = saved.find(s => s.id === sale.id)
        
        if (persisted && JSON.stringify(persisted) === JSON.stringify(sale)) {
            break
        }
        
        if (attempt === 2) {
            // Log only non-sensitive metadata to avoid PII exposure
            const sanitizedError = {
                saleId: sale.id,
                timestamp: new Date().toISOString(),
                retryCount: attempt + 1,
                dataMismatch: true,
                persistedExists: !!persisted
            }
            console.error('Failed to persist held sale after 3 attempts:', sanitizedError)
            throw new Error(
                `Failed to persist held sale ${sale.id} after 3 attempts. Data mismatch detected.`
            )
        }
    }

    localStorage.removeItem(LEGACY_HELD_SALES_STORAGE_KEY)
}

/**
 * Get all held sales from localStorage
 */
export const getHeldSales = () => {
    try {
        const heldSalesStorageKey = getHeldSalesStorageKey()
        const sales = readScopedValue(heldSalesStorageKey, LEGACY_HELD_SALES_STORAGE_KEY, [])
        if (!Array.isArray(sales)) {
            localStorage.removeItem(heldSalesStorageKey)
            return []
        }

        // Filter out expired sales (older than 24 hours)
        const now = new Date()
        const validSales = sales.filter(sale => {
            const heldAt = new Date(sale.heldAt)
            const hoursDiff = (now - heldAt) / (1000 * 60 * 60)
            return hoursDiff < 24
        })

        // Update localStorage if any sales were filtered out
        if (validSales.length !== sales.length) {
            safeWriteJson(localStorage, heldSalesStorageKey, validSales)
        }

        return validSales
    } catch (error) {
        console.error('Error reading held sales:', error)
        return []
    }
}

/**
 * Remove a held sale by ID
 */
export const removeHeldSale = (id) => {
    const heldSalesStorageKey = getHeldSalesStorageKey()
    const held = getHeldSales().filter(s => s.id !== id)
    safeWriteJson(localStorage, heldSalesStorageKey, held)
}

/**
 * Clear all held sales
 */
export const clearAllHeldSales = () => {
    localStorage.removeItem(getHeldSalesStorageKey())
    localStorage.removeItem(LEGACY_HELD_SALES_STORAGE_KEY)
}

/**
 * Save cart backup for crash recovery
 */
export const saveCartBackup = (cart) => {
    try {
        safeWriteJson(localStorage, getCartBackupStorageKey(), {
            ...cart,
            savedAt: new Date().toISOString()
        })
        localStorage.removeItem(LEGACY_CART_BACKUP_STORAGE_KEY)
    } catch (error) {
        console.error('Error saving cart backup:', error)
    }
}

/**
 * Get cart backup if it exists and is not expired
 */
export const getCartBackup = () => {
    try {
        const cartBackupStorageKey = getCartBackupStorageKey()
        const backup = readScopedValue(cartBackupStorageKey, LEGACY_CART_BACKUP_STORAGE_KEY, null)
        if (!backup) {
            return null
        }

        const savedAt = new Date(backup.savedAt)
        const now = new Date()
        const hoursDiff = (now - savedAt) / (1000 * 60 * 60)

        // Discard if older than 24 hours
        if (hoursDiff > 24) {
            clearCartBackup()
            return null
        }

        return backup
    } catch (error) {
        console.error('Error reading cart backup:', error)
        return null
    }
}

/**
 * Clear cart backup
 */
export const clearCartBackup = () => {
    localStorage.removeItem(getCartBackupStorageKey())
    localStorage.removeItem(LEGACY_CART_BACKUP_STORAGE_KEY)
}

/**
 * Generate a unique reference number for held sales
 */
export const generateHoldReference = () => {
    const timestamp = Date.now().toString(36).toUpperCase()
    const randomPart = Math.random().toString(36).slice(2, 6).toUpperCase()
    return `HOLD-${timestamp}-${randomPart}`
}
