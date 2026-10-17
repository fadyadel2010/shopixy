import type { ShipmentPresentation, ShipmentActionAuthority } from './shippingTypes';
export function requireShipmentActionAuthority(authority: unknown, expectedOrderId: string): ShipmentActionAuthority;
export function hasShipmentActionAuthority(authority: ShipmentActionAuthority | null | undefined, shipment: ShipmentPresentation | null | undefined, actionCode: string): boolean;
