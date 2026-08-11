/**
 * D03 — Add Domain Wizard
 *
 * Constitutional source: DOMAIN-3.1 Canonical Blueprint §2.3 (C03), §2.4 (route: /storefront/domains/add)
 *
 * Full 7-step wizard UI for adding a new domain. Navigation between steps only.
 * No validation logic — all steps are interactive and navigable.
 */

import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ArrowLeft, ArrowRight, Check, Globe, Server, Shield, RefreshCw, Link, FileText } from 'lucide-react'

import {
    AuthoringButton,
    AuthoringValidationSummary,
    useAuthoringGuard,
} from '../../authoringPlatform'
import domainManagementService from '../services/domainManagementService'

const STEPS = [
    { id: 1, titleKey: 'storefront.domains.step1Domain', fallbackTitle: 'Domain', icon: Globe },
    { id: 2, titleKey: 'storefront.domains.step2Type', fallbackTitle: 'Type', icon: Server },
    { id: 3, titleKey: 'storefront.domains.step3Verification', fallbackTitle: 'Verification', icon: Shield },
    { id: 4, titleKey: 'storefront.domains.step4Ssl', fallbackTitle: 'SSL', icon: Shield },
    { id: 5, titleKey: 'storefront.domains.step5Redirect', fallbackTitle: 'Redirect', icon: RefreshCw },
    { id: 6, titleKey: 'storefront.domains.step6Canonical', fallbackTitle: 'Canonical', icon: Link },
    { id: 7, titleKey: 'storefront.domains.step7Review', fallbackTitle: 'Review', icon: FileText },
]

const STEP6_CANONICAL_POLICIES = [
    { value: 'enforce', labelKey: 'storefront.domains.canonicalEnforce', fallbackLabel: 'Enforce HTTPS' },
    { value: 'canonical', labelKey: 'storefront.domains.canonicalToPrimary', fallbackLabel: 'Canonical to Primary' },
]

function WizardStep1({ formData, updateField }) {
    const { t } = useTranslation()
    return (
        <div className="space-y-4">
            <div>
                <label htmlFor="wizard-hostname" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
                    {t('storefront.domains.hostnameLabel', 'Hostname')}
                </label>
                <input
                    id="wizard-hostname"
                    type="text"
                    value={formData.hostname || ''}
                    onChange={(e) => updateField('hostname', e.target.value)}
                    placeholder={t('storefront.domains.hostnamePlaceholder', 'e.g. shop.example.com')}
                    className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100 dark:focus:border-indigo-400"
                />
            </div>
            <p className="text-xs text-gray-500 dark:text-gray-400">
                {t(
                    'storefront.domains.hostnameHelp',
                    'Enter the domain name you own. It must be valid and not already registered to this tenant.',
                )}
            </p>
        </div>
    )
}

function WizardStep2({ formData, updateField }) {
    const { t } = useTranslation()
    const DOMAIN_TYPES = [
        { value: 'alias', labelKey: 'storefront.domains.typeAlias', fallbackLabel: 'Alias', desc: 'Additional domain serving the same storefront' },
        { value: 'preview', labelKey: 'storefront.domains.typePreview', fallbackLabel: 'Preview', desc: 'Staging or preview environment domain' },
    ]
    return (
        <div className="space-y-3">
            {DOMAIN_TYPES.map((type) => (
                <label
                    key={type.value}
                    className="flex items-start gap-3 rounded-lg border border-gray-200 p-3 dark:border-gray-700 dark:bg-gray-800/50"
                >
                    <input
                        type="radio"
                        name="domainType"
                        value={type.value}
                        checked={formData.domainType === type.value}
                        onChange={(e) => updateField('domainType', e.target.value)}
                        className="mt-1"
                    />
                    <div>
                        <p className="font-medium text-gray-900 dark:text-gray-100">
                            {t(type.labelKey, type.fallbackLabel)}
                        </p>
                        <p className="text-sm text-gray-500 dark:text-gray-400">{t(`${type.labelKey}Desc`, type.desc)}</p>
                    </div>
                </label>
            ))}
        </div>
    )
}

function WizardStep3({ formData, updateField }) {
    const { t } = useTranslation()
    const VERIFICATION_METHODS = [
        { value: 'txt', labelKey: 'storefront.domains.methodTxt', fallbackLabel: 'TXT Record', desc: 'Add a TXT record to your DNS' },
        { value: 'cname', labelKey: 'storefront.domains.methodCname', fallbackLabel: 'CNAME Record', desc: 'Point a CNAME record to the verification target' },
        { value: 'http', labelKey: 'storefront.domains.methodHttp', fallbackLabel: 'HTTP File', desc: 'Upload a verification file to your server' },
    ]
    return (
        <div className="space-y-3">
            <p className="text-sm text-gray-500 dark:text-gray-400">
                {t('storefront.domains.verificationHelp', 'Choose how you want to verify domain ownership.')}
            </p>
            {VERIFICATION_METHODS.map((method) => (
                <label
                    key={method.value}
                    className="flex items-start gap-3 rounded-lg border border-gray-200 p-3 dark:border-gray-700 dark:bg-gray-800/50"
                >
                    <input
                        type="radio"
                        name="verificationMethod"
                        value={method.value}
                        checked={formData.verificationMethod === method.value}
                        onChange={(e) => updateField('verificationMethod', e.target.value)}
                        className="mt-1"
                    />
                    <div>
                        <p className="font-medium text-gray-900 dark:text-gray-100">
                            {t(method.labelKey, method.fallbackLabel)}
                        </p>
                        <p className="text-sm text-gray-500 dark:text-gray-400">{t(`${method.labelKey}Desc`, method.desc)}</p>
                    </div>
                </label>
            ))}
        </div>
    )
}

function WizardStep4({ formData, updateField }) {
    const { t } = useTranslation()
    const SSL_PROVIDERS = [
        { value: 'managed', labelKey: 'storefront.domains.sslManaged', fallbackLabel: 'Platform managed', desc: 'Use the platform-managed certificate after ownership verification' },
    ]
    return (
        <div className="space-y-3">
            <p className="text-sm text-gray-500 dark:text-gray-400">
                {t('storefront.domains.sslHelp', 'Select the SSL provider for your domain certificate.')}
            </p>
            {SSL_PROVIDERS.map((provider) => (
                <label
                    key={provider.value}
                    className="flex items-start gap-3 rounded-lg border border-gray-200 p-3 dark:border-gray-700 dark:bg-gray-800/50"
                >
                    <input
                        type="radio"
                        name="sslProvider"
                        value={provider.value}
                        checked={formData.sslProvider === provider.value}
                        onChange={(e) => updateField('sslProvider', e.target.value)}
                        className="mt-1"
                    />
                    <div>
                        <p className="font-medium text-gray-900 dark:text-gray-100">
                            {t(provider.labelKey, provider.fallbackLabel)}
                        </p>
                        <p className="text-sm text-gray-500 dark:text-gray-400">{t(`${provider.labelKey}Desc`, provider.desc)}</p>
                    </div>
                </label>
            ))}
        </div>
    )
}

function WizardStep5() {
    const { t } = useTranslation()
    return (
        <div className="space-y-4">
            <p className="text-sm text-gray-500 dark:text-gray-400">
                {t(
                    'storefront.domains.redirectAfterCreationHelp',
                    'Redirects are configured after this domain has been created and verified.',
                )}
            </p>
        </div>
    )
}

function WizardStep6({ formData, updateField }) {
    const { t } = useTranslation()
    return (
        <div className="space-y-3">
            <p className="text-sm text-gray-500 dark:text-gray-400">
                {t('storefront.domains.canonicalHelp', 'Set canonical policy for your domain.')}
            </p>
            {STEP6_CANONICAL_POLICIES.map((policy) => (
                <label
                    key={policy.value}
                    className="flex items-start gap-3 rounded-lg border border-gray-200 p-3 dark:border-gray-700 dark:bg-gray-800/50"
                >
                    <input
                        type="radio"
                        name="canonicalPolicy"
                        value={policy.value}
                        checked={formData.canonicalPolicy === policy.value}
                        onChange={(e) => updateField('canonicalPolicy', e.target.value)}
                        className="mt-1"
                    />
                    <div>
                        <p className="font-medium text-gray-900 dark:text-gray-100">
                            {t(policy.labelKey, policy.fallbackLabel)}
                        </p>
                    </div>
                </label>
            ))}
        </div>
    )
}

function WizardStep7({ formData }) {
    const { t } = useTranslation()
    return (
        <div className="space-y-4">
            <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                {t('storefront.domains.reviewTitle', 'Review Your Settings')}
            </h3>
            <div className="space-y-3">
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-700/50">
                    <span className="text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                        {t('storefront.domains.hostname', 'Hostname')}
                    </span>
                    <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">{formData.hostname || '—'}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-700/50">
                    <span className="text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                        {t('storefront.domains.type', 'Type')}
                    </span>
                    <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">{formData.domainType || '—'}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-700/50">
                    <span className="text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                        {t('storefront.domains.verificationMethod', 'Verification Method')}
                    </span>
                    <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">{formData.verificationMethod || '—'}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-700/50">
                    <span className="text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                        {t('storefront.domains.ssl', 'SSL')}
                    </span>
                    <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">{formData.sslProvider || '—'}</p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-700/50">
                    <span className="text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                        {t('storefront.domains.redirects', 'Redirects')}
                    </span>
                    <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.redirectAfterCreation', 'Configure after creation')}
                    </p>
                </div>
                <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-700/50">
                    <span className="text-xs font-medium text-gray-500 uppercase dark:text-gray-400">
                        {t('storefront.domains.canonical', 'Canonical')}
                    </span>
                    <p className="mt-1 text-sm text-gray-900 dark:text-gray-100">{formData.canonicalPolicy || '—'}</p>
                </div>
            </div>
        </div>
    )
}

export function AddDomainWizardPage() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const [currentStep, setCurrentStep] = useState(1)
    const [formData, setFormData] = useState({
        domainType: 'alias',
        verificationMethod: 'txt',
        sslProvider: 'managed',
        redirectType: 'none',
        canonicalPolicy: 'canonical',
    })
    const [serverErrors, setServerErrors] = useState([])
    const [isCreating, setIsCreating] = useState(false)

    const { requestNavigation } = useAuthoringGuard({
        isDirty: Boolean(formData.hostname?.trim()),
        isBusy: isCreating,
    })

    const updateField = (key, value) => {
        setFormData((prev) => ({ ...prev, [key]: value }))
    }

    const handleNavigate = (path) => {
        requestNavigation(path)
    }

    const goNext = () => setCurrentStep((s) => Math.min(s + 1, STEPS.length))
    const goPrev = () => setCurrentStep((s) => Math.max(s - 1, 1))
    const goToStep = (step) => {
        if (step < currentStep) {
            setCurrentStep(step)
        }
    }

    const handleCreate = async () => {
        if (isCreating) return

        setServerErrors([])
        setIsCreating(true)

        // Map frontend form data to backend CreateDomainRequest
        // Frontend domainType: 'alias' | 'preview'. Primary is
        // granted by the server only after edge-proven SSL.
        // Backend DomainType: Primary(0) | Alias(1) | Redirect(2) | Preview(3)
        const domainTypeMap = {
            alias: 1,
            preview: 3,
        }

        const redirectTypeMap = {
            none: 0,
            '301': 1,
            '307': 2,
        }

        const canonicalPolicyMap = {
            enforce: 0, // UseAsPrimary (canonical to self)
            canonical: 1, // CanonicalizeToPrimary
        }

        const verificationMethodMap = {
            txt: 0,
            cname: 1,
            http: 2,
        }

        const domainTypeValue = domainTypeMap[formData.domainType]
        const request = {
            hostName: formData.hostname,
            domainType: domainTypeValue,
            redirectType: redirectTypeMap[formData.redirectType] || 0,
            canonicalPolicy: canonicalPolicyMap[formData.canonicalPolicy] ?? 1,
        }

        try {
            const response = await domainManagementService.createDomain(request)
            const verificationMethod = verificationMethodMap[formData.verificationMethod || 'txt'] ?? 0

            await domainManagementService.startDomainVerification(response.domainId, {
                verificationMethod,
                expectedVersion: response.version,
            })

            requestNavigation(`/storefront/domains/${response.domainId}/verification`)
        } catch (err) {
            const status = err?.response?.status
            const apiError = err?.response?.data

            if (status === 409) {
                setServerErrors([
                    t('storefront.domains.duplicateHostname',
                        'This hostname is already registered to your account or another tenant.'),
                ])
            } else if (status === 400) {
                const message = apiError?.message ||
                    t('storefront.domains.createValidationError',
                        'The domain could not be created. Please check your input and try again.')
                setServerErrors([message])
            } else if (status === 401 || status === 403) {
                setServerErrors([
                    t('storefront.domains.unauthorized',
                        'You are not authorized to create domains.'),
                ])
            } else {
                setServerErrors([
                    t('storefront.domains.createError',
                        'An unexpected error occurred while creating the domain.'),
                ])
            }
        } finally {
            setIsCreating(false)
        }
    }

    const renderStepContent = () => {
        switch (currentStep) {
            case 1: return <WizardStep1 formData={formData} updateField={updateField} />
            case 2: return <WizardStep2 formData={formData} updateField={updateField} />
            case 3: return <WizardStep3 formData={formData} updateField={updateField} />
            case 4: return <WizardStep4 formData={formData} updateField={updateField} />
            case 5: return <WizardStep5 />
            case 6: return <WizardStep6 formData={formData} updateField={updateField} />
            case 7: return <WizardStep7 formData={formData} />
            default: return null
        }
    }

    const currentStepData = STEPS[currentStep - 1]
    const isFirst = currentStep === 1
    const isLast = currentStep === STEPS.length
    const progressPercent = Math.round(((currentStep - 1) / (STEPS.length - 1)) * 100)

    return (
        <div className="space-y-4">
            <AuthoringButton
                action={{
                    label: t('common.back', 'Back'),
                    onClick: () => handleNavigate('/storefront/domains'),
                }}
                defaultLabel={t('common.back', 'Back')}
            />

            <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                <div className="mb-4 flex items-center justify-between">
                    <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.addDomain', 'Add Domain')}
                    </h1>
                    <span className="text-sm text-gray-500 dark:text-gray-400">
                        {currentStep} of {STEPS.length}
                    </span>
                </div>
                <p className="text-sm text-gray-500 dark:text-gray-400">
                    {t('storefront.domains.addDomainDesc', 'Add a new custom domain to your storefront.')}
                </p>
            </div>

            {/* Progress bar */}
            <div className="h-2 w-full rounded-full bg-gray-200 dark:bg-gray-700">
                <div
                    className="h-full rounded-full bg-indigo-600 transition-all"
                    style={{ width: `${progressPercent}%` }}
                />
            </div>

            {/* Step indicator */}
            <nav aria-label="Wizard progress" className="flex items-center justify-between rounded-xl border border-gray-200 bg-white p-4 dark:border-gray-700 dark:bg-gray-800">
                {STEPS.map((step) => {
                    const isActive = step.id === currentStep
                    const isCompleted = step.id < currentStep
                    return (
                        <button
                            key={step.id}
                            type="button"
                            onClick={() => goToStep(step.id)}
                            aria-current={isActive ? 'step' : undefined}
                            className={`flex flex-col items-center gap-1 rounded-lg px-2 py-2 text-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${
                                isActive
                                    ? 'text-indigo-600'
                                    : isCompleted
                                        ? 'text-emerald-600'
                                        : 'text-gray-500'
                            }`}
                        >
                            <span
                                className={`flex h-8 w-8 items-center justify-center rounded-full border-2 ${
                                    isActive
                                        ? 'border-indigo-600 bg-indigo-100 text-indigo-600'
                                        : isCompleted
                                            ? 'border-emerald-600 bg-emerald-100 text-emerald-600'
                                            : 'border-gray-300 text-gray-500 dark:border-gray-600'
                                }`}
                            >
                                {isCompleted ? <Check size={16} /> : <step.icon size={16} />}
                            </span>
                            {t(step.titleKey, step.fallbackTitle)}
                        </button>
                    )
                })}
            </nav>

            {/* Step content */}
            <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
                    {t(currentStepData.titleKey, currentStepData.fallbackTitle)}
                </h2>
                {renderStepContent()}
                {serverErrors.length > 0 ? (
                    <AuthoringValidationSummary errors={serverErrors} />
                ) : null}
            </div>

            {/* Navigation buttons */}
            <div className="flex justify-between rounded-xl border border-gray-200 bg-white p-4 dark:border-gray-700 dark:bg-gray-800">
                <AuthoringButton
                    action={{
                        label: t('common.previous', 'Previous'),
                        icon: ArrowLeft,
                        onClick: goPrev,
                        disabled: isFirst,
                    }}
                    defaultLabel={t('common.previous', 'Previous')}
                />
                {isLast ? (
                    <AuthoringButton
                        action={{
                            label: t('storefront.domains.createDomain', 'Create Domain'),
                            icon: ArrowRight,
                            onClick: handleCreate,
                            disabled: isCreating,
                        }}
                        defaultLabel={t('storefront.domains.createDomain', 'Create Domain')}
                        tone="publish"
                    />
                ) : (
                    <AuthoringButton
                        action={{
                            label: t('common.next', 'Next'),
                            icon: ArrowRight,
                            onClick: goNext,
                        }}
                        defaultLabel={t('common.next', 'Next')}
                        tone="primary"
                    />
                )}
            </div>
        </div>
    )
}

export default AddDomainWizardPage
