import { useTranslation } from 'react-i18next'
import { storedLabelLink } from '../shipmentPresentation'

export default function ShipmentEvidencePanel({ shipment }) {
    const { t } = useTranslation()
    const label = storedLabelLink(shipment)
    return <section className="bg-white border border-gray-200 rounded-xl p-6 min-w-0 break-words" aria-label={t('shipping.closure.registration')}>
        <h3 className="text-lg font-semibold mb-3">{t('shipping.closure.registration')}</h3>
        <p>{t(`shipping.closure.registrationStates.${shipment.externalRegistrationState || 'Unknown'}`, { defaultValue: shipment.externalRegistrationState || t('shipping.closure.unknown') })}</p>
        {shipment.externalRegistrationRecoveryRequired === true && <p role="alert" className="text-amber-800 mt-2">{t('shipping.closure.recoveryRequired')}</p>}
        {shipment.externalRegistrationError && <p role="alert" className="text-amber-800 mt-2">{shipment.externalRegistrationError}</p>}
        <p className="text-sm text-gray-600 mt-2">{t('shipping.closure.attempts')}: {shipment.externalRegistrationAttemptCount ?? 0}</p>
        {shipment.awbNumber && <p>{t('shipping.closure.awb')}: <bdi className="font-mono break-all">{shipment.awbNumber}</bdi></p>}
        {label ? <a {...label} target={label.download ? undefined : '_blank'} rel="noopener noreferrer" className="inline-block text-blue-700 underline mt-3">{t('shipping.closure.storedLabel')}</a>
            : <p className="text-sm mt-3">{t('shipping.closure.noStoredLabel')}</p>}
        <p className="text-xs text-gray-500 mt-2">{t('shipping.closure.labelBoundary')}</p>
        {shipment.isCashOnDelivery && <p className="text-sm text-amber-800 mt-3">{t('shipping.closure.moneyBoundary')}</p>}
    </section>
}
