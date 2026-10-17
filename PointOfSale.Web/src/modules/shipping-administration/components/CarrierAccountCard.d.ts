import type { ReactElement } from 'react';
import type { CarrierAccount, SupportedCarrier } from '../shippingAdministrationApi';
export default function CarrierAccountCard(props: {
    account: CarrierAccount; supportedCarriers: SupportedCarrier[]; onMutated: () => Promise<void>;
}): ReactElement;
