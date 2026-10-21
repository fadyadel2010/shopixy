import { useEffect, useState } from 'react'
import api from '../services/api'

const root = '/qa/affiliate-certification'

// Not linked from merchant navigation. Server-side identity/runtime checks remain
// authoritative even in a locally built UI. Never include this route in production.
export default function SyntheticQaCertification() {
    const [enabled, setEnabled] = useState(false)
    const [busy, setBusy] = useState(false)
    const [values, setValues] = useState({ promotionLinkId: '', customerId: '', runId: '', orderId: '', shipmentId: '', evidenceId: '' })
    const [result, setResult] = useState(null)
    useEffect(() => {
        let active = true
        api.get(`${root}/status`).then(() => { if (active) setEnabled(true) }).catch(() => { if (active) setEnabled(false) })
        return () => { active = false }
    }, [])
    const execute = async (path, body) => {
        setBusy(true)
        try {
            const response = await api.post(path, body)
            setResult(response.data)
            if (response.data.run?.id) setValues(v => ({ ...v, runId: response.data.run.id }))
            if (response.data.kind) setValues(v => ({ ...v, evidenceId: response.data.id }))
        } catch (error) { setResult({ status: error.response?.status, error: error.response?.data || 'Request failed' }) }
        finally { setBusy(false) }
    }
    if (!enabled) return <h1>QA certification unavailable</h1>
    const evidence = kind => execute(`${root}/evidence`, { runId: values.runId, orderId: values.orderId, shipmentId: values.shipmentId || null, kind })
    const consume = action => execute(`${root}/${action}`, { orderId: values.orderId, evidenceId: values.evidenceId })
    return <section className="p-6 space-y-4">
        <h1 className="text-xl font-bold">SYNTHETIC QA — NO REAL DELIVERY OR MONEY</h1>
        <p>Local certification only. No carrier, wallet transfer, cash receipt, withdrawal or payout.</p>
        {Object.keys(values).map(key => <label key={key} className="block">{key}
            <input className="block border p-2 w-full" aria-label={key} value={values[key]}
                onChange={event => setValues(v => ({ ...v, [key]: event.target.value }))} />
        </label>)}
        <div className="flex flex-wrap gap-3">
            <button disabled={busy} onClick={() => execute(`${root}/runs`, { promotionLinkId: values.promotionLinkId, customerId: values.customerId })}>Create QA run</button>
            <button disabled={busy} onClick={() => evidence('Collection')}>Issue collection evidence</button>
            <button disabled={busy} onClick={() => consume('collect')}>Simulate collection — NO MONEY</button>
            <button disabled={busy} onClick={() => evidence('Delivery')}>Issue delivery attestation</button>
            <button disabled={busy} onClick={() => consume('deliver')}>Consume synthetic delivery</button>
            <button disabled={busy} onClick={() => execute('/admin/affiliates/backfill-commission', { orderId: values.orderId })}>Revalidate canonical commission</button>
        </div>
        <pre aria-label="QA result" className="whitespace-pre-wrap">{JSON.stringify(result, null, 2)}</pre>
    </section>
}
