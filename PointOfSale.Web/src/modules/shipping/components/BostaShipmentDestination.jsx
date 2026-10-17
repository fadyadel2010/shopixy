import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { readBostaNativeDestination } from '../../shipping-administration/bostaNativeApi'
import BostaNativeDestinationSelector from '../../shipping-administration/components/BostaNativeDestinationSelector'

export default function BostaShipmentDestination({ shipmentId }) {
    const { t } = useTranslation()
    const [view, setView] = useState(null)
    const [error, setError] = useState(false)
    const [busy, setBusy] = useState(false)
    const scope = useRef(null)
    useEffect(() => { scope.current = {}; setView(null); setError(false); setBusy(false); return () => { scope.current = null } }, [shipmentId])
    const load = async () => {
        const identity = scope.current
        setBusy(true); setError(false)
        try { const result = await readBostaNativeDestination(shipmentId); if (scope.current === identity) setView(result) }
        catch { if (scope.current === identity) setError(true) }
        finally { if (scope.current === identity) setBusy(false) }
    }
    return <section className="rounded-xl border bg-white p-4 space-y-3">
        <button type="button" className="btn-secondary" disabled={busy} onClick={load}>{t('shippingAdministration.native.readSnapshot')}</button>
        {error && <p role="alert">{t('shippingAdministration.native.failed')}</p>}
        {view?.evidence && <p>{t('shippingAdministration.native.snapshotSaved')} — <bdi>{view.evidence.city.name} / {view.evidence.district.name} — {view.evidence.district.id}</bdi></p>}
        {view?.canSelect && <BostaNativeDestinationSelector accountId={view.accountId} shipmentId={shipmentId} />}
    </section>
}
