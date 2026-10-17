import React, { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, RefreshCw, Pencil, Trash2, ShieldCheck, ShieldAlert, QrCode, Power } from 'lucide-react'
import { useToast } from '../../../components/Toast'
import { useWhatsAppApi } from '../hooks/useWhatsAppApi'
import { useWhatsAppAnalytics } from '../hooks/useWhatsAppAnalytics'
import AccountStatusBadge from '../components/AccountStatusBadge'
import DeliveryFunnel from '../components/DeliveryFunnel'
import TemplateEditor from '../components/TemplateEditor'
import MessageDetailsModal from '../components/MessageDetailsModal'
import { getWhatsAppLanguageKey, getWhatsAppMessageStatusKey, getWhatsAppTemplateTypeKey } from '../i18n'
import api from '../../../services/api'

const accountCountries = [
    { code: 'EG', dialCode: '+20', nameKey: 'settings.countries.egypt' },
    { code: 'SA', dialCode: '+966', nameKey: 'settings.countries.saudiArabia' },
    { code: 'AE', dialCode: '+971', nameKey: 'settings.countries.uae' },
    { code: 'KW', dialCode: '+965', nameKey: 'settings.countries.kuwait' },
    { code: 'QA', dialCode: '+974', nameKey: 'settings.countries.qatar' },
    { code: 'BH', dialCode: '+973', nameKey: 'settings.countries.bahrain' },
    { code: 'OM', dialCode: '+968', nameKey: 'settings.countries.oman' },
    { code: 'JO', dialCode: '+962', nameKey: 'settings.countries.jordan' },
    { code: 'LB', dialCode: '+961', nameKey: 'settings.countries.lebanon' },
    { code: 'US', dialCode: '+1', nameKey: 'settings.countries.unitedStates' },
    { code: 'GB', dialCode: '+44', nameKey: 'settings.countries.unitedKingdom' },
    { code: 'DE', dialCode: '+49', nameKey: 'settings.countries.germany' },
    { code: 'FR', dialCode: '+33', nameKey: 'settings.countries.france' },
]

export default function WhatsAppSettings() {
    const { t, i18n } = useTranslation()
    const { showSuccess, showError } = useToast()
    const [activeTab, setActiveTab] = useState('accounts')
    const [tabState, setTabState] = useState({})

    // API hooks
    const {
        getAccounts, toggleAccount, disconnectAccount, deleteAccount, getQrCode, createAccount, getAccountStatus,
        getTemplates, createTemplate, updateTemplate, deleteTemplate,
        getKillSwitchStatus, activateKillSwitch, deactivateKillSwitch,
        loading
    } = useWhatsAppApi()
    const { getAnalytics, getLogs } = useWhatsAppAnalytics()

    // === ACCOUNTS STATE ===
    const [accounts, setAccounts] = useState([])
    const [showQrModal, setShowQrModal] = useState(false)
    const [qrData, setQrData] = useState(null)
    const [selectedAccount, setSelectedAccount] = useState(null)
    const [showAddModal, setShowAddModal] = useState(false)
    const [newAccount, setNewAccount] = useState({ displayName: '', phoneNumber: '', countryCode: '', useForOrderConfirmation: true, useForOtpVerification: false })
    const [qrPolling, setQrPolling] = useState(false)
    const [qrSecondsRemaining, setQrSecondsRemaining] = useState(null)

    // === TEMPLATES STATE ===
    const [templates, setTemplates] = useState([])
    const [isEditorOpen, setIsEditorOpen] = useState(false)
    const [editingTemplate, setEditingTemplate] = useState(null)
    const [newTemplateType, setNewTemplateType] = useState('OrderConfirmation')
    const [templatesError, setTemplatesError] = useState(null)

    // === SAFETY STATE ===
    const [killSwitchStatus, setKillSwitchStatus] = useState({ isKilled: false, reason: null, activatedAt: null })
    const [killReason, setKillReason] = useState('')
    const [showKillConfirm, setShowKillConfirm] = useState(false)

    // === ANALYTICS STATE ===
    const [metrics, setMetrics] = useState(null)

    // === LOGS STATE ===
    const [logs, setLogs] = useState([])
    const [logFilters, setLogFilters] = useState({ status: '', type: '' })
    const [logPage, setLogPage] = useState(1)
    const [logPagination, setLogPagination] = useState({ totalPages: 1, totalCount: 0 })
    const [selectedMessage, setSelectedMessage] = useState(null)
    const accountsRequestIdRef = useRef(0)
    const templatesRequestIdRef = useRef(0)
    const killSwitchRequestIdRef = useRef(0)
    const analyticsRequestIdRef = useRef(0)
    const logsRequestIdRef = useRef(0)
    const qrCodeRequestIdRef = useRef(0)
    const qrPollingRequestIdRef = useRef(0)
    const orderTemplates = templates.filter(template => template.type?.toLowerCase() === 'orderconfirmation')
    const otpTemplates = templates.filter(template => template.type?.toLowerCase() === 'otp')

    const translateLanguage = (language) => t(getWhatsAppLanguageKey(language) || 'common.unknown')
    const translateMessageStatus = (status) => t(getWhatsAppMessageStatusKey(status) || 'common.unknown')
    const translateTemplateType = (type) => t(getWhatsAppTemplateTypeKey(type) || 'common.unknown')
    const formatDateTime = (value) => value ? new Date(value).toLocaleString(i18n.language) : t('common.none')
    const selectedAccountCountry = accountCountries.find(country => country.code === newAccount.countryCode)

    useEffect(() => {
        let active = true
        void api.get('/settings/default-country', {
            headers: { 'Cache-Control': 'no-store', Pragma: 'no-cache' },
        }).then(response => {
            const countryCode = response.data?.countryCode || ''
            if (active && countryCode) {
                setNewAccount(previous => previous.countryCode
                    ? previous
                    : { ...previous, countryCode })
            }
        }).catch(() => {
            // The modal still lets the administrator choose a country explicitly.
        })

        return () => {
            active = false
            accountsRequestIdRef.current += 1
            templatesRequestIdRef.current += 1
            killSwitchRequestIdRef.current += 1
            analyticsRequestIdRef.current += 1
            logsRequestIdRef.current += 1
            qrCodeRequestIdRef.current += 1
            qrPollingRequestIdRef.current += 1
        }
    }, [])

    // === DATA FETCHING ===
    const fetchAccounts = async () => {
        const requestId = ++accountsRequestIdRef.current
        setTabState(previous => ({ ...previous, accounts: 'loading' }))
        try {
            const data = await getAccounts()

            if (requestId !== accountsRequestIdRef.current) {
                return
            }

            setAccounts(data || [])
            setTabState(previous => ({ ...previous, accounts: 'ready' }))
        } catch (err) {
            if (requestId === accountsRequestIdRef.current) {
                setTabState(previous => ({ ...previous, accounts: 'error' }))
                showError(err.message)
            }
        }
    }

    const fetchTemplates = async () => {
        const requestId = ++templatesRequestIdRef.current
        setTabState(previous => ({ ...previous, templates: 'loading' }))
        setTemplatesError(null)
        try {
            const data = await getTemplates()

            if (requestId !== templatesRequestIdRef.current) {
                return
            }

            setTemplates(data || [])
            setTabState(previous => ({ ...previous, templates: 'ready' }))
        } catch (err) {
            if (requestId === templatesRequestIdRef.current) {
                setTabState(previous => ({ ...previous, templates: 'error' }))
                setTemplatesError(err.message)
                showError(err.message)
            }
        }
    }

    const fetchKillSwitchStatus = async () => {
        const requestId = ++killSwitchRequestIdRef.current
        setTabState(previous => ({ ...previous, safety: 'loading' }))
        try {
            const data = await getKillSwitchStatus()

            if (requestId !== killSwitchRequestIdRef.current) {
                return
            }

            setKillSwitchStatus(data)
            setTabState(previous => ({ ...previous, safety: 'ready' }))
        } catch (err) {
            if (requestId === killSwitchRequestIdRef.current) {
                setTabState(previous => ({ ...previous, safety: 'error' }))
                console.error('Failed to fetch kill switch status:', err)
            }
        }
    }

    const fetchAnalytics = async () => {
        const requestId = ++analyticsRequestIdRef.current
        setTabState(previous => ({ ...previous, analytics: 'loading' }))
        try {
            const data = await getAnalytics()

            if (requestId !== analyticsRequestIdRef.current) {
                return
            }

            setMetrics(data)
            setTabState(previous => ({ ...previous, analytics: 'ready' }))
        } catch (err) {
            if (requestId === analyticsRequestIdRef.current) {
                setTabState(previous => ({ ...previous, analytics: 'error' }))
                showError(err.message)
            }
        }
    }

    const fetchLogs = async (filters = logFilters, page = logPage) => {
        const requestId = ++logsRequestIdRef.current
        setTabState(previous => ({ ...previous, logs: 'loading' }))
        try {
            const data = await getLogs(filters, page)

            if (requestId !== logsRequestIdRef.current) {
                return
            }

            setLogs(data.items || [])
            setTabState(previous => ({ ...previous, logs: 'ready' }))
            setLogPagination({ totalPages: data.totalPages, totalCount: data.totalCount })
        } catch (err) {
            if (requestId === logsRequestIdRef.current) {
                setTabState(previous => ({ ...previous, logs: 'error' }))
                showError(err.message)
            }
        }
    }

    useEffect(() => {
        // Fetch data based on active tab
        if (activeTab === 'accounts') void fetchAccounts()
        if (activeTab === 'templates') void fetchTemplates()
        if (activeTab === 'safety') void fetchKillSwitchStatus()
        if (activeTab === 'analytics') void fetchAnalytics()
        if (activeTab === 'logs') void fetchLogs(logFilters, logPage)
    }, [activeTab])

    useEffect(() => {
        if (activeTab === 'logs') void fetchLogs(logFilters, logPage)
    }, [logPage, logFilters])

    // QR Polling - check connection status every 3 seconds while QR modal is open
    useEffect(() => {
        let pollInterval
        if (showQrModal && selectedAccount && qrPolling) {
            const accountId = selectedAccount.id
            const pollRequestId = qrPollingRequestIdRef.current
            pollInterval = setInterval(async () => {
                try {
                    const status = await getAccountStatus(accountId)
                    if (pollRequestId !== qrPollingRequestIdRef.current) {
                        return
                    }
                    if (status?.status?.toLowerCase() === 'connected') {
                        showSuccess(t('whatsapp.accounts.success.connected'))
                        setShowQrModal(false)
                        setQrPolling(false)
                        await fetchAccounts()
                    }
                } catch (err) {
                    if (pollRequestId === qrPollingRequestIdRef.current) {
                        setQrPolling(false)
                        showError(err.message)
                    }
                }
            }, 3000)
        }
        return () => {
            if (pollInterval) clearInterval(pollInterval)
        }
    }, [showQrModal, selectedAccount, qrPolling])

    useEffect(() => {
        if (!showQrModal || !qrData || qrSecondsRemaining <= 0) return

        const timer = setTimeout(() => {
            setQrSecondsRemaining(seconds => {
                if (seconds <= 1) {
                    setQrData(null)
                    setQrPolling(false)
                    return 0
                }
                return seconds - 1
            })
        }, 1000)

        return () => clearTimeout(timer)
    }, [showQrModal, qrData, qrSecondsRemaining])

    // === ACCOUNT HANDLERS ===
    const handleCreateAccount = async () => {
        try {
            await createAccount(newAccount)
            showSuccess(t('whatsapp.accounts.success.created'))
            setShowAddModal(false)
            setNewAccount(previous => ({ displayName: '', phoneNumber: '', countryCode: previous.countryCode, useForOrderConfirmation: true, useForOtpVerification: false }))
            await fetchAccounts()
        } catch (err) {
            showError(err.message)
        }
    }

    const handleToggleAccount = async (account) => {
        if (!confirm(t(account.enabled ? 'whatsapp.accounts.confirm.disable' : 'whatsapp.accounts.confirm.enable'))) return
        try {
            await toggleAccount(account.id, !account.enabled)
            showSuccess(t(account.enabled ? 'whatsapp.accounts.success.disabled' : 'whatsapp.accounts.success.enabled'))
            await fetchAccounts()
        } catch (err) {
            showError(err.message)
        }
    }

    const handleDisconnect = async (accountId) => {
        if (!confirm(t('whatsapp.accounts.confirm.disconnect'))) return
        try {
            await disconnectAccount(accountId)
            showSuccess(t('whatsapp.accounts.success.disconnected'))
            await fetchAccounts()
        } catch (err) {
            showError(err.message)
        }
    }

    const handleDeleteAccount = async (accountId) => {
        if (!confirm(t('whatsapp.accounts.confirm.delete'))) return
        try {
            await deleteAccount(accountId)
            showSuccess(t('whatsapp.accounts.success.deleted'))
            await fetchAccounts()
        } catch (err) {
            showError(err.message)
        }
    }

    const handleShowQr = async (account) => {
        const requestId = ++qrCodeRequestIdRef.current
        qrPollingRequestIdRef.current += 1
        setSelectedAccount(account)
        setQrData(null)
        setQrSecondsRemaining(null)
        setQrPolling(false)
        setShowQrModal(true)
        try {
            const data = await getQrCode(account.id)
            if (requestId !== qrCodeRequestIdRef.current) {
                return
            }
            if (data.success && data.qrImageBase64) {
                setQrData(data.qrImageBase64)
                setQrSecondsRemaining(data.expiresInSeconds || 60)
                qrPollingRequestIdRef.current += 1
                setQrPolling(true) // Start polling for connection status
            } else if (data.success && data.alreadyConnected) {
                showSuccess(t('whatsapp.accounts.success.alreadyConnected'))
                setShowQrModal(false)
                await fetchAccounts()
            } else {
                showError(data.error || t('whatsapp.accounts.qr.generateFailed'))
                setShowQrModal(false)
            }
        } catch (err) {
            if (requestId === qrCodeRequestIdRef.current) {
                showError(err.message)
                setShowQrModal(false)
            }
        }
    }

    const handleCloseQrModal = () => {
        qrCodeRequestIdRef.current += 1
        qrPollingRequestIdRef.current += 1
        setShowQrModal(false)
        setQrPolling(false)
        setQrData(null)
        setQrSecondsRemaining(null)
        void fetchAccounts() // Refresh accounts when closing modal
    }

    // === TEMPLATE HANDLERS ===
    const handleCreateTemplate = (type) => {
        setNewTemplateType(type)
        setEditingTemplate(null)
        setIsEditorOpen(true)
    }

    const handleEditTemplate = (template) => {
        setNewTemplateType(template.type)
        setEditingTemplate(template)
        setIsEditorOpen(true)
    }

    const handleDeleteTemplate = async (id) => {
        if (!confirm(t('whatsapp.templates.confirm.delete'))) return
        try {
            await deleteTemplate(id)
            showSuccess(t('whatsapp.templates.messages.deleted'))
            await fetchTemplates()
        } catch (err) {
            showError(err.message)
        }
    }

    const handleSaveTemplate = async (data) => {
        try {
            if (editingTemplate && !editingTemplate.isDefault) {
                await updateTemplate(editingTemplate.id, data)
                showSuccess(t('whatsapp.templates.messages.updated'))
            } else {
                await createTemplate(data)
                showSuccess(t('whatsapp.templates.messages.created'))
            }
            setIsEditorOpen(false)
            await fetchTemplates()
        } catch (err) {
            showError(err.message)
        }
    }

    // === SAFETY HANDLERS ===
    const handleActivateKillSwitch = async () => {
        if (!killReason.trim()) {
            showError(t('whatsapp.safety.reasonRequired'))
            return
        }
        try {
            await activateKillSwitch(killReason)
            showSuccess(t('whatsapp.safety.messages.activated'))
            setShowKillConfirm(false)
            setKillReason('')
            await fetchKillSwitchStatus()
        } catch (err) {
            showError(err.message)
        }
    }

    const handleDeactivateKillSwitch = async () => {
        if (!confirm(t('whatsapp.safety.confirm.deactivate'))) return
        try {
            await deactivateKillSwitch()
            showSuccess(t('whatsapp.safety.messages.deactivated'))
            await fetchKillSwitchStatus()
        } catch (err) {
            showError(err.message)
        }
    }

    const tabs = [
        { key: 'accounts', label: t('whatsapp.tabs.accounts') },
        { key: 'templates', label: t('whatsapp.tabs.templates') },
        { key: 'analytics', label: t('whatsapp.tabs.analytics') },
        { key: 'safety', label: t('whatsapp.tabs.safety') },
        { key: 'logs', label: t('whatsapp.tabs.logs') },
    ]

    return (
        <div className="space-y-6">
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('whatsapp.settings.title')}</h1>
                    <p className="text-sm text-gray-500 mt-1">{t('whatsapp.settings.subtitle')}</p>
                </div>
                <button
                    onClick={() => {
                        if (activeTab === 'accounts') fetchAccounts()
                        if (activeTab === 'templates') fetchTemplates()
                        if (activeTab === 'safety') fetchKillSwitchStatus()
                        if (activeTab === 'analytics') fetchAnalytics()
                        if (activeTab === 'logs') fetchLogs()
                    }}
                    title={t('common.refresh')}
                    aria-label={t('common.refresh')}
                    className="inline-flex items-center p-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                >
                    <RefreshCw className={`h-5 w-5 ${loading ? 'animate-spin' : ''}`} />
                </button>
            </div>

            <div className="bg-white rounded-lg shadow min-h-[600px] flex flex-col">
                {/* Tabs */}
                <div className="border-b border-gray-200">
                    <nav className="flex -mb-px overflow-x-auto">
                        {tabs.map(tab => (
                            <button
                                key={tab.key}
                                onClick={() => setActiveTab(tab.key)}
                                className={`px-6 py-4 text-sm font-medium border-b-2 transition-colors whitespace-nowrap ${activeTab === tab.key
                                    ? 'border-indigo-500 text-indigo-600'
                                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                    }`}
                            >
                                {tab.label}
                            </button>
                        ))}
                    </nav>
                </div>

                <div className="p-6 flex-1">
                    {tabState[activeTab] === 'error' && <div role="alert" className="rounded-md border border-red-200 bg-red-50 p-4 text-red-700">{t('common.failedToLoad')} {t('common.retry')}</div>}
                    {(!tabState[activeTab] || tabState[activeTab] === 'loading') && <p role="status">{t('common.loading')}</p>}
                    {/* === ACCOUNTS TAB === */}
                    {activeTab === 'accounts' && tabState.accounts === 'ready' && (
                        <div>
                            <div className="flex justify-between items-center mb-6">
                                <h2 className="text-lg font-medium text-gray-900">{t('whatsapp.accounts.connectedAccounts')}</h2>
                                {accounts.length === 0 && (
                                    <button
                                        onClick={() => setShowAddModal(true)}
                                        className="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-indigo-600 hover:bg-indigo-700"
                                    >
                                        <Plus className="-ml-1 mr-2 h-5 w-5" />
                                        {t('whatsapp.accounts.actions.addAccount')}
                                    </button>
                                )}
                                {accounts.length > 0 && (
                                    <span className="text-sm text-gray-500 italic">{t('whatsapp.accounts.oneAccountLimit')}</span>
                                )}
                            </div>

                            <div className="grid gap-6 sm:grid-cols-1 lg:grid-cols-2">
                                {accounts.length > 0 ? accounts.map(account => (
                                    <div key={account.id} className="bg-white border border-gray-200 rounded-lg shadow-sm p-5">
                                        {(() => {
                                            const isConnected = ['Connected', 'CONNECTED'].includes(account.status)
                                            return (
                                                <>
                                                    <div className="flex justify-between items-start mb-4">
                                                        <div>
                                                            <h3 className="text-lg font-medium text-gray-900">{account.displayName}</h3>
                                                            <p className="text-sm text-gray-500">{account.phoneNumber}</p>
                                                        </div>
                                                        <AccountStatusBadge status={account.status} />
                                                    </div>
                                                    <div className="flex gap-2 mb-4 flex-wrap">
                                                        <span className={`px-2.5 py-0.5 rounded-md text-xs font-medium ${account.useForOrderConfirmation ? 'bg-blue-100 text-blue-800' : 'bg-gray-100 text-gray-600'}`}>{t('whatsapp.accounts.orderConfirmations')}</span>
                                                        <span className={`px-2.5 py-0.5 rounded-md text-xs font-medium ${account.useForOtpVerification ? 'bg-purple-100 text-purple-800' : 'bg-gray-100 text-gray-600'}`}>{t('whatsapp.accounts.otpVerification')}</span>
                                                    </div>
                                                    {account.statusReason && <p className="mb-4 text-xs text-amber-700">{account.statusReason}</p>}
                                                    <div className="flex gap-2 flex-wrap">
                                                        <button
                                                            onClick={() => handleShowQr(account)}
                                                            disabled={loading || isConnected}
                                                            className="inline-flex items-center px-3 py-1.5 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
                                                        >
                                                            <QrCode className="mr-1.5 h-4 w-4" /> {t('whatsapp.accounts.scanQr')}
                                                        </button>
                                                        <button
                                                            onClick={() => handleToggleAccount(account)}
                                                            disabled={loading || (!account.enabled && !isConnected)}
                                                            className={`inline-flex items-center px-3 py-1.5 border border-transparent text-xs font-medium rounded text-white ${account.enabled ? 'bg-red-600 hover:bg-red-700' : 'bg-green-600 hover:bg-green-700'} disabled:opacity-50`}
                                                        >
                                                            <Power className="mr-1.5 h-4 w-4" /> {account.enabled ? t('common.disable') : t('common.enable')}
                                                        </button>
                                                        <button
                                                            onClick={() => handleDisconnect(account.id)}
                                                            disabled={loading}
                                                            className="inline-flex items-center px-3 py-1.5 border border-amber-300 text-xs font-medium rounded text-amber-700 bg-white hover:bg-amber-50 disabled:opacity-50"
                                                        >
                                                            {t('whatsapp.accounts.actions.disconnect')}
                                                        </button>
                                                        <button
                                                            onClick={() => handleDeleteAccount(account.id)}
                                                            disabled={loading}
                                                            className="inline-flex items-center px-3 py-1.5 border border-red-300 text-xs font-medium rounded text-red-700 bg-white hover:bg-red-50 disabled:opacity-50"
                                                        >
                                                            <Trash2 className="mr-1.5 h-4 w-4" /> {t('common.delete')}
                                                        </button>
                                                    </div>
                                                </>
                                            )
                                        })()}
                                    </div>
                                )) : (
                                    <div className="col-span-full text-center py-12 bg-gray-50 rounded-lg border border-dashed border-gray-300">
                                        <p className="text-gray-500">{t('whatsapp.accounts.emptyTitle')}</p>
                                        <p className="text-sm text-gray-400 mt-1">{t('whatsapp.accounts.emptyHint')}</p>
                                    </div>
                                )}
                            </div>

                            <div className="mt-6 bg-blue-50 border-l-4 border-blue-400 p-4">
                                <p className="text-sm text-blue-700">
                                    <strong>{t('whatsapp.accounts.noteTitle')}:</strong> {t('whatsapp.accounts.noteDescription')}
                                </p>
                            </div>
                        </div>
                    )}

                    {/* === TEMPLATES TAB === */}
                    {activeTab === 'templates' && tabState.templates === 'ready' && (
                        <div className="space-y-8">
                            {templatesError && (
                                <div role="alert" className="rounded-md border border-red-200 bg-red-50 p-4 text-sm text-red-700">
                                    <p>{templatesError}</p>
                                    <button onClick={fetchTemplates} className="mt-2 font-medium underline">{t('common.retry')}</button>
                                </div>
                            )}
                            {/* Order Confirmation Templates */}
                            <section>
                                <div className="flex justify-between items-center mb-4">
                                    <h2 className="text-lg font-medium text-gray-900">{t('whatsapp.types.orderConfirmation')}</h2>
                                    <button
                                        onClick={() => handleCreateTemplate('OrderConfirmation')}
                                        className="inline-flex items-center px-3 py-1.5 border border-transparent text-xs font-medium rounded text-white bg-indigo-600 hover:bg-indigo-700"
                                    >
                                        <Plus className="mr-1.5 h-4 w-4" /> {t('whatsapp.templates.actions.create')}
                                    </button>
                                </div>
                                {orderTemplates.length > 0 ? (
                                    <div className="bg-white border rounded-md divide-y">
                                        {orderTemplates.map(template => (
                                            <div key={template.id} className="px-4 py-3 flex justify-between items-center hover:bg-gray-50">
                                                <div>
                                                    <p className={`text-sm font-medium ${template.isActive ? 'text-indigo-600' : 'text-gray-500'}`}>
                                                        {translateLanguage(template.language)} {template.isDefault && <span className="text-xs text-emerald-600">({t('whatsapp.templates.systemDefault')})</span>} {!template.isActive && <span className="text-xs text-gray-400">({t('common.inactive')})</span>}
                                                    </p>
                                                    <p className="text-sm text-gray-500 line-clamp-1">{template.content}</p>
                                                </div>
                                                <div className="flex gap-2">
                                                    <button onClick={() => handleEditTemplate(template)} title={template.isDefault ? t('whatsapp.templates.actions.customize') : t('common.edit')} aria-label={template.isDefault ? t('whatsapp.templates.actions.customize') : t('common.edit')} className="p-1 text-gray-400 hover:text-gray-600"><Pencil className="h-4 w-4" /></button>
                                                    {!template.isDefault && <button onClick={() => handleDeleteTemplate(template.id)} title={t('common.delete')} aria-label={t('common.delete')} className="p-1 text-gray-400 hover:text-red-500"><Trash2 className="h-4 w-4" /></button>}
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                ) : (
                                    <p className="text-sm text-gray-500 italic">{t('whatsapp.templates.empty')}</p>
                                )}
                            </section>

                            {/* OTP Templates */}
                            <section>
                                <div className="flex justify-between items-center mb-4">
                                    <h2 className="text-lg font-medium text-gray-900">{t('whatsapp.types.otp')}</h2>
                                    <button
                                        onClick={() => handleCreateTemplate('Otp')}
                                        className="inline-flex items-center px-3 py-1.5 border border-transparent text-xs font-medium rounded text-white bg-indigo-600 hover:bg-indigo-700"
                                    >
                                        <Plus className="mr-1.5 h-4 w-4" /> {t('whatsapp.templates.actions.create')}
                                    </button>
                                </div>
                                {otpTemplates.length > 0 ? (
                                    <div className="bg-white border rounded-md divide-y">
                                        {otpTemplates.map(template => (
                                            <div key={template.id} className="px-4 py-3 flex justify-between items-center hover:bg-gray-50">
                                                <div>
                                                    <p className={`text-sm font-medium ${template.isActive ? 'text-indigo-600' : 'text-gray-500'}`}>
                                                        {translateLanguage(template.language)} {template.isDefault && <span className="text-xs text-emerald-600">({t('whatsapp.templates.systemDefault')})</span>} {!template.isActive && <span className="text-xs text-gray-400">({t('common.inactive')})</span>}
                                                    </p>
                                                    <p className="text-sm text-gray-500 line-clamp-1">{template.content}</p>
                                                </div>
                                                <div className="flex gap-2">
                                                    <button onClick={() => handleEditTemplate(template)} title={template.isDefault ? t('whatsapp.templates.actions.customize') : t('common.edit')} aria-label={template.isDefault ? t('whatsapp.templates.actions.customize') : t('common.edit')} className="p-1 text-gray-400 hover:text-gray-600"><Pencil className="h-4 w-4" /></button>
                                                    {!template.isDefault && <button onClick={() => handleDeleteTemplate(template.id)} title={t('common.delete')} aria-label={t('common.delete')} className="p-1 text-gray-400 hover:text-red-500"><Trash2 className="h-4 w-4" /></button>}
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                ) : (
                                    <p className="text-sm text-gray-500 italic">{t('whatsapp.templates.empty')}</p>
                                )}
                            </section>
                        </div>
                    )}

                    {/* === ANALYTICS TAB === */}
                    {activeTab === 'analytics' && tabState.analytics === 'ready' && (
                        <div>
                            {/* KPI Grid */}
                            <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4 mb-8">
                                {[
                                    { name: t('whatsapp.logs.statuses.sent'), value: metrics?.sentMessages ?? t('common.notAvailable'), color: 'text-gray-900' },
                                    { name: t('whatsapp.analytics.kpis.successRate'), value: `${metrics?.successRate || 0}%`, color: 'text-green-600' },
                                    { name: t('whatsapp.analytics.kpis.failed'), value: metrics?.failedMessages || 0, color: 'text-red-600' },
                                    { name: t('whatsapp.analytics.kpis.retries'), value: t('common.notAvailable'), color: 'text-yellow-600' },
                                ].map(card => (
                                    <div key={card.name} className="bg-white border rounded-lg px-4 py-5 sm:p-6">
                                        <dt className="text-sm font-medium text-gray-500 truncate">{card.name}</dt>
                                        <dd className={`mt-1 text-3xl font-semibold ${card.color}`}>{card.value}</dd>
                                    </div>
                                ))}
                            </div>

                            <div className="grid grid-cols-1 gap-8 lg:grid-cols-2">
                                {metrics?.funnel ? <DeliveryFunnel metrics={metrics.funnel} /> : <p>{t('common.notAvailable')}</p>}
                                <div className="bg-white border rounded-lg p-6">
                                    <h3 className="text-lg font-medium text-gray-900 mb-4">{t('whatsapp.analytics.operationalInsights')}</h3>
                                    <div className="space-y-4">
                                        <div className="bg-yellow-50 p-4 rounded-md">
                                            <h4 className="text-sm font-medium text-yellow-800">{t('whatsapp.analytics.circuitBreakerEvents')}</h4>
                                            <p className="mt-1 text-2xl font-semibold text-yellow-600">{t('common.notAvailable')}</p>
                                        </div>
                                        <div className="bg-red-50 p-4 rounded-md">
                                            <h4 className="text-sm font-medium text-red-800">{t('whatsapp.analytics.killSwitchActivations')}</h4>
                                            <p className="mt-1 text-2xl font-semibold text-red-600">{t('common.notAvailable')}</p>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    )}

                    {/* === SAFETY TAB === */}
                    {activeTab === 'safety' && tabState.safety === 'ready' && (
                        <div className="space-y-8">
                            {/* Status Banner */}
                            <div className={`rounded-md p-4 ${killSwitchStatus.isKilled ? 'bg-red-50 border-l-4 border-red-400' : 'bg-green-50 border-l-4 border-green-400'}`}>
                                <div className="flex">
                                    <div className="flex-shrink-0">
                                        {killSwitchStatus.isKilled ? (
                                            <ShieldAlert className="h-5 w-5 text-red-400" />
                                        ) : (
                                            <ShieldCheck className="h-5 w-5 text-green-400" />
                                        )}
                                    </div>
                                    <div className="ml-3">
                                        <h3 className={`text-sm font-medium ${killSwitchStatus.isKilled ? 'text-red-800' : 'text-green-800'}`}>
                                            {t('whatsapp.safety.killSwitch')}: {killSwitchStatus.isKilled ? t('whatsapp.safety.messagingHalted') : t('whatsapp.safety.state.deactivated')}
                                        </h3>
                                        {killSwitchStatus.isKilled && (
                                            <div className="mt-2 text-sm text-red-700">
                                                <p>{t('whatsapp.safety.reasonLabel')}: {killSwitchStatus.reason || t('common.none')}</p>
                                                <p>{t('whatsapp.safety.activatedAt')}: {formatDateTime(killSwitchStatus.activatedAt)}</p>
                                            </div>
                                        )}
                                    </div>
                                </div>
                            </div>

                            {/* Kill Switch Control */}
                            <div className="bg-white border rounded-lg p-6">
                                <h3 className="text-lg font-medium text-gray-900 mb-2">{t('whatsapp.safety.killSwitch')}</h3>
                                <p className="text-sm text-gray-500 mb-4">
                                    {t('whatsapp.safety.description')}
                                </p>
                                <div className="flex items-center gap-4">
                                    <label className="relative inline-flex items-center cursor-pointer">
                                        <input
                                            type="checkbox"
                                            className="sr-only peer"
                                            checked={killSwitchStatus.isKilled}
                                            onChange={() => {
                                                if (killSwitchStatus.isKilled) {
                                                    handleDeactivateKillSwitch()
                                                } else {
                                                    setShowKillConfirm(true)
                                                }
                                            }}
                                        />
                                        <div className={`w-11 h-6 rounded-full peer peer-focus:ring-4 transition-colors duration-200
                                            ${killSwitchStatus.isKilled ? 'bg-red-600 peer-focus:ring-red-300' : 'bg-gray-200 peer-focus:ring-gray-300'}
                                            after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all
                                            ${killSwitchStatus.isKilled ? 'after:translate-x-full after:border-white' : ''}`}
                                        ></div>
                                    </label>
                                    <span className="text-sm font-medium text-gray-900">
                                        {killSwitchStatus.isKilled ? t('whatsapp.safety.state.activated') : t('whatsapp.safety.state.deactivated')}
                                    </span>
                                </div>
                            </div>
                        </div>
                    )}

                    {/* === LOGS TAB === */}
                    {activeTab === 'logs' && tabState.logs === 'ready' && (
                        <div>
                            {/* Filters */}
                            <div className="bg-gray-50 rounded-lg p-4 mb-6 grid grid-cols-1 gap-4 sm:grid-cols-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700">{t('common.status')}</label>
                                    <select
                                        value={logFilters.status}
                                        onChange={(e) => { setLogFilters(prev => ({ ...prev, status: e.target.value })); setLogPage(1) }}
                                        className="mt-1 block w-full rounded-md border-gray-300 py-2 pl-3 pr-10 text-base focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                                    >
                                        <option value="">{t('whatsapp.logs.filters.allStatuses')}</option>
                                        <option value="Sent">{t('whatsapp.logs.statuses.sent')}</option>
                                        <option value="Failed">{t('whatsapp.logs.statuses.failed')}</option>
                                        <option value="Delivered">{t('whatsapp.logs.statuses.delivered')}</option>
                                    </select>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700">{t('whatsapp.logs.columns.type')}</label>
                                    <select
                                        value={logFilters.type}
                                        onChange={(e) => { setLogFilters(prev => ({ ...prev, type: e.target.value })); setLogPage(1) }}
                                        className="mt-1 block w-full rounded-md border-gray-300 py-2 pl-3 pr-10 text-base focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                                    >
                                        <option value="">{t('whatsapp.logs.filters.allTypes')}</option>
                                        <option value="OrderConfirmation">{t('whatsapp.types.orderConfirmation')}</option>
                                        <option value="Otp">{t('whatsapp.types.otp')}</option>
                                    </select>
                                </div>
                            </div>

                            {/* Table */}
                            <div className="overflow-x-auto border rounded-lg">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('common.date')}</th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('whatsapp.logs.columns.recipient')}</th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('whatsapp.logs.columns.type')}</th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('common.status')}</th>
                                            <th className="px-6 py-3"></th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {logs.map(log => (
                                            <tr key={log.id}>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDateTime(log.createdAtUtc)}</td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{log.phoneNumber}</td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{translateTemplateType(log.type)}</td>
                                                <td className="px-6 py-4 whitespace-nowrap">
                                                    <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full 
                                                        ${log.status === 'Sent' ? 'bg-green-100 text-green-800' :
                                                            log.status === 'Failed' ? 'bg-red-100 text-red-800' : 'bg-gray-100 text-gray-800'}`}>
                                                        {translateMessageStatus(log.status)}
                                                    </span>
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-right text-sm">
                                                    <button onClick={() => setSelectedMessage(log)} className="text-indigo-600 hover:text-indigo-900">{t('common.view')}</button>
                                                </td>
                                            </tr>
                                        ))}
                                        {logs.length === 0 && (
                                            <tr><td colSpan="5" className="px-6 py-4 text-center text-sm text-gray-500">{t('whatsapp.logs.empty')}</td></tr>
                                        )}
                                    </tbody>
                                </table>
                            </div>

                            {/* Pagination */}
                            {logPagination.totalPages > 1 && (
                                <div className="mt-4 flex items-center justify-between">
                                    <p className="text-sm text-gray-700">{t('whatsapp.logs.pageOf', { page: logPage, totalPages: logPagination.totalPages })}</p>
                                    <div className="flex gap-2">
                                        <button
                                            onClick={() => setLogPage(p => Math.max(1, p - 1))}
                                            disabled={logPage === 1}
                                            className="px-3 py-1 border rounded text-sm disabled:opacity-50"
                                        >{t('common.previous')}</button>
                                        <button
                                            onClick={() => setLogPage(p => Math.min(logPagination.totalPages, p + 1))}
                                            disabled={logPage === logPagination.totalPages}
                                            className="px-3 py-1 border rounded text-sm disabled:opacity-50"
                                        >{t('common.next')}</button>
                                    </div>
                                </div>
                            )}
                        </div>
                    )}
                </div>
            </div>

            {/* === MODALS === */}

            {/* Add Account Modal */}
            {showAddModal && (
                <div className="fixed inset-0 z-50 overflow-y-auto">
                    <div className="flex items-center justify-center min-h-screen px-4">
                        <div className="fixed inset-0 bg-gray-500 bg-opacity-75" onClick={() => setShowAddModal(false)}></div>
                        <div className="relative bg-white rounded-lg shadow-xl max-w-md w-full p-6">
                            <h3 className="text-lg font-medium text-gray-900 mb-4">{t('whatsapp.accounts.actions.addAccount')}</h3>
                            <div className="space-y-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700">{t('whatsapp.accounts.fields.displayName')}</label>
                                    <input
                                        type="text"
                                        value={newAccount.displayName}
                                        onChange={e => setNewAccount(prev => ({ ...prev, displayName: e.target.value }))}
                                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                                        placeholder={t('whatsapp.accounts.placeholders.displayName')}
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700">{t('whatsapp.accounts.fields.country')}</label>
                                    <select
                                        value={newAccount.countryCode}
                                        onChange={e => setNewAccount(prev => ({ ...prev, countryCode: e.target.value }))}
                                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                                    >
                                        <option value="">{t('settings.selectCountry')}</option>
                                        {accountCountries.map(country => (
                                            <option key={country.code} value={country.code}>
                                                {country.code} - {t(country.nameKey)} ({country.dialCode})
                                            </option>
                                        ))}
                                    </select>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700">{t('whatsapp.accounts.fields.phoneNumber')}</label>
                                    <div className="mt-1 flex rounded-md shadow-sm" dir="ltr">
                                        <span className="inline-flex items-center rounded-l-md border border-r-0 border-gray-300 bg-gray-50 px-3 text-sm text-gray-600">
                                            {selectedAccountCountry?.dialCode || '+'}
                                        </span>
                                        <input
                                            type="tel"
                                            value={newAccount.phoneNumber}
                                            onChange={e => setNewAccount(prev => ({ ...prev, phoneNumber: e.target.value.replace(/[^0-9\s()-]/g, '') }))}
                                            className="block w-full min-w-0 flex-1 rounded-none rounded-r-md border-gray-300 focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                                            placeholder={t('whatsapp.accounts.placeholders.phoneNumber')}
                                        />
                                    </div>
                                    {selectedAccountCountry && (
                                        <p className="mt-1 text-xs text-gray-500">
                                            {t('whatsapp.accounts.phonePrefixHint', { prefix: selectedAccountCountry.dialCode })}
                                        </p>
                                    )}
                                </div>
                                <div className="flex gap-4 flex-wrap">
                                    <label className="flex items-center">
                                        <input
                                            type="checkbox"
                                            checked={newAccount.useForOrderConfirmation}
                                            onChange={e => setNewAccount(prev => ({ ...prev, useForOrderConfirmation: e.target.checked }))}
                                            className="h-4 w-4 text-indigo-600 border-gray-300 rounded"
                                        />
                                        <span className="ml-2 text-sm text-gray-700">{t('whatsapp.accounts.orderConfirmations')}</span>
                                    </label>
                                    <label className="flex items-center">
                                        <input
                                            type="checkbox"
                                            checked={newAccount.useForOtpVerification}
                                            onChange={e => setNewAccount(prev => ({ ...prev, useForOtpVerification: e.target.checked }))}
                                            className="h-4 w-4 text-indigo-600 border-gray-300 rounded"
                                        />
                                        <span className="ml-2 text-sm text-gray-700">{t('whatsapp.accounts.otpVerification')}</span>
                                    </label>
                                </div>
                            </div>
                            <div className="mt-6 flex justify-end gap-3">
                                <button onClick={() => setShowAddModal(false)} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">{t('common.cancel')}</button>
                                <button onClick={handleCreateAccount} disabled={!newAccount.displayName || !newAccount.phoneNumber || !newAccount.countryCode} className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50">{t('whatsapp.accounts.actions.createAccount')}</button>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* QR Code Modal */}
            {showQrModal && (
                <div className="fixed inset-0 z-50 overflow-y-auto">
                    <div className="flex items-center justify-center min-h-screen px-4">
                        <div className="fixed inset-0 bg-gray-500 bg-opacity-75" onClick={handleCloseQrModal}></div>
                        <div className="relative bg-white rounded-lg shadow-xl max-w-sm w-full p-6 text-center">
                            <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-green-100 mb-4">
                                <QrCode className="h-6 w-6 text-green-600" />
                            </div>
                            <h3 className="text-lg font-medium text-gray-900 mb-2">{t('whatsapp.accounts.qr.title')}</h3>
                            <p className="text-sm text-gray-500 mb-4">{t('whatsapp.accounts.qr.description')}</p>
                            {qrData ? (
                                <>
                                    <img src={qrData} alt={t('whatsapp.accounts.qr.alt')} className="mx-auto h-56 w-56 max-w-full border p-2" />
                                    <p className="mt-2 text-xs text-gray-500" dir="ltr">{t('whatsapp.accounts.qr.expiresIn', { count: qrSecondsRemaining })}</p>
                                    {qrPolling && (
                                        <p className="mt-3 text-sm text-green-600 animate-pulse">
                                            {t('whatsapp.accounts.qr.waitingForConnection')}
                                        </p>
                                    )}
                                </>
                            ) : (
                                <div className="mx-auto flex h-48 w-48 flex-col items-center justify-center gap-3 bg-gray-100 px-4">
                                    <span>{qrSecondsRemaining === 0 ? t('whatsapp.accounts.qr.expiredTitle') : t('whatsapp.accounts.qr.loading')}</span>
                                    {qrSecondsRemaining === 0 && selectedAccount && (
                                        <button onClick={() => handleShowQr(selectedAccount)} className="text-sm font-medium text-indigo-600 hover:text-indigo-800">{t('whatsapp.accounts.actions.refreshQr')}</button>
                                    )}
                                </div>
                            )}
                            <button onClick={handleCloseQrModal} className="mt-6 w-full px-4 py-2 bg-indigo-600 text-white rounded-md hover:bg-indigo-700">{t('whatsapp.accounts.actions.done')}</button>
                        </div>
                    </div>
                </div>
            )}

            {/* Kill Switch Confirmation Modal */}
            {showKillConfirm && (
                <div className="fixed inset-0 z-50 overflow-y-auto">
                    <div className="flex items-center justify-center min-h-screen px-4">
                        <div className="fixed inset-0 bg-gray-500 bg-opacity-75" onClick={() => setShowKillConfirm(false)}></div>
                        <div className="relative bg-white rounded-lg shadow-xl max-w-lg w-full p-6">
                            <div className="flex items-start">
                                <div className="flex-shrink-0 flex items-center justify-center h-10 w-10 rounded-full bg-red-100">
                                    <ShieldAlert className="h-6 w-6 text-red-600" />
                                </div>
                                <div className="ml-4">
                                    <h3 className="text-lg font-medium text-gray-900">{t('whatsapp.safety.confirm.activateTitle')}</h3>
                                    <p className="mt-2 text-sm text-gray-500">
                                        {t('whatsapp.safety.confirm.activateDescription')}
                                    </p>
                                    <div className="mt-4">
                                        <label className="block text-sm font-medium text-gray-700">{t('whatsapp.safety.reasonLabel')}</label>
                                        <input
                                            type="text"
                                            value={killReason}
                                            onChange={e => setKillReason(e.target.value)}
                                            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-red-500 focus:ring-red-500 sm:text-sm"
                                            placeholder={t('whatsapp.safety.reasonPlaceholder')}
                                        />
                                    </div>
                                </div>
                            </div>
                            <div className="mt-6 flex justify-end gap-3">
                                <button onClick={() => setShowKillConfirm(false)} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">{t('common.cancel')}</button>
                                <button onClick={handleActivateKillSwitch} className="px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700">{t('whatsapp.safety.actions.activate')}</button>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Template Editor Modal */}
            {isEditorOpen && (
                <div className="fixed inset-0 z-50 overflow-y-auto">
                    <div className="flex items-center justify-center min-h-screen px-4">
                        <div className="fixed inset-0 bg-gray-500 bg-opacity-75" onClick={() => setIsEditorOpen(false)}></div>
                        <div className="relative bg-white rounded-lg shadow-xl max-w-2xl w-full p-6">
                            <h3 className="text-lg font-medium text-gray-900 mb-4">
                                {editingTemplate
                                    ? t('whatsapp.templates.actions.editTemplate')
                                    : t('whatsapp.templates.actions.newTemplate', { type: translateTemplateType(newTemplateType) })}
                            </h3>
                            <TemplateEditor
                                template={editingTemplate}
                                type={newTemplateType}
                                onSave={handleSaveTemplate}
                                onCancel={() => setIsEditorOpen(false)}
                            />
                        </div>
                    </div>
                </div>
            )}

            {/* Message Details Modal */}
            {selectedMessage && (
                <MessageDetailsModal
                    message={selectedMessage}
                    onClose={() => setSelectedMessage(null)}
                />
            )}
        </div>
    )
}
