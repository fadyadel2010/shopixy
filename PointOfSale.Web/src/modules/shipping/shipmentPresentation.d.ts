import type { ShipmentPresentation } from './shippingTypes';
export function storedLabelLink(shipment: ShipmentPresentation): { href: string; download?: string } | null;
