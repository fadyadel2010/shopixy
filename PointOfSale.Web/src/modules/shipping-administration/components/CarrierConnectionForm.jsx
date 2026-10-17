import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { runCarrierConnectionTest } from '../shippingAdministrationApi'
import BostaPickupSelector from './BostaPickupSelector'
import BostaGeographySetup from './BostaGeographySetup'
import BostaNativeDestinationSelector from './BostaNativeDestinationSelector'
import { connectionFailureCategory } from '../connectionDiagnostics'

const empty = () => ({ apiKey: '', apiSecret: '', accountId: '', webhookSecret: '', extraConfig: {} })
const text = value => value?.trim() || null
const operational = (carrier, field) => carrier === 'bosta' ? field.key !== 'apiKey' : carrier === 'dhl' && field.key === 'accountId'
export default function CarrierConnectionForm({ account, definition, onMutated, onBusy, onOutcome }) {
    const { t, i18n } = useTranslation()
    const [form, setForm] = useState(empty)
    const savedEnvironment = account.environment || account.integrationReadiness?.environment || 'Production'
    const [environment, setEnvironment] = useState(savedEnvironment)
    const [busy, setBusy] = useState('')
    const [feedback, setFeedback] = useState(null)
    const target = useRef(null)
    const pending = useRef(false)
    useEffect(() => {
        const identity = {}; target.current = identity; pending.current = false
        setForm(empty()); setEnvironment(savedEnvironment); setFeedback(null); setBusy('')
        return () => { target.current = null }
    }, [account.id, account.carrierCode, savedEnvironment])
    const fields = definition?.credentialFields || []
    const saved = account.savedCredentialFields || []
    const value = field => field.target.startsWith('ExtraConfig.') ? form.extraConfig[field.target.slice(12)] || '' : form[field.target[0].toLowerCase() + field.target.slice(1)] || ''
    const configured = fields.some(field => field.required) && fields.every(field => !field.required || saved.includes(field.key))
    const dirty = fields.some(field => text(value(field))) || environment !== savedEnvironment
    const change = (field, next) => {
        setForm(previous => field.target.startsWith('ExtraConfig.')
            ? { ...previous, extraConfig: { ...previous.extraConfig, [field.target.slice(12)]: next } }
            : { ...previous, [field.target[0].toLowerCase() + field.target.slice(1)]: next })
        setFeedback(null)
    }
    const submit = async (saveOnly) => {
        if (pending.current) return
        const identity = target.current
        if (saveOnly) {
            const missing = fields.filter(field => field.required && !text(value(field)) && !saved.includes(field.key))
            if (missing.length) {
                setFeedback({ error: true, message: t('shippingAdministration.connection.missing', { fields: missing.map(field => t(`shippingAdministration.connection.fields.${account.carrierCode}.${field.key}`, { defaultValue: field.label })).join(', ') }) })
                return
            }
        } else if (!configured || dirty) {
            setFeedback({ error: true, message: t('shippingAdministration.connection.saveFirst') }); return
        }
        pending.current = true; setBusy(saveOnly ? 'save' : 'test'); onBusy?.(saveOnly ? 'save' : 'test'); setFeedback(null)
        if (!saveOnly) onOutcome?.({ status: 'Pending' })
        try {
            const result = await runCarrierConnectionTest(account.id, saveOnly ? {
                apiKey: text(form.apiKey), apiSecret: text(form.apiSecret), accountId: text(form.accountId),
                webhookSecret: text(form.webhookSecret), apiUrl: null, useSandbox: definition.environmentSupported && environment === 'Sandbox',
                extraConfig: Object.fromEntries(Object.entries(form.extraConfig).filter(([, v]) => text(v)).map(([k, v]) => [k, v.trim()])), saveOnly: true,
            } : { useSavedCredentials: true, saveOnly: false, useSandbox: false })
            if (result?.succeeded !== true) throw new Error(result?.failureCode || t('shippingAdministration.connection.failed'))
            if (target.current !== identity) return
            if (saveOnly) setForm(empty())
            // Readback decides whether a retained/no-op save preserved the test.
            // A save is neither a failed connection nor a new successful test.
            onOutcome?.(saveOnly ? null : { status: 'Succeeded' })
            setFeedback({ error: false, message: t(saveOnly ? 'shippingAdministration.connection.saved' : 'shippingAdministration.connection.authenticated') })
            await onMutated()
        } catch (error) {
            if (target.current !== identity) return
            if (!saveOnly) onOutcome?.({ status: 'Failed' })
            setFeedback({ error: true, message: `${t('shippingAdministration.connection.failed')} — ${error.message || ''}` })
            await onMutated().catch(() => {})
        } finally {
            if (target.current === identity) { pending.current = false; setBusy(''); onBusy?.('') }
        }
    }
    const renderField = field => {
        const label = t(`shippingAdministration.connection.fields.${account.carrierCode}.${field.key}`, { defaultValue: field.label })
        const id = `carrier-${account.id}-${field.key}`
        return <label key={field.key} htmlFor={id} className="block space-y-1 text-sm text-gray-700">
            <span>{label}{field.required ? ' *' : ''}</span>
            <input id={id} name={field.target} className="input min-w-0 w-full" dir="ltr" type={field.isSecret ? 'password' : 'text'}
                autoComplete="new-password" value={value(field)} disabled={!!busy}
                required={field.required && !saved.includes(field.key)}
                placeholder={saved.includes(field.key) ? t('shippingAdministration.connection.masked') : ''}
                onChange={event => change(field, event.target.value)} />
        </label>
    }
    return <form className="mt-5 space-y-3 rounded-lg border border-gray-200 bg-gray-50 p-4" onSubmit={event => { event.preventDefault(); submit(true) }}>
        <h4 className="font-semibold text-gray-900">{t('shippingAdministration.connection.credentials')}</h4>
        <p className="text-sm text-gray-600">{t('shippingAdministration.connection.retained')}</p>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">{fields.filter(field => !operational(account.carrierCode, field)).map(renderField)}</div>
        {fields.some(field => operational(account.carrierCode, field)) && <details className="space-y-3">
            <summary className="cursor-pointer text-sm font-medium">{t('shippingAdministration.connection.optional')}</summary>
            <p className="text-sm text-gray-600">{t('shippingAdministration.connection.operationBoundary')}</p>
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2">{fields.filter(field => operational(account.carrierCode, field)).map(renderField)}</div>
        </details>}
        {account.carrierCode === 'bosta' && <BostaPickupSelector accountId={account.id} environment={savedEnvironment}
            configurationVersion={`${account.activeCredentialReferenceId || ''}|${account.lastConnectionTest?.id || ''}|${account.connectionTestIsCurrent === true}`}
            disabled={!!busy || dirty || !configured} onSelected={id => change({ target: 'ExtraConfig.businessLocationId' }, id)} />}
        {account.carrierCode === 'bosta' && <>
            <BostaNativeDestinationSelector accountId={account.id} environment={savedEnvironment}
                configurationVersion={account.activeCredentialReferenceId || ''} disabled={!!busy || dirty || !configured} />
            <details><summary>{t('shippingAdministration.native.canonicalAdvanced')}</summary>
                <BostaGeographySetup accountId={account.id} environment={savedEnvironment}
                    configurationVersion={account.activeCredentialReferenceId || ''} disabled={!!busy || dirty || !configured} />
            </details>
        </>}
        {definition?.environmentSupported && <label className="block text-sm text-gray-700" htmlFor={`connection-environment-${account.id}`}>
            {t('shippingAdministration.account.environment')}
            <select id={`connection-environment-${account.id}`} className="input mt-1 w-full" value={environment} disabled={!!busy} onChange={event => setEnvironment(event.target.value)}>
                <option value="Production">{t('shippingAdministration.states.Production')}</option>
                <option value="Sandbox">{t('shippingAdministration.connection.testEnvironment')}</option>
            </select>
        </label>}
        {account.carrierCode === 'mylerz' && <p className="text-sm text-amber-800">{t('shippingAdministration.connection.mylerzBoundary')}</p>}
        <div className="flex flex-wrap gap-3">
            <button type="submit" disabled={!!busy} className="btn-secondary">{busy === 'save' ? t('shippingAdministration.connection.saving') : t('common.save')}</button>
            <button type="button" disabled={!!busy || !configured || dirty || definition?.connectionTestMode !== 'Remote'} onClick={() => submit(false)} className="btn-secondary">{busy === 'test' ? t('shippingAdministration.connection.testing') : t('shippingAdministration.connection.test')}</button>
        </div>
        {(!configured || dirty) && <p className="text-sm text-gray-600">{t('shippingAdministration.connection.saveFirst')}</p>}
        {busy && <p role="status" className="text-sm text-blue-700">{t(busy === 'save' ? 'shippingAdministration.connection.saving' : 'shippingAdministration.connection.testing')}</p>}
        {feedback && <p role={feedback.error ? 'alert' : 'status'} className={`break-words text-sm ${feedback.error ? 'text-red-700' : 'text-green-800'}`}>{feedback.message}</p>}
        {((feedback?.error && feedback.message) || (account.connectionTestIsCurrent && account.lastConnectionTest?.failureCode)) && <p className="text-sm text-red-700">
            {t(`shippingAdministration.connection.errors.${connectionFailureCategory(feedback?.error ? feedback.message : account.lastConnectionTest.failureCode)}`)}
        </p>}
        {account.connectionTestIsCurrent && account.lastConnectionTest && <p className="text-sm text-gray-700">
            {t('shippingAdministration.connection.lastTest')}: <bdi>{new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(account.lastConnectionTest.testedAtUtc))}</bdi>
            {' — '}{t(`shippingAdministration.states.${account.lastConnectionTest.status}`)}
            {' — '}{t(`shippingAdministration.states.${account.lastConnectionTest.environment}`)}
            {account.lastConnectionTest.failureCode && <bdi> — {account.lastConnectionTest.failureCode}</bdi>}
        </p>}
    </form>
}
