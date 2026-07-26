/**
 * AF-4B: Affiliate Module — POS Admin Only
 *
 * ARCHITECTURE RULE:
 * This module exports ONLY admin management components consumed by POS App.jsx.
 *
 * The affiliate hub (landing, login, register, portal) lives in the
 * external Affiliate Hub entry point — NOT in POS.
 *
 * Components exported here:
 * - AdminAffiliatesList: Admin listing of all affiliate accounts
 * - AdminAffiliateDetail: Admin detail view of a single affiliate
 *
 * Components NOT exported (external Affiliate Hub):
 * - AffiliatePortal, AffiliateLogin, AffiliateRegister, AffiliateLanding
 */
export { default as AdminAffiliatesList } from './pages/AdminAffiliatesList'
export { default as AdminAffiliateDetail } from './pages/AdminAffiliateDetail'
