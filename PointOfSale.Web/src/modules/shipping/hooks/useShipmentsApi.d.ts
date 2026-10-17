import type { ShipmentPresentation, ShipmentActionAuthority, CancellationOutcome } from '../shippingTypes';
export interface ShipmentFilters { pageSize?: number; status?: string; carrierCode?: string; isCod?: boolean; dateFrom?: string; dateTo?: string; search?: string }
export function useShipmentsList(filters?: ShipmentFilters): {
    shipments: ShipmentPresentation[]; pagination: { page: number; pageSize: number; totalCount: number; totalPages: number };
    loading: boolean; error: string | null; goToPage(page: number): Promise<void>; refetch(): Promise<void>;
};
export function useShipmentDetails(id?: string | null): { shipment: ShipmentPresentation | null; loading: boolean; error: string | null; refetch(): Promise<void> };
export function useShipmentActions(): {
    loading: boolean; error: string | null; clearError(): void;
    updateStatus(id: string, status: string, notes?: string): Promise<{ success: false; error: string }>;
    cancelShipment(id: string, reason?: string): Promise<{ success: boolean; data?: CancellationOutcome; error?: string }>;
};
export function useShipmentActionAuthority(id?: string | null): { authority: ShipmentActionAuthority | null; loading: boolean; error: string | null; refetch(): Promise<ShipmentActionAuthority | null> };
export function getStatusConfig(status: string): { bgColor: string; borderColor: string; textColor: string; icon: string; order: number; color?: string; isTerminal?: boolean };
export const SHIPMENT_STATUS_CONFIG: Record<string, ReturnType<typeof getStatusConfig>>;
export function useShippingHealth(): { health: unknown; loading: boolean; error: string | null; refetch(): Promise<void> };
