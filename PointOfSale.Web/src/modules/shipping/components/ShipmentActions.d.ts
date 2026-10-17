import type { ReactElement } from 'react';
import type { ShipmentPresentation, ShipmentActionAuthority } from '../shippingTypes';
export default function ShipmentActions(props: {
    shipment: ShipmentPresentation | null; actionAuthority?: ShipmentActionAuthority | null; actionAuthorityLoading?: boolean;
    actionAuthorityError?: string | null; refetchActionAuthority?: () => Promise<ShipmentActionAuthority | null>; onActionComplete?: () => void;
}): ReactElement | null;
