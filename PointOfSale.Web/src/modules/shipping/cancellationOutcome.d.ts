import type { HttpRequestOptions } from '../../core/httpContracts';
import type { CancellationOutcome } from './shippingTypes';
export function admitCancellationOutcome(data: unknown, expectedShipmentId?: string): CancellationOutcome;
export function requestShipmentCancellation(api: { post(url: string, data?: unknown, config?: HttpRequestOptions): Promise<{ data: unknown }> }, shipmentId: string, reason: string, config?: HttpRequestOptions): Promise<CancellationOutcome>;
