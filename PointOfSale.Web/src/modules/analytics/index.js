/**
 * Analytics Module Index
 * Phase 5A: Admin Funnel Dashboard UI
 * Phase 8A: Admin Attribution Dashboard UI
 * 
 * Exports all analytics components and pages
 */

// Funnel Pages
export { default as FunnelsListPage } from './pages/FunnelsListPage'
export { default as FunnelDetailsPage } from './pages/FunnelDetailsPage'

// Funnel Components
export { default as FunnelSummaryCards } from './components/FunnelSummaryCards'
export { default as FunnelBars } from './components/FunnelBars'
export { default as FunnelStepTable } from './components/FunnelStepTable'

// Funnel Hooks
export * from './hooks/useFunnelRealtime'

// Attribution (Phase 8A) & Campaign Cost & ROI (Phase 8B)
export * from './attribution'
