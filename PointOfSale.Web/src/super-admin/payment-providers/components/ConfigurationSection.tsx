import { Settings } from 'lucide-react'
import type { PaymentProviderConfiguration } from '../paymentProviderTypes'
import { PaymobConfigurationForm } from './PaymobConfigurationForm'

type SecretReplacementKey = 'apiKey' | 'secretKey' | 'hmacSecret'

export function ConfigurationSection({
    provider,
    secretReplacementDirty,
    onChange,
    onSecretReplacementChange,
    onValidationChange,
}: {
    provider: PaymentProviderConfiguration | null
    secretReplacementDirty: Record<SecretReplacementKey, boolean>
    onChange: (provider: PaymentProviderConfiguration) => void
    onSecretReplacementChange: (key: SecretReplacementKey, value: string) => void
    onValidationChange: (errors: string[]) => void
}) {
    if (!provider) {
        return (
            <div className="rounded-2xl border border-slate-200 bg-white px-5 py-8 text-sm text-slate-600">
                Select a provider to configure it.
            </div>
        )
    }

    if (provider.providerCode === 'PAYMOB') {
        return (
            <PaymobConfigurationForm
                provider={provider}
                secretReplacementDirty={secretReplacementDirty}
                onChange={onChange}
                onSecretReplacementChange={onSecretReplacementChange}
                onValidationChange={onValidationChange}
            />
        )
    }

    return (
        <div className="rounded-2xl border border-dashed border-slate-300 bg-slate-50 p-5">
            <div className="flex items-start gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-white text-slate-700 shadow-sm">
                    <Settings className="h-5 w-5" />
                </div>
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                        Provider Configuration
                    </p>
                    <h3 className="mt-2 text-lg font-semibold text-slate-950">
                        {provider.displayName} configuration
                    </h3>
                    <p className="mt-2 text-sm leading-6 text-slate-600">
                        This provider has not published a configuration form yet.
                    </p>
                </div>
            </div>
        </div>
    )
}
