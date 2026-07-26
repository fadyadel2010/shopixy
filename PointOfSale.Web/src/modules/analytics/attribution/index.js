/**
 * Attribution Module Index
 * Phase 8A: Admin Attribution Dashboard UI
 * Phase 8B: Campaign Cost & ROI (MVP)
 * 
 * Exports all attribution components, pages, and hooks
 */

// Pages
export { default as AttributionOverviewPage } from './pages/AttributionOverviewPage'
export { default as AttributionModelsPage } from './pages/AttributionModelsPage'
export { default as AttributionOrdersPage } from './pages/AttributionOrdersPage'
export { default as CampaignCostsPage } from './pages/CampaignCostsPage'
export { default as RoiDashboardPage } from './pages/RoiDashboardPage'

// Components
export { default as AttributionSummaryCards } from './components/AttributionSummaryCards'
export { default as RevenueBySourceChart } from './components/RevenueBySourceChart'
export { default as CampaignPerformanceTable } from './components/CampaignPerformanceTable'
export { default as ModelComparisonCards } from './components/ModelComparisonCards'
export { default as CampaignRoiTable } from './components/CampaignRoiTable'
export { default as RoiSummaryCards } from './components/RoiSummaryCards'

// Hooks
export * from './hooks/useAttribution'
export * from './hooks/useCampaignCost'

