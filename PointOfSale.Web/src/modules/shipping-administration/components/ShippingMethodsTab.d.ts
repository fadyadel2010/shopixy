import type { ReactElement } from 'react';
import type { CarrierAccount, Warehouse } from '../shippingAdministrationApi';
export interface AdminShippingMethod {
    id: string; name: string; nameAr: string | null; description: string | null; baseRate: number; perKgRate: number;
    freeShippingThreshold: number | null; estimatedDeliveryDaysMin: number; estimatedDeliveryDaysMax: number;
    sortOrder: number; carrierName: string | null; carrierCode: string | null; serviceLevel: string;
    trackingUrlTemplate: string | null; availableCountries: string | null; isActive: boolean; fulfillmentWarehouseId: string | null;
}
export function admitAdminShippingMethods(payload: unknown): AdminShippingMethod[];
export default function ShippingMethodsTab(props?: {
    warehouses?: Array<Pick<Warehouse, 'id' | 'name' | 'code' | 'isActive'>>;
    carrierAccounts?: CarrierAccount[];
}): ReactElement;
