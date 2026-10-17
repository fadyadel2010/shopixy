import { useState, useEffect, useCallback, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { FaGoogle, FaFacebook, FaCheck, FaTimes, FaKey, FaEye, FaEyeSlash, FaSync, FaExclamationTriangle } from 'react-icons/fa'
import { useToast } from '../../../components/Toast'
import authProvidersApi from '../services/authProvidersApi'
import './AuthProvidersPage.css'

/**
 * Storefront Auth Providers Management Page
 * 
 * POS Admin screen for managing Storefront authentication providers.
 * Allows enabling/disabling Google and Facebook login for customers.
 * 
 * ARCHITECTURAL RULES:
 * - POS is the ONLY admin for provider settings
 * - Storefront CANNOT enable/disable providers
 * - Client secrets are NEVER shown in UI
 * 
 * Phase: P2 - Storefront Auth Provider Management
 */
export default function AuthProvidersPage() {
    const { t } = useTranslation()
    const { showSuccess, showError } = useToast()

    const [providers, setProviders] = useState([])
    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState({})
    const [editMode, setEditMode] = useState({})
    const [formData, setFormData] = useState({})
    const [showSecrets, setShowSecrets] = useState({})
    const [showDisableModal, setShowDisableModal] = useState(null)
    const [disableReason, setDisableReason] = useState('')
    const [showRotateModal, setShowRotateModal] = useState(null)
    const [newSecret, setNewSecret] = useState('')
    const [loadError, setLoadError] = useState(false)
    const generation = useRef(0)
    const readId = useRef(0)
    const mutation = useRef(false)
    const editing = useRef({})
    const editRevision = useRef({})
    const revision = provider => providers.find(p => p.provider === provider)?.revision ?? '0'

    const loadProviders = useCallback(async () => {
        const current = generation.current
        const id = ++readId.current
        try {
            setLoading(true)
            const data = await authProvidersApi.getAuthProviders()
            if (current !== generation.current || id !== readId.current) return false
            if (!Array.isArray(data?.providers) || data.providers.length !== 2 || !data.providers.every(p => ['Google', 'Facebook'].includes(p.provider))) throw new Error('Invalid provider response')
            setProviders(data.providers)
            setLoadError(false)

            // Initialize form data for each provider
            const initialFormData = {}
            for (const provider of data.providers || []) {
                initialFormData[provider.provider] = {
                    clientId: provider.clientId || '',
                    clientSecret: '',
                    additionalScopes: provider.additionalScopes || ''
                }
            }
            setFormData(previous => Object.fromEntries(Object.entries(initialFormData).map(([key, value]) => [key, editing.current[key] ? previous[key] : value])))
            return true
        } catch {
            if (current !== generation.current || id !== readId.current) return false
            setLoadError(true)
            return false
        } finally {
            if (current === generation.current && id === readId.current) setLoading(false)
        }
    }, [])

    useEffect(() => {
        loadProviders()
        return () => { generation.current += 1; readId.current += 1 }
    }, [loadProviders])

    const getProviderIcon = (provider) => {
        switch (provider.toLowerCase()) {
            case 'google':
                return <FaGoogle className="provider-icon google" />
            case 'facebook':
                return <FaFacebook className="provider-icon facebook" />
            default:
                return null
        }
    }

    const handleEditToggle = (provider) => {
        if (mutation.current) return
        editing.current[provider] = !editing.current[provider]
        if (editing.current[provider]) editRevision.current[provider] = revision(provider)
        setEditMode(prev => ({
            ...prev,
            [provider]: !prev[provider]
        }))
        // Reset form data when toggling edit mode
        const providerData = providers.find(p => p.provider === provider)
        if (providerData) {
            setFormData(prev => ({
                ...prev,
                [provider]: {
                    clientId: providerData.clientId || '',
                    clientSecret: '',
                    additionalScopes: providerData.additionalScopes || ''
                }
            }))
        }
    }

    const handleInputChange = (provider, field, value) => {
        setFormData(prev => ({
            ...prev,
            [provider]: {
                ...prev[provider],
                [field]: value
            }
        }))
    }

    const handleSaveSettings = async (providerName) => {
        if (mutation.current) return
        mutation.current = true
        const current = generation.current
        try {
            setSaving(prev => ({ ...prev, [providerName]: true }))

            const settings = {
                clientId: formData[providerName]?.clientId ?? '',
                additionalScopes: formData[providerName]?.additionalScopes ?? ''
            }

            // Only include secret if it was changed
            if (formData[providerName]?.clientSecret) {
                settings.clientSecret = formData[providerName].clientSecret
            }

            await authProvidersApi.updateAuthProvider(providerName, settings, editRevision.current[providerName])
            if (current !== generation.current) return
            editing.current[providerName] = false
            setEditMode(prev => ({ ...prev, [providerName]: false }))
            setFormData(prev => ({ ...prev, [providerName]: { ...prev[providerName], clientSecret: '' } }))
            if (await loadProviders()) showSuccess(t('phase7.configurationSaved'))
            else showError(t('phase7.savedReadbackFailed'))
        } catch (error) {
            if (current === generation.current) showError(error.response?.status === 409 ? t('phase7.conflict') : t('authProviders.errors.saveFailed'))
        } finally {
            mutation.current = false
            if (current === generation.current) setSaving(prev => ({ ...prev, [providerName]: false }))
        }
    }

    const handleEnableProvider = async (providerName) => {
        if (mutation.current) return
        mutation.current = true
        const current = generation.current
        try {
            setSaving(prev => ({ ...prev, [providerName]: true }))
            await authProvidersApi.enableAuthProvider(providerName, revision(providerName))
            if (current !== generation.current) return
            if (await loadProviders()) showSuccess(t('authProviders.messages.providerEnabled', { provider: providerName }))
            else showError(t('phase7.savedReadbackFailed'))
        } catch {
            if (current === generation.current) showError(t('authProviders.errors.enableFailed'))
        } finally {
            mutation.current = false
            if (current === generation.current) setSaving(prev => ({ ...prev, [providerName]: false }))
        }
    }

    const handleDisableClick = (providerName) => {
        setShowDisableModal(providerName)
        setDisableReason('')
    }

    const handleConfirmDisable = async () => {
        if (mutation.current) return
        mutation.current = true
        const current = generation.current
        const providerName = showDisableModal
        try {
            setSaving(prev => ({ ...prev, [providerName]: true }))
            await authProvidersApi.disableAuthProvider(providerName, disableReason || null, revision(providerName))
            if (current !== generation.current) return
            setShowDisableModal(null)
            if (await loadProviders()) showSuccess(t('authProviders.messages.providerDisabled', { provider: providerName }))
            else showError(t('phase7.savedReadbackFailed'))
        } catch {
            if (current === generation.current) showError(t('authProviders.errors.disableFailed'))
        } finally {
            mutation.current = false
            if (current === generation.current) setSaving(prev => ({ ...prev, [providerName]: false }))
        }
    }

    const handleRotateClick = (providerName) => {
        setShowRotateModal(providerName)
        setNewSecret('')
    }

    const handleConfirmRotate = async () => {
        if (mutation.current) return
        const providerName = showRotateModal
        if (!newSecret.trim()) {
            showError(t('authProviders.errors.secretRequired'))
            return
        }
        mutation.current = true
        const current = generation.current

        try {
            setSaving(prev => ({ ...prev, [providerName]: true }))
            await authProvidersApi.rotateSecret(providerName, newSecret, revision(providerName))
            if (current !== generation.current) return
            setShowRotateModal(null)
            setNewSecret('')
            if (await loadProviders()) showSuccess(t('phase7.secretReplaced'))
            else showError(t('phase7.savedReadbackFailed'))
        } catch {
            if (current === generation.current) showError(t('authProviders.errors.rotateFailed'))
        } finally {
            mutation.current = false
            if (current === generation.current) setSaving(prev => ({ ...prev, [providerName]: false }))
        }
    }

    const toggleSecretVisibility = (provider) => {
        setShowSecrets(prev => ({
            ...prev,
            [provider]: !prev[provider]
        }))
    }

    const formatDate = (dateString) => {
        if (!dateString) return '-'
        return new Date(dateString).toLocaleString()
    }

    if (loading) {
        return (
            <div className="auth-providers-page">
                <div className="page-header">
                    <h1>{t('authProviders.title')}</h1>
                </div>
                <div className="loading-container">
                    <div className="loading-spinner"></div>
                    <p>{t('common.loading')}</p>
                </div>
            </div>
        )
    }

    return (
        <div className="auth-providers-page">
            <div className="page-header">
                <h1>{t('authProviders.title')}</h1>
                <p className="page-description">{t('authProviders.description')}</p>
                <p>{t('phase7.authScope')}</p>
                <button type="button" disabled={mutation.current} onClick={loadProviders}>{t('common.refresh', 'Refresh')}</button>
            </div>
            {loadError && <div role="alert">{t('phase7.loadFailed')} <button onClick={loadProviders}>{t('common.retry', 'Retry')}</button></div>}

            <fieldset disabled={Object.values(saving).some(Boolean)} className="providers-grid" style={{ border: 0, padding: 0, margin: 0 }}>
                {providers.map(provider => (
                    <div key={provider.provider} className={`provider-card ${provider.isEnabled ? 'enabled' : 'disabled'}`}>
                        <div className="provider-header">
                            <div className="provider-info">
                                {getProviderIcon(provider.provider)}
                                <h2>{provider.provider}</h2>
                            </div>
                            <div className={`status-badge ${provider.isEnabled ? 'enabled' : 'disabled'}`}>
                                {provider.isEnabled ? (
                                    <>
                                        <FaCheck /> {t('authProviders.status.enabled')}
                                    </>
                                ) : (
                                    <>
                                        <FaTimes /> {t('authProviders.status.disabled')}
                                    </>
                                )}
                            </div>
                        </div>

                        <div className="provider-body">
                            {/* Client ID */}
                            <div className="field-group">
                                <label htmlFor={`client-${provider.provider}`}>{t('authProviders.fields.clientId')}</label>
                                {editMode[provider.provider] ? (
                                    <input
                                        id={`client-${provider.provider}`} maxLength={256}
                                        type="text"
                                        value={formData[provider.provider]?.clientId || ''}
                                        onChange={(e) => handleInputChange(provider.provider, 'clientId', e.target.value)}
                                        placeholder={t('authProviders.placeholders.clientId')}
                                    />
                                ) : (
                                    <div className="field-value">
                                        {provider.clientId ? (
                                            <span className="masked-value">{provider.clientId.slice(0, 20)}...</span>
                                        ) : (
                                            <span className="not-configured">{t('authProviders.notConfigured')}</span>
                                        )}
                                    </div>
                                )}
                            </div>

                            {/* Client Secret */}
                            <div className="field-group">
                                <label htmlFor={`secret-${provider.provider}`}>{t('authProviders.fields.clientSecret')}</label>
                                {editMode[provider.provider] ? (
                                    <div className="secret-input-wrapper">
                                        <input
                                            id={`secret-${provider.provider}`} maxLength={512} autoComplete="new-password"
                                            type={showSecrets[provider.provider] ? 'text' : 'password'}
                                            value={formData[provider.provider]?.clientSecret || ''}
                                            onChange={(e) => handleInputChange(provider.provider, 'clientSecret', e.target.value)}
                                            placeholder={t('authProviders.placeholders.clientSecret')}
                                        />
                                        <button
                                            type="button"
                                            aria-label={t('phase7.toggleSecret')}
                                            className="toggle-visibility-btn"
                                            onClick={() => toggleSecretVisibility(provider.provider)}
                                        >
                                            {showSecrets[provider.provider] ? <FaEyeSlash /> : <FaEye />}
                                        </button>
                                    </div>
                                ) : (
                                    <div className="field-value">
                                        {provider.hasClientSecret ? (
                                            <span className="secret-configured">
                                                <FaKey /> {t('authProviders.secretConfigured')}
                                            </span>
                                        ) : (
                                            <span className="not-configured">{t('authProviders.notConfigured')}</span>
                                        )}
                                    </div>
                                )}
                            </div>

                            {/* Additional Scopes */}
                            {editMode[provider.provider] && (
                                <div className="field-group">
                                    <label htmlFor={`scopes-${provider.provider}`}>{t('authProviders.fields.additionalScopes')}</label>
                                    <input
                                        id={`scopes-${provider.provider}`} maxLength={512}
                                        type="text"
                                        value={formData[provider.provider]?.additionalScopes || ''}
                                        onChange={(e) => handleInputChange(provider.provider, 'additionalScopes', e.target.value)}
                                        placeholder={t('authProviders.placeholders.additionalScopes')}
                                    />
                                    <span className="field-hint">{t('authProviders.hints.additionalScopes')}</span>
                                </div>
                            )}

                            {/* Last Updated */}
                            <div className="meta-info">
                                <span className="last-updated">
                                    {t('authProviders.lastUpdated')}: {formatDate(provider.updatedAtUtc)}
                                </span>
                            </div>
                        </div>

                        <div className="provider-actions">
                            {editMode[provider.provider] ? (
                                <>
                                    <button
                                        className="btn btn-primary"
                                        onClick={() => handleSaveSettings(provider.provider)}
                                        disabled={saving[provider.provider]}
                                    >
                                        {saving[provider.provider] ? t('common.saving') : t('common.save')}
                                    </button>
                                    <button
                                        className="btn btn-secondary"
                                        onClick={() => handleEditToggle(provider.provider)}
                                        disabled={saving[provider.provider]}
                                    >
                                        {t('common.cancel')}
                                    </button>
                                </>
                            ) : (
                                <>
                                    <button
                                        className="btn btn-secondary"
                                        onClick={() => handleEditToggle(provider.provider)}
                                    >
                                        {t('common.edit')}
                                    </button>

                                    {provider.hasClientSecret && (
                                        <button
                                            className="btn btn-warning"
                                            onClick={() => handleRotateClick(provider.provider)}
                                            title={t('authProviders.actions.rotateSecret')}
                                        >
                                            <FaSync /> {t('authProviders.actions.rotateSecret')}
                                        </button>
                                    )}

                                    {provider.isEnabled ? (
                                        <button
                                            className="btn btn-danger"
                                            onClick={() => handleDisableClick(provider.provider)}
                                            disabled={saving[provider.provider]}
                                        >
                                            {t('authProviders.actions.disable')}
                                        </button>
                                    ) : (
                                        <button
                                            className="btn btn-success"
                                            onClick={() => handleEnableProvider(provider.provider)}
                                            disabled={saving[provider.provider] || !provider.hasClientSecret || !provider.clientId}
                                            title={!provider.hasClientSecret || !provider.clientId ? t('authProviders.hints.configureFirst') : ''}
                                        >
                                            {t('authProviders.actions.enable')}
                                        </button>
                                    )}
                                </>
                            )}
                        </div>
                    </div>
                ))}
            </fieldset>

            {/* Setup Guide Section */}
            <div className="setup-guide-section">
                <details className="setup-guide">
                    <summary className="setup-guide-header">
                        <span className="setup-guide-title">
                            {t('authProviders.setupGuide.title')}
                        </span>
                    </summary>
                    <div className="setup-guide-content">
                        {/* Google Setup Guide */}
                        <div className="provider-guide">
                            <h4>
                                <FaGoogle className="guide-icon google" />
                                {t('authProviders.setupGuide.google.title')}
                            </h4>
                            <ol className="setup-steps">
                                {(t('authProviders.setupGuide.google.steps', { returnObjects: true }) || []).map((step, index) => (
                                    <li key={index}>{step}</li>
                                ))}
                            </ol>
                            <p className="setup-note">
                                💡 {t('authProviders.setupGuide.google.note')}
                            </p>
                        </div>

                        {/* Facebook Setup Guide */}
                        <div className="provider-guide">
                            <h4>
                                <FaFacebook className="guide-icon facebook" />
                                {t('authProviders.setupGuide.facebook.title')}
                            </h4>
                            <ol className="setup-steps">
                                {(t('authProviders.setupGuide.facebook.steps', { returnObjects: true }) || []).map((step, index) => (
                                    <li key={index}>{step}</li>
                                ))}
                            </ol>
                            <p className="setup-note">
                                💡 {t('authProviders.setupGuide.facebook.note')}
                            </p>
                        </div>
                    </div>
                </details>
            </div>

            {/* Disable Confirmation Modal */}
            {showDisableModal && (
                <div className="modal-overlay" onClick={() => setShowDisableModal(null)}>
                    <fieldset disabled={Object.values(saving).some(Boolean)} role="dialog" aria-modal="true" aria-label={t('authProviders.modals.disableTitle', { provider: showDisableModal })} className="modal-content warning-modal" onClick={e => e.stopPropagation()}>
                        <div className="modal-header">
                            <FaExclamationTriangle className="warning-icon" />
                            <h3>{t('authProviders.modals.disableTitle', { provider: showDisableModal })}</h3>
                        </div>
                        <div className="modal-body">
                            <p className="warning-text">{t('authProviders.modals.disableWarning')}</p>
                            <p>{t('authProviders.modals.disableMessage')}</p>
                            <div className="field-group">
                                <label>{t('authProviders.modals.reasonLabel')}</label>
                                <input
                                    type="text"
                                    value={disableReason}
                                    onChange={(e) => setDisableReason(e.target.value)}
                                    placeholder={t('authProviders.modals.reasonPlaceholder')}
                                />
                            </div>
                        </div>
                        <div className="modal-actions">
                            <button
                                className="btn btn-secondary"
                                onClick={() => setShowDisableModal(null)}
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                className="btn btn-danger"
                                onClick={handleConfirmDisable}
                                disabled={saving[showDisableModal]}
                            >
                                {saving[showDisableModal] ? t('common.loading') : t('authProviders.actions.confirmDisable')}
                            </button>
                        </div>
                    </fieldset>
                </div>
            )}

            {/* Rotate Secret Modal */}
            {showRotateModal && (
                <div className="modal-overlay" onClick={() => setShowRotateModal(null)}>
                    <fieldset disabled={Object.values(saving).some(Boolean)} role="dialog" aria-modal="true" aria-label={t('authProviders.modals.rotateTitle', { provider: showRotateModal })} className="modal-content" onClick={e => e.stopPropagation()}>
                        <div className="modal-header">
                            <FaKey className="key-icon" />
                            <h3>{t('authProviders.modals.rotateTitle', { provider: showRotateModal })}</h3>
                        </div>
                        <div className="modal-body">
                            <p>{t('authProviders.modals.rotateMessage')}</p>
                            <div className="field-group">
                                <label htmlFor="rotate-secret">{t('authProviders.modals.newSecretLabel')}</label>
                                <div className="secret-input-wrapper">
                                    <input
                                        id="rotate-secret" maxLength={512} autoComplete="new-password"
                                        type={showSecrets['rotate'] ? 'text' : 'password'}
                                        value={newSecret}
                                        onChange={(e) => setNewSecret(e.target.value)}
                                        placeholder={t('authProviders.placeholders.newSecret')}
                                    />
                                    <button
                                        type="button"
                                        aria-label={t('phase7.toggleSecret')}
                                        className="toggle-visibility-btn"
                                        onClick={() => toggleSecretVisibility('rotate')}
                                    >
                                        {showSecrets['rotate'] ? <FaEyeSlash /> : <FaEye />}
                                    </button>
                                </div>
                            </div>
                        </div>
                        <div className="modal-actions">
                            <button
                                className="btn btn-secondary"
                                onClick={() => setShowRotateModal(null)}
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                className="btn btn-primary"
                                onClick={handleConfirmRotate}
                                disabled={saving[showRotateModal] || !newSecret.trim()}
                            >
                                {saving[showRotateModal] ? t('common.loading') : t('authProviders.actions.confirmRotate')}
                            </button>
                        </div>
                    </fieldset>
                </div>
            )}
        </div>
    )
}
