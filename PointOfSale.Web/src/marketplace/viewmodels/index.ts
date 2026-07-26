/**
 * ViewModel layer - Pure mapping of backend response → UI-ready shape.
 * RULES:
 * - Consumes hooks only
 * - Maps to UI props
 * - Adds loading + empty flags
 * - Adds formatted text
 * - NO domain logic
 * - NO calculations
 * - NO business rules
 */

export { useMarketplaceHomeVM } from './useMarketplaceHomeVM';
export { useRequestDetailsVM } from './useRequestDetailsVM';
export { useChatVM } from './useChatVM';
