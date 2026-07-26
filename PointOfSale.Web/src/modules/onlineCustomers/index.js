/**
 * Online Customers Module
 * Phase P1: POS Admin - Online Customers Management (Storefront)
 * 
 * This module provides POS Admin screens for managing Storefront Online Customers.
 * 
 * FEATURES:
 * - List online customers with search & filters
 * - View customer details
 * - Activate/Deactivate accounts
 * - View and revoke sessions
 * 
 * ARCHITECTURE:
 * - Uses Admin APIs only (no Storefront APIs)
 * - PostgreSQL is source of truth for writes
 * - MongoDB provides read-optimized views
 * - All actions are auditable
 * 
 * SECURITY:
 * - TenantId enforced everywhere
 * - Role-based access (Admin, Owner, Manager)
 * - Session revocation is immediate via Redis
 */

export * from './pages'
export * from './components'
export * from './services'
