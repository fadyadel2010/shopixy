import { useState, useEffect, useCallback } from 'react'
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

    const loadProviders = useCallback(async () => {
        try {
            setLoading(true)
            const data = await authProvidersApi.getAuthProviders()
            setProviders(data.providers || [])

            // Initialize form data for each provider
            const initialFormData = {}
            for (const provider of data.providers || []) {
                initialFormData[provider.provider] = {
                    clientId: provider.clientId || '',
                    clientSecret: '',
                    additionalScopes: provider.additionalScopes || ''
                }
            }
            setFormData(initialFormData)
        } catch (error) {
            showError(t('authProviders.errors.loadFailed'))
            console.error('Failed to load auth providers:', error)
        } finally {
            setLoading(false)
        }
    }, [showError, t])

    useEffect(() => {
        loadProviders()
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
        try {
            setSaving(prev => ({ ...prev, [providerName]: true }))

            const settings = {
                clientId: formData[providerName]?.clientId || null,
                additionalScopes: formData[providerName]?.additionalScopes || null
            }

            // Only include secret if it was changed
            if (formData[providerName]?.clientSecret) {
                settings.clientSecret = formData[providerName].clientSecret
            }

            await authProvidersApi.updateAuthProvider(providerName, settings)
            showSuccess(t('authProviders.messages.settingsSaved'))
            setEditMode(prev => ({ ...prev, [providerName]: false }))
            await loadProviders()
        } catch (error) {
            showError(error.response?.data?.message || t('authProviders.errors.saveFailed'))
        } finally {
            setSaving(prev => ({ ...prev, [providerName]: false }))
        }
    }

    const handleEnableProvider = async (providerName) => {
        try {
            setSaving(prev => ({ ...prev, [providerName]: true }))
            await authProvidersApi.enableAuthProvider(providerName)
            showSuccess(t('authProviders.messages.providerEnabled', { provider: providerName }))
            await loadProviders()
        } catch (error) {
            showError(error.response?.data?.message || t('authProviders.errors.enableFailed'))
        } finally {
            setSaving(prev => ({ ...prev, [providerName]: false }))
        }
    }

    const handleDisableClick = (providerName) => {
        setShowDisableModal(providerName)
        setDisableReason('')
    }

    const handleConfirmDisable = async () => {
        const providerName = showDisableModal
        try {
            setSaving(prev => ({ ...prev, [providerName]: true }))
            await authProvidersApi.disableAuthProvider(providerName, disableReason || null)
            showSuccess(t('authProviders.messages.providerDisabled', { provider: providerName }))
            setShowDisableModal(null)
            await loadProviders()
        } catch (error) {
            showError(error.response?.data?.message || t('authProviders.errors.disableFailed'))
        } finally {
            setSaving(prev => ({ ...prev, [providerName]: false }))
        }
    }

    const handleRotateClick = (providerName) => {
        setShowRotateModal(providerName)
        setNewSecret('')
    }

    const handleConfirmRotate = async () => {
        const providerName = showRotateModal
        if (!newSecret.trim()) {
            showError(t('authProviders.errors.secretRequired'))
            return
        }

        try {
            setSaving(prev => ({ ...prev, [providerName]: true }))
            await authProvidersApi.rotateSecret(providerName, newSecret)
            showSuccess(t('authProviders.messages.secretRotated'))
            setShowRotateModal(null)
            setNewSecret('')
            await loadProviders()
        } catch (error) {
            showError(error.response?.data?.message || t('authProviders.errors.rotateFailed'))
        } finally {
            setSaving(prev => ({ ...prev, [providerName]: false }))
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
            </div>

            <div className="providers-grid">
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
                                <label>{t('authProviders.fields.clientId')}</label>
                                {editMode[provider.provider] ? (
                                    <input
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
                                <label>{t('authProviders.fields.clientSecret')}</label>
                                {editMode[provider.provider] ? (
                                    <div className="secret-input-wrapper">
                                        <input
                                            type={showSecrets[provider.provider] ? 'text' : 'password'}
                                            value={formData[provider.provider]?.clientSecret || ''}
                                            onChange={(e) => handleInputChange(provider.provider, 'clientSecret', e.target.value)}
                                            placeholder={t('authProviders.placeholders.clientSecret')}
                                        />
                                        <button
                                            type="button"
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
                                    <label>{t('authProviders.fields.additionalScopes')}</label>
                                    <input
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
            </div>

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
                    <div className="modal-content warning-modal" onClick={e => e.stopPropagation()}>
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
                    </div>
                </div>
            )}

            {/* Rotate Secret Modal */}
            {showRotateModal && (
                <div className="modal-overlay" onClick={() => setShowRotateModal(null)}>
                    <div className="modal-content" onClick={e => e.stopPropagation()}>
                        <div className="modal-header">
                            <FaKey className="key-icon" />
                            <h3>{t('authProviders.modals.rotateTitle', { provider: showRotateModal })}</h3>
                        </div>
                        <div className="modal-body">
                            <p>{t('authProviders.modals.rotateMessage')}</p>
                            <div className="field-group">
                                <label>{t('authProviders.modals.newSecretLabel')}</label>
                                <div className="secret-input-wrapper">
                                    <input
                                        type={showSecrets['rotate'] ? 'text' : 'password'}
                                        value={newSecret}
                                        onChange={(e) => setNewSecret(e.target.value)}
                                        placeholder={t('authProviders.placeholders.newSecret')}
                                    />
                                    <button
                                        type="button"
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
                    </div>
                </div>
            )}
        </div>
    )
}
