import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../../../services/api'

const base = '/admin/geographic/catalog'
const isStatus = (value, name, number) => value === name || value === number

export default function CanonicalCatalogBootstrap({ accountId, environment, configurationVersion, disabled }) {
    const { i18n } = useTranslation()
    const ar = i18n.language.startsWith('ar')
    const [text, setText] = useState('')
    const [preview, setPreview] = useState(null)
    const [releases, setReleases] = useState([])
    const [review, setReview] = useState(null)
    const [releaseId, setReleaseId] = useState('')
    const [reason, setReason] = useState('')
    const [sourceReviewed, setSourceReviewed] = useState(false)
    const [publishApproved, setPublishApproved] = useState(false)
    const [page, setPage] = useState(0)
    const [busy, setBusy] = useState(false)
    const [error, setError] = useState('')
    const scope = useRef(null)
    const pending = useRef(false)
    useEffect(() => {
        scope.current = {}; pending.current = false
        setText(''); setPreview(null); setReleases([]); setReview(null); setReleaseId(''); setReason('')
        setSourceReviewed(false); setPublishApproved(false); setPage(0); setBusy(false); setError('')
        return () => { scope.current = null }
    }, [accountId, environment, configurationVersion])
    const run = async operation => {
        if (disabled || pending.current) return
        const identity = scope.current
        pending.current = true; setBusy(true); setError('')
        try {
            const apply = await operation()
            if (scope.current === identity) apply()
        } catch (failure) {
            if (scope.current === identity) setError(failure.response?.data?.failureCode || failure.response?.data?.code || failure.message)
        } finally {
            if (scope.current === identity) { pending.current = false; setBusy(false) }
        }
    }
    const inspect = () => run(async () => {
        const { data } = await api.post(`${base}/import/preview`, JSON.parse(text))
        return () => { setPreview(data); setReview(null); setPage(0) }
    })
    const stage = () => run(async () => {
        const { data } = await api.post(`${base}/import/drafts`, { manifest: JSON.parse(text), expectedManifestChecksumSha256: preview.manifestChecksumSha256, reason })
        if (!data.succeeded) throw new Error(data.failureCode)
        const saved = (await api.get(`${base}/releases/${data.resourceId}`)).data
        return () => { setReview(saved); setReleaseId(data.resourceId); setPreview(null); setSourceReviewed(false); setPublishApproved(false); setPage(0) }
    })
    const list = () => run(async () => {
        const { data } = await api.get(`${base}/releases`)
        return () => setReleases(data)
    })
    const read = () => run(async () => {
        const { data } = await api.get(`${base}/releases/${releaseId}`)
        return () => { setReview(data); setSourceReviewed(false); setPublishApproved(false); setPage(0) }
    })
    const act = action => run(async () => {
        const checksum = review.validation.contentChecksumSha256
        const payload = action === 'validate' ? { expectedContentChecksumSha256: checksum, sourceEvidenceUri: review.sourceEvidenceUri,
            sourceEvidenceSha256: review.sourceEvidenceSha256, confirmOfficialSourceReviewed: sourceReviewed, reason } : { expectedContentChecksumSha256: checksum, reason }
        const { data } = await api.post(`${base}/releases/${review.release.id}/${action}`, payload)
        if (!data.succeeded) throw new Error(data.failureCode)
        const saved = (await api.get(`${base}/releases/${review.release.id}`)).data
        return () => { setReview(saved); setPublishApproved(false) }
    })
    const locked = disabled || busy
    const rows = review?.places || []
    const validReason = !!reason.trim() && reason.length <= 200
    return <section aria-label={ar ? 'تهيئة كتالوج Shopixy المراجع' : 'Governed canonical catalog bootstrap'} className="space-y-3 rounded border p-3">
        <h5 className="font-semibold">{ar ? 'استيراد ومراجعة كتالوج Shopixy' : 'Import and review Shopixy canonical catalog'}</h5>
        <p className="text-xs">{ar ? 'استورد ملفا من مصدر رسمي موثق فقط. معرفات بوسطة ليست معرفات Shopixy. لا نشر تلقائي ولا تفعيل ناقل.' : 'Import only source-attributed canonical data. Bosta labels are not canonical Shopixy identities. No automatic publication or carrier activation. An independent reviewer must inspect the official source.'}</p>
        <label className="block text-sm">{ar ? 'ملف JSON للكتالوج' : 'Canonical catalog JSON manifest'}
            <textarea className="input w-full font-mono" rows={5} value={text} disabled={locked}
                onChange={event => { setText(event.target.value); setPreview(null); setReview(null); setSourceReviewed(false); setPublishApproved(false) }} />
        </label>
        <button type="button" className="btn-secondary" disabled={locked || !text.trim()} onClick={inspect}>{ar ? 'فحص الاستيراد دون حفظ' : 'Preview import without saving'}</button>
        {preview && <div role="status">
            <p>{preview.placeCount} {ar ? 'مكان' : 'places'} / {preview.edgeCount} {ar ? 'رابط' : 'parent edges'} — SHA256: {preview.manifestChecksumSha256}</p>
            <ul>{preview.issues?.map((issue, index) => <li key={`${issue.code}:${issue.stableKey}:${index}`}>{issue.code} {issue.stableKey}</li>)}</ul>
        </div>}
        <label className="block text-sm">{ar ? 'سبب الإجراء' : 'Catalog action reason'}
            <input className="input w-full" value={reason} maxLength={200} disabled={locked} onChange={event => { setReason(event.target.value); setPublishApproved(false) }} />
        </label>
        <button type="button" className="btn-secondary" disabled={locked || !preview?.succeeded || !validReason} onClick={stage}>{ar ? 'حفظ مسودة الاستيراد فقط' : 'Stage canonical import draft only'}</button>
        <button type="button" className="btn-secondary" disabled={locked} onClick={list}>{ar ? 'قائمة النسخ للمراجعة' : 'List catalog releases for review'}</button>
        <label className="block text-sm">{ar ? 'معرف نسخة الكتالوج' : 'Catalog release ID'}
            <input className="input w-full" value={releaseId} disabled={locked} onChange={event => { setReleaseId(event.target.value.trim()); setReview(null); setPublishApproved(false) }} />
        </label>
        <ul>{releases.map(release => <li key={release.id}><button type="button" disabled={locked} className="underline" onClick={() => { setReleaseId(release.id); setReview(null); setPublishApproved(false) }}>{release.releaseKey} — {release.id} — {String(release.status)}</button></li>)}</ul>
        <button type="button" className="btn-secondary" disabled={locked || !/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(releaseId)} onClick={read}>{ar ? 'قراءة كتالوج محفوظ' : 'Read saved canonical catalog'}</button>
        {review && <div className="space-y-2 text-sm">
            <p>{review.release.releaseKey} — {review.release.id} — {String(review.release.status)}</p>
            <p>{review.sourceSummary}</p><p>{review.sourceEvidenceUri} — {review.sourceEvidenceSha256}</p>
            <p>{ar ? 'منشئ النسخة' : 'Editor'}: {review.release.createdByUserId}</p>
            <p>SHA256: {review.validation.contentChecksumSha256} {review.validation.failureCode}</p>
            <pre className="overflow-auto whitespace-pre-wrap">{JSON.stringify(review.countryProfiles, null, 2)}</pre>
            <table className="w-full"><caption>{ar ? 'الهويات المقترحة والأسماء والآباء' : 'Proposed identities, names and parents'}</caption>
                <thead><tr><th>{ar ? 'هوية' : 'Identity'}</th><th>{ar ? 'نوع ورمز' : 'Kind / code'}</th><th>{ar ? 'الأب والدولة' : 'Parent / country'}</th><th>{ar ? 'الأسماء' : 'Names'}</th></tr></thead>
                <tbody>{rows.slice(page * 50, (page + 1) * 50).map(place => <tr key={place.id}><td><div>{place.stableKey}</div><div>{place.id}</div></td><td>{String(place.placeKind)} / {place.canonicalCode}<br />{place.externalStandard} {place.externalStandardCode}</td><td><div>{place.parentPlaceId}</div><div>{place.countryPlaceId}</div></td><td>{place.names.map(name => `${name.languageTag}: ${name.name}`).join(' / ')}</td></tr>)}</tbody>
            </table>
            <button type="button" disabled={locked || page === 0} onClick={() => setPage(value => value - 1)}>{ar ? 'السابق' : 'Previous places'}</button>
            <span> {page + 1} / {Math.max(1, Math.ceil(rows.length / 50))} </span>
            <button type="button" disabled={locked || (page + 1) * 50 >= rows.length} onClick={() => setPage(value => value + 1)}>{ar ? 'التالي' : 'Next places'}</button>
            <label className="block"><input type="checkbox" checked={sourceReviewed} disabled={locked} onChange={event => setSourceReviewed(event.target.checked)} /> {ar ? 'راجعت المصدر الرسمي والهوية والتسلسل الهرمي بشكل مستقل' : 'I independently reviewed the official source, identities and hierarchy'}</label>
            <button type="button" className="btn-secondary" disabled={locked || !validReason || !sourceReviewed || !review.validation.succeeded || !isStatus(review.release.status, 'Draft', 0)} onClick={() => act('validate')}>{ar ? 'اعتماد مراجعة الكتالوج' : 'Validate independently reviewed catalog'}</button>
            <label className="block"><input type="checkbox" checked={publishApproved} disabled={locked} onChange={event => setPublishApproved(event.target.checked)} /> {ar ? 'أوافق صراحة على نشر هذه النسخة دون تفعيل الناقل' : 'I explicitly approve publishing this exact catalog revision, without carrier activation'}</label>
            <button type="button" className="btn-secondary" disabled={locked || !validReason || !publishApproved || !review.validation.succeeded || !isStatus(review.release.status, 'Validated', 1)} onClick={() => act('publish')}>{ar ? 'نشر كتالوج معتمد' : 'Publish approved canonical catalog'}</button>
        </div>}
        {error && <p role="alert">{error}</p>}
        {busy && <p role="status">{ar ? 'جار التنفيذ' : 'Working…'}</p>}
    </section>
}
