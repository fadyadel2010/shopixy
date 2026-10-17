import type { ShipmentPresentation } from '../../shipping/shippingTypes';
export function requireBackendShipmentIdentity<T>(candidate: T, expectedOrderId?: string | null): T & Required<Pick<ShipmentPresentation, 'orderId' | 'shipmentNumber'>>;
export function requireBackendTrackedOrder<T>(candidate: T): T & { id: string };
