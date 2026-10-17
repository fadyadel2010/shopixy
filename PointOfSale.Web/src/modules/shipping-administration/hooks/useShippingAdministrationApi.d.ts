import type { AdministrationAudit, CarrierAccount, SupportedCarrier, Warehouse } from '../shippingAdministrationApi';

export function useShippingAdministrationApi(): {
    supportedCarriers: SupportedCarrier[];
    carrierAccounts: CarrierAccount[];
    warehouses: Warehouse[];
    audit: AdministrationAudit[];
    loading: boolean;
    error: string;
    refresh: () => Promise<void>;
};
