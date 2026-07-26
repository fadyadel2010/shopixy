/**
 * Shipping Module Index
 * Phase: Shipping Frontend + Unified Shipping Configuration
 * 
 * Exports all shipping admin components and pages.
 * 
 * NEW: ShippingSetupPage is the Single Source of Truth for shipping configuration.
 */

// Pages
export { default as ShipmentsListPage } from './pages/ShipmentsListPage'
export { default as ShipmentDetailsPage } from './pages/ShipmentDetailsPage'
export { default as ShippingHealthPage } from './pages/ShippingHealthPage'
export { default as ShippingSetupPage } from './pages/ShippingSetupPage'

// Shipment Components
export { default as ShipmentTable } from './components/ShipmentTable'
export { default as ShipmentTimeline } from './components/ShipmentTimeline'
export { default as ShipmentSummaryCard } from './components/ShipmentSummaryCard'
export { default as ShipmentActions } from './components/ShipmentActions'
export { default as CarrierBadge } from './components/CarrierBadge'
export { default as CodStatusBadge } from './components/CodStatusBadge'

// Shipping Setup Components (Phase: Unified Shipping Configuration)
export { default as CarrierStatusCard } from './components/CarrierStatusCard'
export { default as ShippingOptionRow } from './components/ShippingOptionRow'
export { default as ShippingOptionFormModal } from './components/ShippingOptionFormModal'
export { default as CarrierConfigurationModal } from './components/CarrierConfigurationModal'

// Hooks
export * from './hooks/useShipmentsApi'
export * from './hooks/useShippingSetupApi'
