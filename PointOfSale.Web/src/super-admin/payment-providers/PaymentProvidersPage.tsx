import { useCallback, useEffect, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { AlertTriangle, ShieldCheck } from 'lucide-react'
import { useToast } from '../../components/Toast'
import { ConfigurationSection } from './components/ConfigurationSection'
import { ConnectionResultPanel } from './components/ConnectionResultPanel'
import { DevelopmentOnlinePaymentSimulationPanel } from './components/DevelopmentOnlinePaymentSimulationPanel'
import { OperationalMessages } from './components/OperationalMessages'
import { PaymentProviderDetails } from './components/PaymentProviderDetails'
import { PaymentProviderLayout } from './components/PaymentProviderLayout'
import { PaymentProviderList } from './components/PaymentProviderList'
import { ProviderDashboard } from './components/ProviderDashboard'
import { ProviderHealthCard } from './components/ProviderHealthCard'
import { ProviderSessionReconciliationPanel } from './components/ProviderSessionReconciliationPanel'
import { RotateSecretDialog } from './components/RotateSecretDialog'
import { SaveBar } from './components/SaveBar'
import { SecretCard } from './components/SecretCard'
import { TestConnectionButton } from './components/TestConnectionButton'
import {
    getPaymentProviderConfiguration,
    getPaymentProviderDashboard,
    getPaymentProviders,
    getPaymentProviderSecrets,
    reconcilePaymentProviderSessionNotCreated,
    rotatePaymentProviderSecret,
    savePaymentProviderConfiguration,
    simulateDevelopmentOnlinePaymentSuccess,
    testPaymentProviderConnection,
} from './paymentProvidersService'
import { usePaymentProviderAdministrationState } from './usePaymentProviderAdministrationState'
import type {
    PaymentProviderConfiguration,
    PaymentProviderSecretMetadata,
    PaymentProviderSessionReconciliationRequest,
    RotatePaymentProviderSecretRequest,
} from './paymentProviderTypes'

function getErrorMessage(error: unknown, fallback: string) {
    return error instanceof Error && error.message ? error.message : fallback
}

export default function PaymentProvidersPage() {
    const { showError, showSuccess } = useToast()
    const [state, dispatch] = usePaymentProviderAdministrationState()
    const secretReplacementsRef = useRef<Record<string, string>>({})
    const [secretReplacementDirty, setSecretReplacementDirty] = useState({ apiKey: false, secretKey: false, hmacSecret: false })
    const [formResetVersion, setFormResetVersion] = useState(0)
    const [rotatingSecretType, setRotatingSecretType] = useState<string | null>(null)

    const providersQuery = useQuery({
        queryKey: ['super-admin-payment-providers'],
        queryFn: getPaymentProviders,
        staleTime: 60_000,
    })

    const dashboardQuery = useQuery({
        queryKey: ['super-admin-payment-provider-dashboard'],
        queryFn: getPaymentProviderDashboard,
        staleTime: 60_000,
    })

    const selectedProviderQuery = useQuery({
        queryKey: ['super-admin-payment-provider', state.selectedProviderCode],
        queryFn: () => getPaymentProviderConfiguration(state.selectedProviderCode!),
        enabled: Boolean(state.selectedProviderCode),
    })

    const secretsQuery = useQuery({
        queryKey: ['super-admin-payment-provider-secrets', state.selectedProviderCode],
        queryFn: () => getPaymentProviderSecrets(state.selectedProviderCode!),
        enabled: Boolean(state.selectedProviderCode),
    })

    const saveMutation = useMutation({
        mutationFn: () =>
            savePaymentProviderConfiguration(state.selectedProviderCode!, {
                providerCode: state.selectedProviderCode!,
                enabled: state.configuration?.enabled,
                environment: state.configuration?.environment,
                configuration: state.configuration?.configuration ?? {},
                metadata: state.configuration?.metadata ?? {},
                secretReplacements: secretReplacementsRef.current,
            }),
        onSuccess: (response) => {
            secretReplacementsRef.current = {}
            setSecretReplacementDirty({ apiKey: false, secretKey: false, hmacSecret: false })
            setFormResetVersion((value) => value + 1)
            dispatch({
                type: 'save:success',
                configuration: response.provider,
                errors: response.validationErrors,
            })
            showSuccess('CommerceHub payment provider configuration saved.')
        },
        onError: (error) => {
            const message = getErrorMessage(error, 'Unable to save payment provider configuration.')
            dispatch({ type: 'save:error', errors: [message] })
            showError(message)
        },
    })

    const testConnectionMutation = useMutation({
        mutationFn: () => testPaymentProviderConnection(state.selectedProviderCode!),
        onSuccess: (response) => {
            dispatch({ type: 'test:success', result: response.result, health: response.health })
            if (response.result.success) {
                showSuccess(response.result.message)
            } else {
                showError(response.result.message)
            }
        },
        onError: (error) => {
            const message = getErrorMessage(error, 'Unable to test payment provider connection.')
            dispatch({ type: 'test:error', errors: [message] })
            showError(message)
        },
    })

    const rotateSecretMutation = useMutation({
        mutationFn: (payload: RotatePaymentProviderSecretRequest) =>
            rotatePaymentProviderSecret(state.selectedProviderCode!, payload),
        onSuccess: (response) => {
            setRotatingSecretType(null)
            secretsQuery.refetch()
            selectedProviderQuery.refetch()
            showSuccess(`${response.secret.secretType} rotated to version ${response.secret.version}.`)
        },
        onError: (error) => {
            const message = getErrorMessage(error, 'Unable to rotate provider secret.')
            showError(message)
        },
    })

    const reconcileSessionMutation = useMutation({
        mutationFn: (payload: PaymentProviderSessionReconciliationRequest) =>
            reconcilePaymentProviderSessionNotCreated(state.selectedProviderCode!, payload),
        onSuccess: (response) => {
            dashboardQuery.refetch()
            selectedProviderQuery.refetch()
            showSuccess(response.message)
        },
        onError: (error) => {
            showError(getErrorMessage(error, 'Unable to reconcile the provider session.'))
        },
    })

    const developmentSuccessMutation = useMutation({
        mutationFn: ({ paymentId, idempotencyKey, reason }: { paymentId: string; idempotencyKey: string; reason: string }) =>
            simulateDevelopmentOnlinePaymentSuccess(paymentId, {
                idempotencyKey,
                reason,
                administratorConfirmation: true,
            }),
        onSuccess: (response) => {
            dashboardQuery.refetch()
            showSuccess(response.message)
        },
        onError: (error) => {
            showError(getErrorMessage(error, 'Unable to execute the Development payment success simulation.'))
        },
    })

    useEffect(() => {
        if (providersQuery.isFetching) {
            dispatch({ type: 'load:start' })
            return
        }

        if (providersQuery.isError) {
            dispatch({
                type: 'load:error',
                errors: [getErrorMessage(providersQuery.error, 'Unable to load payment providers.')],
            })
            return
        }

        if (providersQuery.data) {
            dispatch({ type: 'load:success', providers: providersQuery.data.providers })
        }
    }, [providersQuery.data, providersQuery.error, providersQuery.isError, providersQuery.isFetching, dispatch])

    useEffect(() => {
        if (selectedProviderQuery.isError) {
            dispatch({
                type: 'load:error',
                errors: [getErrorMessage(selectedProviderQuery.error, 'Unable to load payment provider details.')],
            })
            return
        }

        if (selectedProviderQuery.data) {
            dispatch({
                type: 'configuration:success',
                configuration: selectedProviderQuery.data.provider,
                errors: selectedProviderQuery.data.validationErrors,
            })
        }
    }, [selectedProviderQuery.data, selectedProviderQuery.error, selectedProviderQuery.isError, dispatch])

    function handleProviderSelect(providerCode: string) {
        secretReplacementsRef.current = {}
        setSecretReplacementDirty({ apiKey: false, secretKey: false, hmacSecret: false })
        setFormResetVersion((value) => value + 1)
        setRotatingSecretType(null)
        dispatch({ type: 'select', providerCode })
    }

    function handleSave() {
        if (!state.selectedProviderCode || !state.configuration) {
            showError('Select a provider before saving.')
            return
        }

        if (state.validationErrors.length > 0) {
            showError('Resolve validation errors before saving.')
            return
        }

        dispatch({ type: 'save:start' })
        saveMutation.mutate()
    }

    function handleCancel() {
        secretReplacementsRef.current = {}
        setSecretReplacementDirty({ apiKey: false, secretKey: false, hmacSecret: false })
        setFormResetVersion((value) => value + 1)
        dispatch({ type: 'configuration:reset' })
    }

    function handleTestConnection() {
        if (!state.selectedProviderCode || !state.configuration) {
            showError('Select a provider before testing the connection.')
            return
        }

        if (state.dirty) {
            showError('Save provider configuration changes before testing the connection.')
            return
        }

        dispatch({ type: 'test:start' })
        testConnectionMutation.mutate()
    }

    function handleConfigurationChange(configuration: PaymentProviderConfiguration) {
        dispatch({ type: 'configuration:update', configuration })
    }

    function handleSecretReplacementChange(key: 'apiKey' | 'secretKey' | 'hmacSecret', value: string) {
        const trimmedValue = value.trim()
        if (trimmedValue) {
            secretReplacementsRef.current[key] = trimmedValue
        } else {
            delete secretReplacementsRef.current[key]
        }

        setSecretReplacementDirty((current) => ({
            ...current,
            [key]: Boolean(trimmedValue),
        }))
        dispatch({ type: 'dirty:set', dirty: true })
    }

    const handleValidationChange = useCallback((errors: string[]) => {
        dispatch({ type: 'validation:set', errors })
    }, [dispatch])

    function handleRotateSecret(payload: RotatePaymentProviderSecretRequest) {
        if (!state.selectedProviderCode) {
            showError('Select a provider before rotating secrets.')
            return
        }

        rotateSecretMutation.mutate(payload)
    }

    const secretsByType = (secretsQuery.data?.secrets ?? []).reduce<Record<string, PaymentProviderSecretMetadata[]>>(
        (groups, secret) => {
            groups[secret.secretType] = [...(groups[secret.secretType] ?? []), secret]
            return groups
        },
        {},
    )

    const secretTypes = ['API Key', 'HMAC Secret']

    const header = (
        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
            <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                <ShieldCheck className="h-4 w-4" />
                CommerceHub / Administration / Payment Providers
            </div>
            <h1 className="mt-5 text-3xl font-semibold text-slate-950">Payment Providers</h1>
            <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-600">
                Platform-owned administration for CommerceHub payment providers. Saved executable provider settings
                control which online payment methods CommerceHub can offer.
            </p>
        </section>
    )

    return (
        <PaymentProviderLayout
            header={header}
            list={
                <>
                    <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">Provider List</p>
                        <h2 className="mt-2 text-xl font-semibold text-slate-950">Available providers</h2>
                        <p className="mt-2 text-sm leading-6 text-slate-600">
                            Rendered from the administration API.
                        </p>
                    </div>
                    <PaymentProviderList
                        providers={state.providerList}
                        loading={state.loading}
                        selectedProviderCode={state.selectedProviderCode}
                        onSelect={handleProviderSelect}
                    />
                </>
            }
            details={
                <>
                    <ProviderDashboard
                        dashboard={dashboardQuery.data}
                        loading={dashboardQuery.isLoading}
                        refreshing={dashboardQuery.isFetching}
                        onRefresh={() => {
                            dashboardQuery.refetch()
                            if (state.selectedProviderCode) {
                                selectedProviderQuery.refetch()
                                secretsQuery.refetch()
                            }
                        }}
                    />
                    {state.validationErrors.length > 0 ? (
                        <div className="rounded-2xl border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
                            <div className="flex items-center gap-2 font-semibold">
                                <AlertTriangle className="h-4 w-4" />
                                Validation Summary
                            </div>
                            <ul className="mt-2 list-disc space-y-1 pl-5">
                                {state.validationErrors.map((error) => (
                                    <li key={error}>{error}</li>
                                ))}
                            </ul>
                        </div>
                    ) : null}
                    <PaymentProviderDetails provider={state.configuration} />
                    <ConfigurationSection
                        key={`${state.selectedProviderCode ?? 'none'}-${formResetVersion}`}
                        provider={state.configuration}
                        secretReplacementDirty={secretReplacementDirty}
                        onChange={handleConfigurationChange}
                        onSecretReplacementChange={handleSecretReplacementChange}
                        onValidationChange={handleValidationChange}
                    />
                    <ProviderHealthCard health={state.configuration?.health} />
                    <OperationalMessages health={state.configuration?.health} />
                    <section className="space-y-4">
                        <div className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                                Secret Lifecycle
                            </p>
                            <h3 className="mt-2 text-lg font-semibold text-slate-950">Version history</h3>
                        </div>
                        {secretTypes.map((secretType) => {
                            const versions = (secretsByType[secretType] ?? [])
                                .slice()
                                .sort((left, right) => right.version - left.version)
                            const current = versions.find((secret) => secret.status === 'Active')

                            return (
                                <SecretCard
                                    key={secretType}
                                    secretType={secretType}
                                    current={current}
                                    versions={versions}
                                    onRotate={setRotatingSecretType}
                                />
                            )
                        })}
                    </section>
                    <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                        <div className="flex flex-wrap items-center justify-between gap-3">
                            <div>
                                <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                                    Provider Action
                                </p>
                                <h3 className="mt-2 text-lg font-semibold text-slate-950">Test connection</h3>
                            </div>
                            <TestConnectionButton
                                running={state.runningTest || testConnectionMutation.isPending}
                                disabled={!state.selectedProviderCode || !state.configuration || state.dirty}
                                onClick={handleTestConnection}
                            />
                        </div>
                    </section>
                    <ConnectionResultPanel result={state.latestResult} />
                    {import.meta.env.DEV ? (
                        <DevelopmentOnlinePaymentSimulationPanel
                            submitting={developmentSuccessMutation.isPending}
                            result={developmentSuccessMutation.data}
                            onSubmit={(paymentId, request) => developmentSuccessMutation.mutate({
                                paymentId,
                                idempotencyKey: request.idempotencyKey,
                                reason: request.reason,
                            })}
                        />
                    ) : null}
                    {state.selectedProviderCode?.trim().toUpperCase() === 'PAYMOB' ? (
                        <ProviderSessionReconciliationPanel
                            key={state.selectedProviderCode}
                            submitting={reconcileSessionMutation.isPending}
                            disabled={state.dirty}
                            onSubmit={(payload) => reconcileSessionMutation.mutate(payload)}
                        />
                    ) : null}
                    <SaveBar
                        saving={state.saving || saveMutation.isPending}
                        disabled={!state.selectedProviderCode || !state.configuration || state.validationErrors.length > 0}
                        dirty={state.dirty}
                        successMessage={state.successMessage}
                        onSave={handleSave}
                        onCancel={handleCancel}
                    />
                    <RotateSecretDialog
                        open={Boolean(rotatingSecretType)}
                        secretType={rotatingSecretType ?? ''}
                        rotating={rotateSecretMutation.isPending}
                        onClose={() => setRotatingSecretType(null)}
                        onRotate={handleRotateSecret}
                    />
                </>
            }
        />
    )
}
