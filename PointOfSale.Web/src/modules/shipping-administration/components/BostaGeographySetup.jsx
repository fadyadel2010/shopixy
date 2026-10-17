import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../../../services/api'
import CanonicalCatalogBootstrap from './CanonicalCatalogBootstrap'

const empty = () => ({ reference: null, city: '', district: '', run: null, choices: {}, release: null, preview: null, readback: null })

export default function BostaGeographySetup({ accountId, environment, configurationVersion, disabled }) {
    const { i18n } = useTranslation()
    const ar = i18n.language.startsWith('ar')
    const [view, setView] = useState(empty)
    const [busy, setBusy] = useState(false)
    const [message, setMessage] = useState('')
    const [resumeId, setResumeId] = useState('')
    const [syncId, setSyncId] = useState('')
    const [bootstrapReason, setBootstrapReason] = useState('')
    const [bootstrapApproved, setBootstrapApproved] = useState(false)
    const scope = useRef(null)
    const pending = useRef(false)
    useEffect(() => {
        scope.current = {}; pending.current = false; setView(empty()); setMessage(''); setResumeId(''); setBusy(false)
        setSyncId(''); setBootstrapReason(''); setBootstrapApproved(false)
        return () => { scope.current = null }
    }, [accountId, environment, configurationVersion])
    const base = `/admin/shipping/carrier-accounts/${accountId}/bosta/geography`
    const run = async operation => {
        if (disabled || pending.current) return
        const identity = scope.current
        pending.current = true; setBusy(true); setMessage('')
        try {
            const patch = await operation()
            if (scope.current === identity) setView(previous => ({ ...previous, ...patch }))
        } catch (error) {
            if (scope.current === identity) setMessage(error.response?.data?.failureCode || error.response?.data?.code || error.message || 'BOSTA_GEOGRAPHY_OPERATION_FAILED')
        } finally {
            if (scope.current === identity) { pending.current = false; setBusy(false) }
        }
    }
    const load = () => run(async () => {
        const { data } = await api.get(base, { params: view.city ? { cityId: view.city } : {} })
        if (data.accountId !== accountId || data.environment !== environment || !Array.isArray(data.places)) throw new Error('BOSTA_REFERENCE_TARGET_MISMATCH')
        return { reference: data, run: null, release: null, preview: null, readback: null }
    })
    const sync = () => run(async () => ({ run: (await api.post(`${base}/sync`, { cityId: view.city })).data, choices: {}, release: null, preview: null, readback: null }))
    const bootstrap = () => run(async () => ({ run: (await api.post(`${base}/bootstrap`, { cityId: view.city, reason: bootstrapReason })).data, choices: {}, release: null, preview: null, readback: null }))
    const resumeSync = () => run(async () => ({ run: (await api.get(`${base}/sync/${syncId}`)).data, choices: {}, release: null, preview: null, readback: null }))
    const selectedCandidates = view.run?.candidates?.filter(c => c.providerPlaceType === 'country' || c.providerPlaceType === 'city' && c.providerPlaceId === view.city || c.providerPlaceType === 'district' && c.providerPlaceId === view.district && c.providerParentPlaceId === view.city) || []
    const accept = () => run(async () => {
        const { data } = await api.post(`${base}/drafts`, { syncRunId: view.run.id, catalogReleaseId: view.reference.catalogReleaseId,
            expectedSourceHashSha256: view.run.sourceHashSha256, reason: 'Explicit Admin review of Bosta reference identities',
            choices: selectedCandidates.map(c => ({ candidateId: c.id, canonicalPlaceId: view.choices[c.id] })) })
        if (data.succeeded !== true) throw new Error(data.failureCode)
        const preview = (await api.get(`/admin/geographic/carrier-mappings/releases/${data.resourceId}/validation`)).data
        const readback = (await api.get(`${base}/releases/${data.resourceId}`)).data
        return { release: data.resourceId, preview, readback }
    })
    const publish = action => run(async () => {
        const { data } = await api.post(`/admin/geographic/carrier-mappings/releases/${view.release}/${action}`, {
            expectedContentChecksumSha256: view.preview.contentChecksumSha256, reason: 'Independent Admin review of exact Bosta mapping revision' })
        if (data.succeeded !== true) throw new Error(data.failureCode)
        return { readback: (await api.get(`${base}/releases/${view.release}`)).data }
    })
    const resume = () => run(async () => {
        const readback = (await api.get(`${base}/releases/${resumeId}`)).data
        const preview = (await api.get(`/admin/geographic/carrier-mappings/releases/${resumeId}/validation`)).data
        return { release: resumeId, readback, preview }
    })
    const lock = disabled || busy
    return <section className="space-y-3 rounded border border-blue-200 p-3" aria-label={ar ? 'جغرافيا بوسطة' : 'Bosta geography'}>
        <h5 className="font-semibold">{ar ? 'مزامنة ومراجعة جغرافيا بوسطة' : 'Synchronize and review Bosta geography'}</h5>
        <p className="text-xs">{ar ? 'لا يتم تفعيل الناقل أو حجز شحنة. نقطة الاستلام منفصلة عن وجهة التسليم. النشر يتطلب مراجعا مستقلا.' : 'No carrier activation or shipment booking. Pickup origin is separate from destination mapping. Publication requires an independent reviewer.'}</p>
        <CanonicalCatalogBootstrap accountId={accountId} environment={environment} configurationVersion={configurationVersion} disabled={disabled} />
        <button type="button" className="btn-secondary" disabled={lock} onClick={load}>{ar ? 'تحميل المرجع الحالي' : 'Load current geography reference'}</button>
        <label className="block text-sm">{ar ? 'معرف نسخة لمراجعة مستقلة' : 'Release ID for independent review'}
            <input className="input w-full" value={resumeId} disabled={lock} onChange={event => setResumeId(event.target.value.trim())}
                onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); if (/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(resumeId)) resume() } }} />
        </label>
        <button type="button" className="btn-secondary" disabled={lock || !/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(resumeId)} onClick={resume}>{ar ? 'قراءة النسخة المحفوظة' : 'Read saved release'}</button>
        {view.reference && <>
            <label className="block">{ar ? 'مدينة بوسطة' : 'Provider city'}
                <select className="input w-full" value={view.city} disabled={lock} onChange={e => setView(previous => ({ ...previous, city: e.target.value, district: '', run: null, choices: {}, release: null }))}>
                    <option value="">—</option>{view.reference.places.filter(p => p.type === 'city').map(p => <option key={p.id} value={p.id}>{p.name} — {p.id}</option>)}
                </select>
            </label>
            <button type="button" className="btn-secondary" disabled={lock || !view.city} onClick={sync}>{ar ? 'مزامنة مناطق المدينة للمراجعة' : 'Synchronize city districts for review'}</button>
            <label className="block text-sm">{ar ? 'سبب تهيئة مرجع الناقل' : 'Geography provider bootstrap reason'}<input className="input w-full" value={bootstrapReason} maxLength={200} disabled={lock} onChange={e => { setBootstrapReason(e.target.value); setBootstrapApproved(false) }} /></label>
            <label className="block text-sm"><input type="checkbox" checked={bootstrapApproved} disabled={lock} onChange={e => setBootstrapApproved(e.target.checked)} /> {ar ? 'أوافق على تسجيل بيانات مرجع بوسطة فقط ومزامنة مسودة دون نشر أو تفعيل الناقل' : 'Register missing Bosta geography metadata and synchronize a draft only; do not publish or activate the carrier'}</label>
            <button type="button" className="btn-secondary" disabled={lock || !view.city || !bootstrapApproved || !bootstrapReason.trim()} onClick={bootstrap}>{ar ? 'تهيئة المرجع الحقيقي وحفظ مسودة' : 'Bootstrap verified geography provider and draft'}</button>
            {!view.reference.catalogReleaseId && <p role="status">{ar ? 'يلزم كتالوج Shopixy منشور قبل قبول الربط.' : 'A published Shopixy catalog is required before accepting mappings.'}</p>}
        </>}
        <label className="block text-sm">{ar ? 'معرف مزامنة محفوظة' : 'Saved reference sync ID'}<input className="input w-full" value={syncId} disabled={lock} onChange={e => setSyncId(e.target.value.trim())} /></label>
        <button type="button" className="btn-secondary" disabled={lock || !view.reference || !/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(syncId)} onClick={resumeSync}>{ar ? 'قراءة مرجع محفوظ' : 'Read saved reference draft'}</button>
        {view.run && <>
            <label className="block">{ar ? 'منطقة الوجهة' : 'Destination district'}
                <select className="input w-full" value={view.district} disabled={lock} onChange={e => setView(previous => ({ ...previous, district: e.target.value, choices: {}, release: null }))}>
                    <option value="">—</option>{view.run.candidates.filter(c => c.providerPlaceType === 'district' && c.providerParentPlaceId === view.city).map(c => <option key={c.id} value={c.providerPlaceId} disabled={!c.serviceable}>{c.providerDisplayName} — {c.providerPlaceId}{c.serviceable ? '' : ' (unserviceable)'}</option>)}
                </select>
            </label>
            {selectedCandidates.map(c => <label key={c.id} className="block text-sm">{c.providerPlaceType}: {c.providerDisplayName} — {c.providerPlaceId}{c.serviceable ? '' : ' (unserviceable)'}
                <select aria-label={`Canonical ${c.providerPlaceType}`} className="input w-full" value={view.choices[c.id] || ''} disabled={lock || !!view.release}
                    onChange={e => setView(previous => ({ ...previous, choices: { ...previous.choices, [c.id]: e.target.value } }))}>
                    <option value="">{ar ? 'اختر هوية Shopixy' : 'Choose Shopixy identity'}</option>
                    {view.reference.canonicalPlaces.filter(p => c.providerPlaceType === 'country' ? p.placeKind === 'Country' || p.placeKind === 0 :
                        c.providerPlaceType === 'city' ? ['AdministrativeArea', 'Locality', 1, 2].includes(p.placeKind) : ['Locality', 'District', 2, 3].includes(p.placeKind))
                        .map(p => <option key={p.id} value={p.id}>{p.canonicalCode} — {p.id}</option>)}
                </select>
            </label>)}
            <button type="button" className="btn-secondary" disabled={lock || !!view.release || !view.reference.catalogReleaseId || selectedCandidates.length !== 3 || selectedCandidates.some(c => !c.serviceable || !view.choices[c.id])} onClick={accept}>{ar ? 'حفظ مسودة الربط المراجع' : 'Save reviewed mapping draft'}</button>
        </>}
        {view.release && <div className="space-y-2 text-sm">
            <p>{view.release} — {String(view.readback?.status)} — {view.preview?.failureCode}</p>
            <ul>{view.readback?.mappings?.map(m => <li key={`${m.providerPlaceType}:${m.providerPlaceId}`}>{m.providerPlaceType}: {m.providerPlaceId} → {m.canonicalPlaceId}</li>)}</ul>
            <button type="button" className="btn-secondary" disabled={lock || !view.preview?.succeeded} onClick={() => publish('validate')}>{ar ? 'اعتماد المراجعة المستقلة' : 'Validate independent review'}</button>
            <button type="button" className="btn-secondary" disabled={lock || !view.preview?.succeeded} onClick={() => publish('publish')}>{ar ? 'نشر النسخة المراجعة' : 'Publish reviewed revision'}</button>
        </div>}
        {message && <p role="alert">{message}</p>}
        {busy && <p role="status">{ar ? 'جار التنفيذ' : 'Working…'}</p>}
    </section>
}
