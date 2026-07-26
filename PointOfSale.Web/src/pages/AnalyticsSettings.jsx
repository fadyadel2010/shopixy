import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, ArrowRight, BarChart3, CheckCircle2, Lock, Send, ShoppingCart, X } from 'lucide-react'
import api from '../services/api'

import PixelOverrideStatsCard from '../modules/analytics/components/PixelOverrideStatsCard'

/**
 * Analytics Settings Page (Phase 6: External Analytics Integration)
 * 
 * ARCHITECTURE:
 * - POS owns all analytics events
 * - GA4 and Meta are DOWNSTREAM CONSUMERS only
 * - Server-side dispatch ONLY (no frontend scripts)
 * - Secrets encrypted at rest
 * - Providers can be enabled/disabled independently
 * 
 * ROUTE: /settings/analytics
 */
export default function AnalyticsSettings() {
    const { t } = useTranslation()
    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)
    const [testing, setTesting] = useState({ google: false, meta: false })
    const [testResults, setTestResults] = useState({ google: null, meta: null })
    const [settings, setSettings] = useState({
        google: {
            enabled: false,
            measurementId: '',
            apiSecret: '',
            debugMode: false,
            hasApiSecret: false
        },
        meta: {
            enabled: false,
            pixelId: '',
            accessToken: '',
            testEventCode: '',
            hasAccessToken: false
        }
    })
    const [error, setError] = useState(null)
    const [success, setSuccess] = useState(null)

    useEffect(() => {
        loadSettings()
    }, [])

    const loadSettings = async () => {
        try {
            setLoading(true)
            const response = await api.get('/admin/analytics/settings')
            const data = response.data

            setSettings({
                google: {
                    enabled: data.google?.enabled || false,
                    measurementId: data.google?.measurementId || '',
                    apiSecret: '', // Never returned from API
                    debugMode: data.google?.debugMode || false,
                    hasApiSecret: data.google?.hasApiSecret || false
                },
                meta: {
                    enabled: data.meta?.enabled || false,
                    pixelId: data.meta?.pixelId || '',
                    accessToken: '', // Never returned from API
                    testEventCode: data.meta?.testEventCode || '',
                    hasAccessToken: data.meta?.hasAccessToken || false
                }
            })
            setError(null)
        } catch (err) {
            console.error('Failed to load analytics settings:', err)
            setError(t('analyticsSettings.errors.loadFailed'))
        } finally {
            setLoading(false)
        }
    }

    const saveSettings = async () => {
        try {
            setSaving(true)
            setError(null)
            setSuccess(null)

            await api.put('/admin/analytics/settings', {
                google: {
                    enabled: settings.google.enabled,
                    measurementId: settings.google.measurementId,
                    apiSecret: settings.google.apiSecret || undefined, // Only send if changed
                    debugMode: settings.google.debugMode
                },
                meta: {
                    enabled: settings.meta.enabled,
                    pixelId: settings.meta.pixelId,
                    accessToken: settings.meta.accessToken || undefined, // Only send if changed
                    testEventCode: settings.meta.testEventCode
                }
            })

            // Reload to get updated state
            await loadSettings()
            setSuccess(t('analyticsSettings.settingsSaved'))
        } catch (err) {
            console.error('Failed to save analytics settings:', err)
            setError(t('analyticsSettings.errors.saveFailed'))
        } finally {
            setSaving(false)
        }
    }

    const toggleProvider = async (provider, enabled) => {
        try {
            setError(null)
            await api.put(`/admin/analytics/settings/${provider}/toggle`, { enabled })

            setSettings(prev => ({
                ...prev,
                [provider === 'GoogleAnalytics' ? 'google' : 'meta']: {
                    ...prev[provider === 'GoogleAnalytics' ? 'google' : 'meta'],
                    enabled
                }
            }))

            setSuccess(t(`analyticsSettings.${enabled ? 'providerEnabled' : 'providerDisabled'}`, {
                provider: provider === 'GoogleAnalytics'
                    ? t('analyticsSettings.providerNames.google')
                    : t('analyticsSettings.providerNames.meta')
            }))
        } catch (err) {
            console.error('Failed to toggle provider:', err)
            setError(t('analyticsSettings.errors.toggleFailed'))
        }
    }

    const testConnection = async (provider) => {
        const key = provider === 'GoogleAnalytics' ? 'google' : 'meta'
        setTesting(prev => ({ ...prev, [key]: true }))
        setTestResults(prev => ({ ...prev, [key]: null }))

        try {
            const response = await api.post(`/admin/analytics/settings/${provider}/test`)
            setTestResults(prev => ({
                ...prev,
                [key]: {
                    success: response.data.success,
                    message: response.data.message,
                    latencyMs: response.data.latencyMs
                }
            }))
        } catch (err) {
            setTestResults(prev => ({
                ...prev,
                [key]: {
                    success: false,
                    message: err.response?.data?.message || t('analyticsSettings.errors.testFailed')
                }
            }))
        } finally {
            setTesting(prev => ({ ...prev, [key]: false }))
        }
    }

    const updateGoogleSetting = (field, value) => {
        setSettings(prev => ({
            ...prev,
            google: { ...prev.google, [field]: value }
        }))
    }

    const updateMetaSetting = (field, value) => {
        setSettings(prev => ({
            ...prev,
            meta: { ...prev.meta, [field]: value }
        }))
    }

    if (loading) {
        return (
            <div className="analytics-settings-page loading">
                <div className="loading-spinner">
                    <div className="spinner"></div>
                    <p>{t('common.loading')}</p>
                </div>
            </div>
        )
    }

    return (
        <div className="analytics-settings-page">
            <header className="page-header">
                <div className="header-content">
                    <h1>{t('analyticsSettings.title')}</h1>
                    <p className="subtitle">{t('analyticsSettings.subtitle')}</p>
                </div>
                <button
                    className="btn btn-primary save-btn"
                    onClick={saveSettings}
                    disabled={saving}
                >
                    {saving ? t('common.saving') : t('common.save')}
                </button>
            </header>

            {/* Security Notice */}
            <div className="security-notice">
                <div className="notice-icon" aria-hidden="true">
                    <Lock size={24} />
                </div>
                <div className="notice-content">
                    <strong>{t('analyticsSettings.securityTitle')}</strong>
                    <p>{t('analyticsSettings.securityDescription')}</p>
                </div>
            </div>

            {/* Error/Success Messages */}
            {error && (
                <div className="alert alert-error">
                    <span className="alert-icon" aria-hidden="true">
                        <AlertTriangle size={18} />
                    </span>
                    {error}
                    <button className="alert-close" onClick={() => setError(null)} aria-label={t('common.close')}>
                        <X size={18} />
                    </button>
                </div>
            )}
            {success && (
                <div className="alert alert-success">
                    <span className="alert-icon" aria-hidden="true">
                        <CheckCircle2 size={18} />
                    </span>
                    {success}
                    <button className="alert-close" onClick={() => setSuccess(null)} aria-label={t('common.close')}>
                        <X size={18} />
                    </button>
                </div>
            )}

            {/* Phase M1: Pixel Transparency Card */}
            <div style={{ marginBottom: '2rem' }}>
                <PixelOverrideStatsCard />
            </div>

            <div className="settings-grid">
                {/* Google Analytics 4 Card */}
                <div className={`provider-card ${settings.google.enabled ? 'enabled' : ''}`}>
                    <div className="provider-header">
                        <div className="provider-logo ga4-logo">
                            <svg viewBox="0 0 24 24" fill="currentColor" width="32" height="32">
                                <path d="M22.844 11.35l-7.553-7.553a1.002 1.002 0 00-1.414 0l-1.415 1.414 1.789 1.79a1.19 1.19 0 011.508 1.514l1.725 1.725a1.192 1.192 0 11-.714.708l-1.609-1.609v4.319a1.192 1.192 0 11-.98-.016V9.285a1.19 1.19 0 01-.648-1.563L11.77 5.959l-5.464 5.464a1 1 0 000 1.414l7.553 7.553a1 1 0 001.414 0l7.571-7.571a1 1 0 000-1.414v-.055z" />
                            </svg>
                        </div>
                        <div className="provider-info">
                            <h2>{t('analyticsSettings.providerNames.google')}</h2>
                            <span className="provider-type">{t('analyticsSettings.measurementProtocol')}</span>
                        </div>
                        <label className="toggle-switch">
                            <input
                                type="checkbox"
                                checked={settings.google.enabled}
                                onChange={(e) => toggleProvider('GoogleAnalytics', e.target.checked)}
                            />
                            <span className="toggle-slider"></span>
                        </label>
                    </div>

                    <div className="provider-body">
                        <div className="form-group">
                            <label>{t('analyticsSettings.measurementId')}</label>
                            <input
                                type="text"
                                placeholder={t('analyticsSettings.placeholders.measurementId')}
                                value={settings.google.measurementId}
                                onChange={(e) => updateGoogleSetting('measurementId', e.target.value)}
                                className="form-input"
                            />
                            <span className="field-hint">{t('analyticsSettings.measurementIdHint')}</span>
                        </div>

                        <div className="form-group">
                            <label>
                                {t('analyticsSettings.apiSecret')}
                                {settings.google.hasApiSecret && (
                                    <span className="secret-badge">{t('analyticsSettings.configured')}</span>
                                )}
                            </label>
                            <input
                                type="password"
                                placeholder={settings.google.hasApiSecret ? t('analyticsSettings.placeholders.secretConfigured') : t('analyticsSettings.enterApiSecret')}
                                value={settings.google.apiSecret}
                                onChange={(e) => updateGoogleSetting('apiSecret', e.target.value)}
                                className="form-input"
                            />
                            <span className="field-hint">{t('analyticsSettings.apiSecretHint')}</span>
                        </div>

                        <div className="form-group checkbox-group">
                            <label className="checkbox-label">
                                <input
                                    type="checkbox"
                                    checked={settings.google.debugMode}
                                    onChange={(e) => updateGoogleSetting('debugMode', e.target.checked)}
                                />
                                <span>{t('analyticsSettings.debugMode')}</span>
                            </label>
                            <span className="field-hint">{t('analyticsSettings.debugModeHint')}</span>
                        </div>

                        <button
                            className="btn btn-secondary test-btn"
                            onClick={() => testConnection('GoogleAnalytics')}
                            disabled={testing.google || !settings.google.measurementId}
                        >
                            {testing.google ? (
                                <>
                                    <span className="spinner-small"></span>
                                    {t('analyticsSettings.testing')}
                                </>
                            ) : (
                                t('analyticsSettings.testConnection')
                            )}
                        </button>

                        {testResults.google && (
                            <div className={`test-result ${testResults.google.success ? 'success' : 'error'}`}>
                                <span className="result-icon" aria-hidden="true">
                                    {testResults.google.success ? <CheckCircle2 size={16} /> : <AlertTriangle size={16} />}
                                </span>
                                <span className="result-message">{testResults.google.message}</span>
                                {testResults.google.latencyMs && (
                                    <span className="result-latency">{testResults.google.latencyMs}ms</span>
                                )}
                            </div>
                        )}
                    </div>

                    <div className="provider-footer">
                        <div className="event-mapping">
                            <h4>{t('analyticsSettings.eventMapping')}</h4>
                            <div className="mapping-list">
                                <span className="mapping-item">{t('analyticsSettings.eventMappings.ga4.pageView')}</span>
                                <span className="mapping-item">{t('analyticsSettings.eventMappings.ga4.productView')}</span>
                                <span className="mapping-item">{t('analyticsSettings.eventMappings.ga4.addToCart')}</span>
                                <span className="mapping-item">{t('analyticsSettings.eventMappings.ga4.purchase')}</span>
                            </div>
                        </div>
                    </div>
                </div>

                {/* Meta Conversions API Card */}
                <div className={`provider-card ${settings.meta.enabled ? 'enabled' : ''}`}>
                    <div className="provider-header">
                        <div className="provider-logo meta-logo">
                            <svg viewBox="0 0 24 24" fill="currentColor" width="32" height="32">
                                <path d="M12 2.04c-5.5 0-10 4.49-10 10.02 0 5 3.66 9.15 8.44 9.9v-7H7.9v-2.9h2.54V9.85c0-2.52 1.49-3.92 3.77-3.92 1.09 0 2.24.2 2.24.2v2.47H15.2c-1.24 0-1.63.78-1.63 1.57v1.88h2.78l-.45 2.9h-2.33v7a10 10 0 008.44-9.9c0-5.53-4.5-10.02-10.01-10.02z" />
                            </svg>
                        </div>
                        <div className="provider-info">
                            <h2>{t('analyticsSettings.providerNames.meta')}</h2>
                            <span className="provider-type">{t('analyticsSettings.serverSide')}</span>
                        </div>
                        <label className="toggle-switch">
                            <input
                                type="checkbox"
                                checked={settings.meta.enabled}
                                onChange={(e) => toggleProvider('Meta', e.target.checked)}
                            />
                            <span className="toggle-slider"></span>
                        </label>
                    </div>

                    <div className="provider-body">
                        <div className="form-group">
                            <label>{t('analyticsSettings.pixelId')}</label>
                            <input
                                type="text"
                                placeholder={t('analyticsSettings.placeholders.pixelId')}
                                value={settings.meta.pixelId}
                                onChange={(e) => updateMetaSetting('pixelId', e.target.value)}
                                className="form-input"
                            />
                            <span className="field-hint">{t('analyticsSettings.pixelIdHint')}</span>
                        </div>

                        <div className="form-group">
                            <label>
                                {t('analyticsSettings.accessToken')}
                                {settings.meta.hasAccessToken && (
                                    <span className="secret-badge">{t('analyticsSettings.configured')}</span>
                                )}
                            </label>
                            <input
                                type="password"
                                placeholder={settings.meta.hasAccessToken ? t('analyticsSettings.placeholders.accessTokenConfigured') : t('analyticsSettings.enterAccessToken')}
                                value={settings.meta.accessToken}
                                onChange={(e) => updateMetaSetting('accessToken', e.target.value)}
                                className="form-input"
                            />
                            <span className="field-hint">{t('analyticsSettings.accessTokenHint')}</span>
                        </div>

                        <div className="form-group">
                            <label>{t('analyticsSettings.testEventCode')}</label>
                            <input
                                type="text"
                                placeholder={t('analyticsSettings.placeholders.testEventCode')}
                                value={settings.meta.testEventCode}
                                onChange={(e) => updateMetaSetting('testEventCode', e.target.value)}
                                className="form-input"
                            />
                            <span className="field-hint">{t('analyticsSettings.testEventCodeHint')}</span>
                        </div>

                        <button
                            className="btn btn-secondary test-btn"
                            onClick={() => testConnection('Meta')}
                            disabled={testing.meta || !settings.meta.pixelId}
                        >
                            {testing.meta ? (
                                <>
                                    <span className="spinner-small"></span>
                                    {t('analyticsSettings.testing')}
                                </>
                            ) : (
                                t('analyticsSettings.testConnection')
                            )}
                        </button>

                        {testResults.meta && (
                            <div className={`test-result ${testResults.meta.success ? 'success' : 'error'}`}>
                                <span className="result-icon" aria-hidden="true">
                                    {testResults.meta.success ? <CheckCircle2 size={16} /> : <AlertTriangle size={16} />}
                                </span>
                                <span className="result-message">{testResults.meta.message}</span>
                                {testResults.meta.latencyMs && (
                                    <span className="result-latency">{testResults.meta.latencyMs}ms</span>
                                )}
                            </div>
                        )}
                    </div>

                    <div className="provider-footer">
                        <div className="event-mapping">
                            <h4>{t('analyticsSettings.eventMapping')}</h4>
                            <div className="mapping-list">
                                <span className="mapping-item">{t('analyticsSettings.eventMappings.meta.pageView')}</span>
                                <span className="mapping-item">{t('analyticsSettings.eventMappings.meta.productView')}</span>
                                <span className="mapping-item">{t('analyticsSettings.eventMappings.meta.addToCart')}</span>
                                <span className="mapping-item">{t('analyticsSettings.eventMappings.meta.purchase')}</span>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            {/* Architecture Info */}
            <div className="architecture-info">
                <h3>{t('analyticsSettings.howItWorks')}</h3>
                <div className="architecture-flow">
                    <div className="flow-step">
                        <div className="step-icon" aria-hidden="true">
                            <ShoppingCart size={24} />
                        </div>
                        <div className="step-label">{t('analyticsSettings.architecture.storefront')}</div>
                    </div>
                    <div className="flow-arrow" aria-hidden="true">
                        <ArrowRight size={20} />
                    </div>
                    <div className="flow-step highlight">
                        <div className="step-icon" aria-hidden="true">
                            <BarChart3 size={24} />
                        </div>
                        <div className="step-label">{t('analyticsSettings.architecture.posAnalytics')}</div>
                    </div>
                    <div className="flow-arrow" aria-hidden="true">
                        <ArrowRight size={20} />
                    </div>
                    <div className="flow-step">
                        <div className="step-icon" aria-hidden="true">
                            <Send size={24} />
                        </div>
                        <div className="step-label">{t('analyticsSettings.architecture.dispatcher')}</div>
                    </div>
                    <div className="flow-arrow" aria-hidden="true">
                        <ArrowRight size={20} />
                    </div>
                    <div className="flow-step providers">
                        <div className="step-icon" aria-hidden="true">
                            <BarChart3 size={24} />
                        </div>
                        <div className="step-label">{t('analyticsSettings.architecture.providers')}</div>
                    </div>
                </div>
                <ul className="architecture-points">
                    <li>{t('analyticsSettings.point1')}</li>
                    <li>{t('analyticsSettings.point2')}</li>
                    <li>{t('analyticsSettings.point3')}</li>
                    <li>{t('analyticsSettings.point4')}</li>
                </ul>
            </div>

            <style>{`
                .analytics-settings-page {
                    padding: 2rem;
                    max-width: 1400px;
                    margin: 0 auto;
                }

                .analytics-settings-page.loading {
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    min-height: 60vh;
                }

                .loading-spinner {
                    text-align: center;
                }

                .spinner {
                    width: 48px;
                    height: 48px;
                    border: 4px solid #e2e8f0;
                    border-top-color: #6366f1;
                    border-radius: 50%;
                    animation: spin 1s linear infinite;
                    margin: 0 auto 1rem;
                }

                .spinner-small {
                    width: 16px;
                    height: 16px;
                    border: 2px solid #e2e8f0;
                    border-top-color: #6366f1;
                    border-radius: 50%;
                    animation: spin 1s linear infinite;
                    display: inline-block;
                    margin-right: 0.5rem;
                }

                @keyframes spin {
                    to { transform: rotate(360deg); }
                }

                .page-header {
                    display: flex;
                    justify-content: space-between;
                    align-items: flex-start;
                    margin-bottom: 2rem;
                }

                .header-content h1 {
                    font-size: 1.875rem;
                    font-weight: 700;
                    color: #1e293b;
                    margin: 0 0 0.5rem;
                }

                .subtitle {
                    color: #64748b;
                    margin: 0;
                }

                .save-btn {
                    padding: 0.75rem 1.5rem;
                    font-weight: 600;
                }

                .btn {
                    display: inline-flex;
                    align-items: center;
                    justify-content: center;
                    padding: 0.625rem 1.25rem;
                    border-radius: 0.5rem;
                    font-weight: 500;
                    font-size: 0.875rem;
                    cursor: pointer;
                    transition: all 0.2s;
                    border: none;
                }

                .btn-primary {
                    background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
                    color: white;
                }

                .btn-primary:hover:not(:disabled) {
                    transform: translateY(-1px);
                    box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
                }

                .btn-primary:disabled {
                    opacity: 0.6;
                    cursor: not-allowed;
                }

                .btn-secondary {
                    background: #f1f5f9;
                    color: #475569;
                    border: 1px solid #e2e8f0;
                }

                .btn-secondary:hover:not(:disabled) {
                    background: #e2e8f0;
                }

                .btn-secondary:disabled {
                    opacity: 0.6;
                    cursor: not-allowed;
                }

                .security-notice {
                    display: flex;
                    align-items: flex-start;
                    gap: 1rem;
                    padding: 1rem 1.5rem;
                    background: linear-gradient(135deg, #ecfdf5 0%, #d1fae5 100%);
                    border: 1px solid #a7f3d0;
                    border-radius: 0.75rem;
                    margin-bottom: 2rem;
                }

                .notice-icon {
                    font-size: 1.5rem;
                }

                .notice-content strong {
                    display: block;
                    color: #065f46;
                    margin-bottom: 0.25rem;
                }

                .notice-content p {
                    color: #047857;
                    margin: 0;
                    font-size: 0.875rem;
                }

                .alert {
                    display: flex;
                    align-items: center;
                    gap: 0.75rem;
                    padding: 1rem 1.25rem;
                    border-radius: 0.5rem;
                    margin-bottom: 1.5rem;
                }

                .alert-error {
                    background: #fef2f2;
                    border: 1px solid #fecaca;
                    color: #991b1b;
                }

                .alert-success {
                    background: #f0fdf4;
                    border: 1px solid #bbf7d0;
                    color: #166534;
                }

                .alert-close {
                    margin-left: auto;
                    background: none;
                    border: none;
                    font-size: 1.25rem;
                    cursor: pointer;
                    opacity: 0.6;
                }

                .alert-close:hover {
                    opacity: 1;
                }

                .settings-grid {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(400px, 1fr));
                    gap: 2rem;
                    margin-bottom: 3rem;
                }

                .provider-card {
                    background: white;
                    border-radius: 1rem;
                    border: 2px solid #e2e8f0;
                    overflow: hidden;
                    transition: all 0.3s ease;
                }

                .provider-card.enabled {
                    border-color: #6366f1;
                    box-shadow: 0 8px 25px rgba(99, 102, 241, 0.15);
                }

                .provider-header {
                    display: flex;
                    align-items: center;
                    gap: 1rem;
                    padding: 1.5rem;
                    background: linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%);
                    border-bottom: 1px solid #e2e8f0;
                }

                .provider-logo {
                    width: 56px;
                    height: 56px;
                    border-radius: 12px;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                }

                .ga4-logo {
                    background: linear-gradient(135deg, #f9ab00 0%, #e37400 100%);
                    color: white;
                }

                .meta-logo {
                    background: linear-gradient(135deg, #0866ff 0%, #0052cc 100%);
                    color: white;
                }

                .provider-info {
                    flex: 1;
                }

                .provider-info h2 {
                    font-size: 1.25rem;
                    font-weight: 700;
                    color: #1e293b;
                    margin: 0 0 0.25rem;
                }

                .provider-type {
                    font-size: 0.875rem;
                    color: #64748b;
                }

                .toggle-switch {
                    position: relative;
                    display: inline-block;
                    width: 52px;
                    height: 28px;
                }

                .toggle-switch input {
                    opacity: 0;
                    width: 0;
                    height: 0;
                }

                .toggle-slider {
                    position: absolute;
                    cursor: pointer;
                    top: 0;
                    left: 0;
                    right: 0;
                    bottom: 0;
                    background-color: #cbd5e1;
                    transition: 0.3s;
                    border-radius: 28px;
                }

                .toggle-slider:before {
                    position: absolute;
                    content: "";
                    height: 22px;
                    width: 22px;
                    left: 3px;
                    bottom: 3px;
                    background-color: white;
                    transition: 0.3s;
                    border-radius: 50%;
                    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
                }

                input:checked + .toggle-slider {
                    background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
                }

                input:checked + .toggle-slider:before {
                    transform: translateX(24px);
                }

                .provider-body {
                    padding: 1.5rem;
                }

                .form-group {
                    margin-bottom: 1.25rem;
                }

                .form-group label {
                    display: flex;
                    align-items: center;
                    gap: 0.5rem;
                    font-weight: 600;
                    color: #334155;
                    margin-bottom: 0.5rem;
                    font-size: 0.875rem;
                }

                .secret-badge {
                    font-size: 0.75rem;
                    padding: 0.125rem 0.5rem;
                    background: #dcfce7;
                    color: #166534;
                    border-radius: 9999px;
                    font-weight: 500;
                }

                .form-input {
                    width: 100%;
                    padding: 0.75rem 1rem;
                    border: 1px solid #e2e8f0;
                    border-radius: 0.5rem;
                    font-size: 0.875rem;
                    transition: all 0.2s;
                    background: #f8fafc;
                }

                .form-input:focus {
                    outline: none;
                    border-color: #6366f1;
                    box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
                    background: white;
                }

                .form-input::placeholder {
                    color: #94a3b8;
                }

                .field-hint {
                    display: block;
                    font-size: 0.75rem;
                    color: #94a3b8;
                    margin-top: 0.375rem;
                }

                .checkbox-group {
                    margin-top: 1rem;
                }

                .checkbox-label {
                    display: flex;
                    align-items: center;
                    gap: 0.5rem;
                    cursor: pointer;
                }

                .checkbox-label input {
                    width: 18px;
                    height: 18px;
                    accent-color: #6366f1;
                }

                .test-btn {
                    width: 100%;
                    margin-top: 0.5rem;
                }

                .test-result {
                    display: flex;
                    align-items: center;
                    gap: 0.5rem;
                    padding: 0.75rem 1rem;
                    border-radius: 0.5rem;
                    margin-top: 1rem;
                    font-size: 0.875rem;
                }

                .test-result.success {
                    background: #f0fdf4;
                    border: 1px solid #bbf7d0;
                    color: #166534;
                }

                .test-result.error {
                    background: #fef2f2;
                    border: 1px solid #fecaca;
                    color: #991b1b;
                }

                .result-latency {
                    margin-left: auto;
                    font-weight: 600;
                    opacity: 0.7;
                }

                .provider-footer {
                    padding: 1rem 1.5rem;
                    background: #f8fafc;
                    border-top: 1px solid #e2e8f0;
                }

                .event-mapping h4 {
                    font-size: 0.75rem;
                    text-transform: uppercase;
                    letter-spacing: 0.05em;
                    color: #64748b;
                    margin: 0 0 0.75rem;
                }

                .mapping-list {
                    display: flex;
                    flex-wrap: wrap;
                    gap: 0.5rem;
                }

                .mapping-item {
                    font-size: 0.75rem;
                    padding: 0.25rem 0.5rem;
                    background: white;
                    border: 1px solid #e2e8f0;
                    border-radius: 0.25rem;
                    color: #475569;
                    font-family: 'Monaco', 'Consolas', monospace;
                }

                .architecture-info {
                    background: linear-gradient(135deg, #1e1b4b 0%, #312e81 100%);
                    border-radius: 1rem;
                    padding: 2rem;
                    color: white;
                }

                .architecture-info h3 {
                    font-size: 1.25rem;
                    font-weight: 700;
                    margin: 0 0 1.5rem;
                    text-align: center;
                }

                .architecture-flow {
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    gap: 1rem;
                    margin-bottom: 2rem;
                    flex-wrap: wrap;
                }

                .flow-step {
                    text-align: center;
                    padding: 1rem 1.5rem;
                    background: rgba(255, 255, 255, 0.1);
                    border-radius: 0.75rem;
                    backdrop-filter: blur(10px);
                }

                .flow-step.highlight {
                    background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
                    box-shadow: 0 4px 20px rgba(99, 102, 241, 0.4);
                }

                .flow-step.providers {
                    background: linear-gradient(135deg, #0ea5e9 0%, #06b6d4 100%);
                }

                .step-icon {
                    font-size: 1.5rem;
                    margin-bottom: 0.5rem;
                }

                .step-label {
                    font-size: 0.875rem;
                    font-weight: 600;
                }

                .flow-arrow {
                    font-size: 1.25rem;
                    color: rgba(255, 255, 255, 0.5);
                }

                .architecture-points {
                    list-style: none;
                    padding: 0;
                    margin: 0;
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
                    gap: 0.75rem;
                }

                .architecture-points li {
                    display: flex;
                    align-items: flex-start;
                    gap: 0.75rem;
                    font-size: 0.875rem;
                    color: rgba(255, 255, 255, 0.9);
                }

                .architecture-points li::before {
                    content: "";
                    width: 0.5rem;
                    height: 0.5rem;
                    border-radius: 9999px;
                    background: #34d399;
                    margin-top: 0.35rem;
                    flex-shrink: 0;
                }

                @media (max-width: 768px) {
                    .analytics-settings-page {
                        padding: 1rem;
                    }

                    .page-header {
                        flex-direction: column;
                        gap: 1rem;
                    }

                    .save-btn {
                        width: 100%;
                    }

                    .settings-grid {
                        grid-template-columns: 1fr;
                    }

                    .architecture-flow {
                        flex-direction: column;
                    }

                    .flow-arrow {
                        transform: rotate(90deg);
                    }
                }

                /* RTL Support */
                [dir="rtl"] .page-header {
                    flex-direction: row-reverse;
                }

                [dir="rtl"] .provider-header {
                    flex-direction: row-reverse;
                }

                [dir="rtl"] .alert-close {
                    margin-left: 0;
                    margin-right: auto;
                }

                [dir="rtl"] .result-latency {
                    margin-left: 0;
                    margin-right: auto;
                }
            `}</style>
        </div>
    )
}

