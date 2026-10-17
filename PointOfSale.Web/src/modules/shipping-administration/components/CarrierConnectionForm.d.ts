import type { ReactElement } from 'react';
import type { CarrierAccount, SupportedCarrier } from '../shippingAdministrationApi';
export default function CarrierConnectionForm(props: {
    account: CarrierAccount; definition?: SupportedCarrier; onMutated: () => Promise<void>;
    onBusy?: (action: string) => void;
    onOutcome?: (outcome: { status: 'Pending' | 'NotTested' | 'Succeeded' | 'Failed' }) => void;
}): ReactElement;
