import { useEffect, useMemo, type ReactNode } from 'react'
import { AlertTriangle, CheckCircle2, EyeOff, KeyRound, Link2, SlidersHorizontal, ToggleLeft } from 'lucide-react'
import type { PaymentProviderConfiguration } from '../paymentProviderTypes'

type SecretReplacementKey = 'apiKey' | 'hmacSecret'

type SecretReplacementDirtyState = Record<SecretReplacementKey, boolean>

interface PaymobConfigurationFormProps {
    provider: PaymentProviderConfiguration
    secretReplacementDirty: SecretReplacementDirtyState
    onChange: (provider: PaymentProviderConfiguration) => void
    onSecretReplacementChange: (key: SecretReplacementKey, value: string) => void
    onValidationChange: (errors: string[]) => void
}

const runtimeFields = [
    ['timeoutSeconds', 'Timeout'],
    ['retryCount', 'Retry Count'],
    ['requestTimeoutSeconds', 'Request Timeout'],
    ['maximumRetryAttempts', 'Maximum Retry Attempts'],
] as const

const SecretMask = '**************'

function textValue(provider: PaymentProviderConfiguration, key: string, fallback = '') {
    const value = provider.configuration[key]
    if (value === undefined || value === null) {
        return fallback
    }

    return String(value)
}

function boolValue(provider: PaymentProviderConfiguration, key: string, fallback = false) {
    const value = provider.configuration[key]
    if (typeof value === 'boolean') {
        return value
    }

    if (typeof value === 'string') {
        return value.toLowerCase() === 'true'
    }

    return fallback
}

function hasConfiguredCredential(provider: PaymentProviderConfiguration, key: 'apiKeyConfigured' | 'hmacSecretConfigured') {
    return boolValue(provider, key)
}

function validatePaymobDraft(
    provider: PaymentProviderConfiguration,
    secretReplacementDirty: SecretReplacementDirtyState,
) {
    const errors: string[] = []
    const environment = textValue(provider, 'environment', provider.environment)
    const currency = textValue(provider, 'currency')
    const integrationId = textValue(provider, 'integrationId')
    const iframeId = textValue(provider, 'iframeId')

    if (!textValue(provider, 'displayName', provider.displayName).trim()) {
        errors.push('Display Name is required.')
    }

    if (!textValue(provider, 'description', provider.description).trim()) {
        errors.push('Description is required.')
    }

    if (environment !== 'Sandbox' && environment !== 'Production') {
        errors.push('Unknown Environment.')
    }

    if (!/^[A-Z]{3}$/.test(currency)) {
        errors.push('Invalid Currency.')
    }

    if (!/^[1-9]\d*$/.test(integrationId)) {
        errors.push('Invalid Integration ID.')
    }

    if (!/^[1-9]\d*$/.test(iframeId)) {
        errors.push('Invalid iFrame ID.')
    }

    if (!hasConfiguredCredential(provider, 'apiKeyConfigured') && !secretReplacementDirty.apiKey) {
        errors.push('Missing API Key.')
    }

    if (!hasConfiguredCredential(provider, 'hmacSecretConfigured') && !secretReplacementDirty.hmacSecret) {
        errors.push('Missing HMAC Secret.')
    }

    runtimeFields.forEach(([key, label]) => {
        const value = textValue(provider, key, '0')
        if (!/^\d+$/.test(value)) {
            errors.push(`Negative ${label}.`)
        }
    })

    return errors
}

export function PaymobConfigurationForm({
    provider,
    secretReplacementDirty,
    onChange,
    onSecretReplacementChange,
    onValidationChange,
}: PaymobConfigurationFormProps) {
    const validationErrors = useMemo(
        () => validatePaymobDraft(provider, secretReplacementDirty),
        [provider, secretReplacementDirty],
    )

    useEffect(() => {
        onValidationChange(validationErrors)
    }, [onValidationChange, validationErrors])

    function updateConfiguration(key: string, value: unknown) {
        const nextConfiguration = {
            ...provider.configuration,
            [key]: value,
        }

        const nextProvider: PaymentProviderConfiguration = {
            ...provider,
            enabled: key === 'enabled' ? Boolean(value) : provider.enabled,
            environment: key === 'environment' ? String(value) : provider.environment,
            displayName: key === 'displayName' ? String(value) : provider.displayName,
            description: key === 'description' ? String(value) : provider.description,
            configuration: nextConfiguration,
        }

        if (key === 'environment') {
            nextProvider.configuration.sandbox = value === 'Sandbox'
        }

        onChange(nextProvider)
    }

    const apiKeyConfigured = hasConfiguredCredential(provider, 'apiKeyConfigured')
    const hmacSecretConfigured = hasConfiguredCredential(provider, 'hmacSecretConfigured')

    return (
        <div className="space-y-4">
            <FormSection icon={<ToggleLeft className="h-5 w-5" />} title="General">
                <div className="grid gap-4 md:grid-cols-2">
                    <label className="flex items-center justify-between rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm font-semibold text-slate-800">
                        Enabled
                        <input
                            type="checkbox"
                            checked={boolValue(provider, 'enabled', provider.enabled)}
                            onChange={(event) => updateConfiguration('enabled', event.target.checked)}
                            className="h-5 w-5 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500"
                        />
                    </label>
                    <label className="space-y-2">
                        <span className="text-sm font-semibold text-slate-700">Environment</span>
                        <select
                            value={textValue(provider, 'environment', provider.environment)}
                            onChange={(event) => updateConfiguration('environment', event.target.value)}
                            className="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-emerald-500 focus:outline-none focus:ring-2 focus:ring-emerald-100"
                        >
                            <option value="Sandbox">Sandbox</option>
                            <option value="Production">Production</option>
                        </select>
                    </label>
                    <TextField
                        label="Display Name"
                        value={textValue(provider, 'displayName', provider.displayName)}
                        onChange={(value) => updateConfiguration('displayName', value)}
                    />
                    <TextField
                        label="Currency"
                        value={textValue(provider, 'currency')}
                        maxLength={3}
                        onChange={(value) => updateConfiguration('currency', value.trim().toUpperCase())}
                    />
                    <label className="space-y-2 md:col-span-2">
                        <span className="text-sm font-semibold text-slate-700">Description</span>
                        <textarea
                            value={textValue(provider, 'description', provider.description)}
                            onChange={(event) => updateConfiguration('description', event.target.value)}
                            rows={3}
                            className="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-emerald-500 focus:outline-none focus:ring-2 focus:ring-emerald-100"
                        />
                    </label>
                </div>
            </FormSection>

            <FormSection icon={<KeyRound className="h-5 w-5" />} title="Credentials">
                <div className="grid gap-4 md:grid-cols-2">
                    <SecretReplacementField
                        label="API Key"
                        configured={apiKeyConfigured}
                        onChange={(value) => onSecretReplacementChange('apiKey', value)}
                    />
                    <SecretReplacementField
                        label="HMAC Secret"
                        configured={hmacSecretConfigured}
                        onChange={(value) => onSecretReplacementChange('hmacSecret', value)}
                    />
                </div>
            </FormSection>

            <FormSection icon={<Link2 className="h-5 w-5" />} title="Integration">
                <div className="grid gap-4 md:grid-cols-2">
                    <TextField
                        label="Integration ID"
                        value={textValue(provider, 'integrationId')}
                        onChange={(value) => updateConfiguration('integrationId', value)}
                    />
                    <TextField
                        label="iFrame ID"
                        value={textValue(provider, 'iframeId')}
                        onChange={(value) => updateConfiguration('iframeId', value)}
                    />
                    <TextField
                        label="Merchant Identifier"
                        value={textValue(provider, 'merchantIdentifier')}
                        onChange={(value) => updateConfiguration('merchantIdentifier', value)}
                    />
                    <TextField
                        label="Provider Account Name"
                        value={textValue(provider, 'providerAccountName')}
                        onChange={(value) => updateConfiguration('providerAccountName', value)}
                    />
                </div>
            </FormSection>

            <FormSection icon={<SlidersHorizontal className="h-5 w-5" />} title="Runtime">
                <div className="grid gap-4 md:grid-cols-2">
                    {runtimeFields.map(([key, label]) => (
                        <TextField
                            key={key}
                            label={label}
                            value={textValue(provider, key)}
                            type="number"
                            min={0}
                            onChange={(value) => updateConfiguration(key, value)}
                        />
                    ))}
                </div>
            </FormSection>

            <FormSection icon={<CheckCircle2 className="h-5 w-5" />} title="Callback Information">
                <div className="grid gap-4 md:grid-cols-2">
                    <ReadOnlyField label="Webhook Method" value={textValue(provider, 'webhookMethod', 'POST')} />
                    <ReadOnlyField label="Webhook URL" value={textValue(provider, 'webhookUrl')} />
                    <ReadOnlyField label="Browser Callback Method" value={textValue(provider, 'browserCallbackMethod', 'GET')} />
                    <ReadOnlyField label="Browser Callback URL" value={textValue(provider, 'browserCallbackUrl')} />
                </div>
            </FormSection>

            <FormSection icon={<EyeOff className="h-5 w-5" />} title="Advanced">
                <div className="grid gap-4 md:grid-cols-2">
                    <ReadOnlyField label="Provider Code" value={provider.providerCode} />
                    <ReadOnlyField label="Configuration Status" value={provider.status} />
                </div>
            </FormSection>

            {validationErrors.length > 0 ? (
                <div className="rounded-2xl border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
                    <div className="flex items-center gap-2 font-semibold">
                        <AlertTriangle className="h-4 w-4" />
                        Validation Summary
                    </div>
                    <ul className="mt-2 list-disc space-y-1 pl-5">
                        {validationErrors.map((error) => (
                            <li key={error}>{error}</li>
                        ))}
                    </ul>
                </div>
            ) : null}
        </div>
    )
}

function FormSection({
    icon,
    title,
    children,
}: {
    icon: ReactNode
    title: string
    children: ReactNode
}) {
    return (
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="mb-4 flex items-center gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-slate-700">
                    {icon}
                </div>
                <h3 className="text-lg font-semibold text-slate-950">{title}</h3>
            </div>
            {children}
        </section>
    )
}

function TextField({
    label,
    value,
    onChange,
    type = 'text',
    min,
    maxLength,
}: {
    label: string
    value: string
    onChange: (value: string) => void
    type?: 'text' | 'number'
    min?: number
    maxLength?: number
}) {
    return (
        <label className="space-y-2">
            <span className="text-sm font-semibold text-slate-700">{label}</span>
            <input
                type={type}
                min={min}
                maxLength={maxLength}
                value={value}
                onChange={(event) => onChange(event.target.value)}
                className="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-emerald-500 focus:outline-none focus:ring-2 focus:ring-emerald-100"
            />
        </label>
    )
}

function ReadOnlyField({ label, value }: { label: string; value: string }) {
    return (
        <label className="space-y-2">
            <span className="text-sm font-semibold text-slate-700">{label}</span>
            <input
                type="text"
                value={value}
                readOnly
                className="w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700"
            />
        </label>
    )
}

function SecretReplacementField({
    label,
    configured,
    onChange,
}: {
    label: string
    configured: boolean
    onChange: (value: string) => void
}) {
    return (
        <label className="space-y-2">
            <span className="text-sm font-semibold text-slate-700">{label}</span>
            <div className="flex gap-2">
                <input
                    type="password"
                    placeholder={configured ? SecretMask : ''}
                    autoComplete="new-password"
                    onChange={(event) => onChange(event.target.value)}
                    className="min-w-0 flex-1 rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-emerald-500 focus:outline-none focus:ring-2 focus:ring-emerald-100"
                />
                <button
                    type="button"
                    onClick={(event) => {
                        const input = event.currentTarget.parentElement?.querySelector('input')
                        input?.focus()
                    }}
                    className="inline-flex items-center rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50"
                >
                    Replace
                </button>
            </div>
        </label>
    )
}
