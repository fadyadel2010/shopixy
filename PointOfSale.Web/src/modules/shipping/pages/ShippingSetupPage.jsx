/**
 * Shipping Setup Page
 * Phase: Unified Shipping Configuration (Single Source of Truth)
 * 
 * Route: /admin/shipping/setup
 * 
 * THE SINGLE SOURCE OF TRUTH FOR ALL SHIPPING.
 * 
 * This page consolidates EVERYTHING shipping-related:
 * 
 * Tab 1: Configuration
 *   ✅ Carrier Integrations (connection status)
 *   ✅ Shipping Options (what customer sees at checkout)
 *   ✅ Flow Mapping Preview
 * 
 * Tab 2: Shipments
 *   ✅ All shipments list (operational view)
 *   ✅ Status tracking
 *   ✅ Actions (view details, etc.)
 * 
 * Tab 3: Health
 *   ✅ Webhook status
 *   ✅ Tracking sync status
 *   ✅ Carrier connectivity
 * 
 * Answer: "How does shipping actually work in this system?"
 * From ONE page.
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import {
    Settings, Truck, Package, Plus, RefreshCw, Activity,
    AlertCircle, CheckCircle, ArrowRight, ChevronDown, ChevronUp,
    Search, Filter, Eye
} from 'lucide-react'

import { useSettings } from '../../../contexts/SettingsContext'
import {
    useCarrierConfigurations,
    useShippingOptions,
    useShippingOptionActions
} from '../hooks/useShippingSetupApi'
import {
    useShipmentsList,
    useShippingHealth,
    getStatusConfig
} from '../hooks/useShipmentsApi'

import CarrierStatusCard from '../components/CarrierStatusCard'
import ShippingOptionRow from '../components/ShippingOptionRow'
import ShippingOptionFormModal from '../components/ShippingOptionFormModal'
import CarrierConfigurationModal from '../components/CarrierConfigurationModal'
import { CarrierBadge } from '../index'

export default function ShippingSetupPage() {
    const { t, i18n } = useTranslation()
    const navigate = useNavigate()
    const { formatCurrency, settings } = useSettings()
    const isRTL = i18n.language === 'ar'

    // TABS STATE
    const [activeTab, setActiveTab] = useState('configuration')

    // Configuration Data hooks
    const {
        carriers,
        loading: carriersLoading,
        error: carriersError,
        refetch: refetchCarriers
    } = useCarrierConfigurations()

    const {
        options,
        loading: optionsLoading,
        error: optionsError,
        refetch: refetchOptions
    } = useShippingOptions()

    const {
        createOption,
        updateOption,
        deleteOption,
        loading: actionLoading
    } = useShippingOptionActions()

    // Shipments Data
    const [shipmentFilters, setShipmentFilters] = useState({})
    const {
        shipments,
        pagination,
        loading: shipmentsLoading,
        error: shipmentsError,
        goToPage,
        refetch: refetchShipments
    } = useShipmentsList(shipmentFilters)

    // Health Data
    const {
        health,
        loading: healthLoading,
        error: healthError,
        refetch: refetchHealth
    } = useShippingHealth()

    // UI State for Configuration tab
    const [expandedSections, setExpandedSections] = useState({
        carriers: true,
        options: true,
        flow: false
    })
    const [showModal, setShowModal] = useState(false)
    const [editingOption, setEditingOption] = useState(null)
    const [successMessage, setSuccessMessage] = useState('')
    const [errorMessage, setErrorMessage] = useState('')

    // Carrier Configuration Modal State
    const [showCarrierConfigModal, setShowCarrierConfigModal] = useState(false)
    const [selectedCarrier, setSelectedCarrier] = useState(null)

    // Section toggle
    const toggleSection = (section) => {
        setExpandedSections(prev => ({ ...prev, [section]: !prev[section] }))
    }

    // Refresh all data based on active tab
    const handleRefresh = () => {
        if (activeTab === 'configuration') {
            refetchCarriers()
            refetchOptions()
        } else if (activeTab === 'shipments') {
            refetchShipments()
        } else if (activeTab === 'health') {
            refetchHealth()
        }
    }

    // Dismiss messages
    const dismissMessages = () => {
        setSuccessMessage('')
        setErrorMessage('')
    }

    // Modal handlers
    const openAddModal = () => {
        setEditingOption(null)
        setShowModal(true)
    }

    const openEditModal = (option) => {
        setEditingOption(option)
        setShowModal(true)
    }

    const closeModal = () => {
        setShowModal(false)
        setEditingOption(null)
    }

    // Save handler
    const handleSave = async (data) => {
        dismissMessages()

        let result
        if (editingOption?.id) {
            result = await updateOption(editingOption.id, data)
        } else {
            result = await createOption(data)
        }

        if (result.success) {
            setSuccessMessage(
                editingOption?.id
                    ? t('shippingSetup.optionUpdated', 'Shipping option updated successfully')
                    : t('shippingSetup.optionCreated', 'Shipping option created successfully')
            )
            closeModal()
            refetchOptions()
        } else {
            setErrorMessage(result.error)
        }
    }

    // Toggle option active status
    const handleToggleOption = async (option) => {
        const result = await updateOption(option.id, { isActive: !option.isActive })
        if (result.success) {
            refetchOptions()
        }
    }

    // Delete handler
    const handleDeleteOption = async (option) => {
        if (!window.confirm(t('common.deleteConfirm', { item: option.name }))) return

        const result = await deleteOption(option.id)
        if (result.success) {
            setSuccessMessage(t('common.deletedSuccessfully', 'Deleted successfully'))
            refetchOptions()
        } else {
            setErrorMessage(result.error)
        }
    }

    // Handle carrier configure - Open configuration modal
    const handleConfigureCarrier = (carrier) => {
        setSelectedCarrier(carrier)
        setShowCarrierConfigModal(true)
    }

    // Handle carrier config modal close
    const handleCarrierConfigClose = () => {
        setShowCarrierConfigModal(false)
        setSelectedCarrier(null)
    }

    // Handle carrier config saved - refetch carriers to update status
    const handleCarrierConfigSaved = () => {
        refetchCarriers()
    }

    // Calculate stats
    const connectedCarriers = carriers.filter(c => c.lastValidationSucceeded === true).length
    const totalCarriers = carriers.length
    const activeOptions = options.filter(o => o.isActive).length
    const totalOptions = options.length

    // Carrier info for health display
    const carrierInfo = {
        bosta: { name: 'Bosta', color: 'emerald', icon: '🚛' },
        aramex: { name: 'Aramex', color: 'red', icon: '📦' },
        mylerz: { name: 'Mylerz', color: 'blue', icon: '🚚' },
        dhl: { name: 'DHL Express', color: 'yellow', icon: '✈️' }
    }

    // Format date helper
    const formatDate = (dateStr) => {
        if (!dateStr) return 'Never'
        return new Date(dateStr).toLocaleDateString(i18n.language, {
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        })
    }

    // Is loading?
    const isLoading = carriersLoading && optionsLoading && shipmentsLoading && healthLoading

    // TABS CONFIG
    const tabs = [
        { key: 'configuration', label: t('shippingSetup.tabConfiguration', 'Configuration'), icon: Settings },
        { key: 'shipments', label: t('shippingSetup.tabShipments', 'Shipments'), icon: Package, count: pagination.totalCount },
        { key: 'health', label: t('shippingSetup.tabHealth', 'Health'), icon: Activity }
    ]

    return (
        <div className="max-w-6xl mx-auto pb-12">
            {/* Header */}
            <div className="flex items-center justify-between mb-6">
                <div className="flex items-center gap-4">
                    <div className="w-14 h-14 bg-gradient-to-br from-indigo-500 to-purple-600 rounded-2xl flex items-center justify-center shadow-lg">
                        <Truck className="text-white" size={28} />
                    </div>
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('shippingSetup.pageTitle', 'Shipping')}
                        </h1>
                        <p className="text-gray-500">
                            {t('shippingSetup.pageSubtitle', 'Configure, manage, and monitor all shipping operations')}
                        </p>
                    </div>
                </div>

                <button
                    onClick={handleRefresh}
                    disabled={isLoading}
                    className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-xl hover:bg-gray-50 disabled:opacity-50 transition-colors"
                >
                    <RefreshCw size={16} className={isLoading ? 'animate-spin' : ''} />
                    {t('common.refresh', 'Refresh')}
                </button>
            </div>

            {/* Messages */}
            {successMessage && (
                <div className="mb-6 p-4 bg-green-50 border border-green-200 rounded-xl flex items-center gap-3">
                    <CheckCircle className="text-green-500" size={20} />
                    <span className="text-green-700">{successMessage}</span>
                    <button onClick={dismissMessages} className="ml-auto text-green-500 hover:text-green-700">×</button>
                </div>
            )}
            {errorMessage && (
                <div className="mb-6 p-4 bg-red-50 border border-red-200 rounded-xl flex items-center gap-3">
                    <AlertCircle className="text-red-500" size={20} />
                    <span className="text-red-700">{errorMessage}</span>
                    <button onClick={dismissMessages} className="ml-auto text-red-500 hover:text-red-700">×</button>
                </div>
            )}

            {/* TABS */}
            <div className="flex border-b border-gray-200 mb-6">
                {tabs.map(tab => {
                    const Icon = tab.icon
                    return (
                        <button
                            key={tab.key}
                            onClick={() => setActiveTab(tab.key)}
                            className={`flex items-center gap-2 px-6 py-3 text-sm font-medium border-b-2 transition-colors ${activeTab === tab.key
                                ? 'border-indigo-600 text-indigo-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                }`}
                        >
                            <Icon size={18} />
                            {tab.label}
                            {tab.count !== undefined && tab.count > 0 && (
                                <span className={`px-2 py-0.5 text-xs rounded-full ${activeTab === tab.key
                                    ? 'bg-indigo-100 text-indigo-700'
                                    : 'bg-gray-100 text-gray-600'
                                    }`}>
                                    {tab.count}
                                </span>
                            )}
                        </button>
                    )
                })}
            </div>

            {/* TAB CONTENT */}
            {activeTab === 'configuration' && (
                <ConfigurationTab
                    carriers={carriers}
                    carriersLoading={carriersLoading}
                    carriersError={carriersError}
                    options={options}
                    optionsLoading={optionsLoading}
                    optionsError={optionsError}
                    expandedSections={expandedSections}
                    toggleSection={toggleSection}
                    handleConfigureCarrier={handleConfigureCarrier}
                    refetchCarriers={refetchCarriers}
                    openAddModal={openAddModal}
                    openEditModal={openEditModal}
                    handleDeleteOption={handleDeleteOption}
                    handleToggleOption={handleToggleOption}
                    formatCurrency={formatCurrency}
                    connectedCarriers={connectedCarriers}
                    totalCarriers={totalCarriers}
                    activeOptions={activeOptions}
                    totalOptions={totalOptions}
                    t={t}
                />
            )}

            {activeTab === 'shipments' && (
                <ShipmentsTab
                    shipments={shipments}
                    pagination={pagination}
                    loading={shipmentsLoading}
                    error={shipmentsError}
                    goToPage={goToPage}
                    navigate={navigate}
                    formatDate={formatDate}
                    formatCurrency={formatCurrency}
                    t={t}
                />
            )}

            {activeTab === 'health' && (
                <HealthTab
                    health={health}
                    loading={healthLoading}
                    error={healthError}
                    refetch={refetchHealth}
                    carrierInfo={carrierInfo}
                    formatDate={formatDate}
                    t={t}
                />
            )}

            {/* Shipping Option Modal */}
            <ShippingOptionFormModal
                isOpen={showModal}
                onClose={closeModal}
                onSave={handleSave}
                option={editingOption}
                carriers={carriers}
                loading={actionLoading}
                currencySymbol={settings?.currencySymbol || '$'}
            />

            {/* Carrier Configuration Modal */}
            <CarrierConfigurationModal
                carrierCode={selectedCarrier?.carrierCode}
                carrierName={selectedCarrier?.displayName}
                open={showCarrierConfigModal}
                onClose={handleCarrierConfigClose}
                onSaved={handleCarrierConfigSaved}
            />
        </div>
    )
}


// ═══════════════════════════════════════════════════════════════
// CONFIGURATION TAB
// ═══════════════════════════════════════════════════════════════
function ConfigurationTab({
    carriers, carriersLoading, carriersError,
    options, optionsLoading, optionsError,
    expandedSections, toggleSection,
    handleConfigureCarrier, refetchCarriers,
    openAddModal, openEditModal, handleDeleteOption, handleToggleOption,
    formatCurrency, connectedCarriers, totalCarriers, activeOptions, totalOptions,
    t
}) {
    return (
        <>
            {/* Quick Stats */}
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
                <div className="bg-white border border-gray-200 rounded-xl p-4">
                    <div className="text-2xl font-bold text-indigo-600">{connectedCarriers}/{totalCarriers}</div>
                    <div className="text-sm text-gray-500">{t('shippingSetup.carriersConnected', 'Carriers Connected')}</div>
                </div>
                <div className="bg-white border border-gray-200 rounded-xl p-4">
                    <div className="text-2xl font-bold text-emerald-600">{activeOptions}</div>
                    <div className="text-sm text-gray-500">{t('shippingSetup.activeOptions', 'Active Options')}</div>
                </div>
                <div className="bg-white border border-gray-200 rounded-xl p-4">
                    <div className="text-2xl font-bold text-amber-600">{totalOptions - activeOptions}</div>
                    <div className="text-sm text-gray-500">{t('shippingSetup.inactiveOptions', 'Inactive Options')}</div>
                </div>
                <div className="bg-white border border-gray-200 rounded-xl p-4">
                    <div className="text-2xl font-bold text-purple-600">
                        {carriers.filter(c => c.supportsCod).length}
                    </div>
                    <div className="text-sm text-gray-500">{t('shippingSetup.codEnabled', 'COD Enabled')}</div>
                </div>
            </div>

            {/* Section 1: Carrier Integrations */}
            <div className="bg-white border border-gray-200 rounded-2xl mb-6 overflow-hidden">
                <button
                    onClick={() => toggleSection('carriers')}
                    className="w-full flex items-center justify-between p-5 hover:bg-gray-50 transition-colors"
                >
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 bg-blue-100 rounded-xl flex items-center justify-center">
                            <Truck className="text-blue-600" size={20} />
                        </div>
                        <div className="text-left">
                            <h2 className="font-semibold text-gray-900">
                                {t('shippingSetup.carrierIntegrations', 'Carrier Integrations')}
                            </h2>
                            <p className="text-sm text-gray-500">
                                {t('shippingSetup.carrierIntegrationsDesc', 'Connection status for all supported carriers')}
                            </p>
                        </div>
                    </div>
                    {expandedSections.carriers ? <ChevronUp size={20} /> : <ChevronDown size={20} />}
                </button>

                {expandedSections.carriers && (
                    <div className="p-5 pt-0 border-t border-gray-100">
                        {carriersError ? (
                            <div className="p-4 bg-red-50 border border-red-200 rounded-xl text-red-700">
                                {carriersError}
                            </div>
                        ) : carriers.length === 0 ? (
                            <div className="p-8 text-center text-gray-500">
                                <Truck className="mx-auto mb-3 text-gray-300" size={40} />
                                <p>{t('shippingSetup.noCarriers', 'No carriers available')}</p>
                            </div>
                        ) : (
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                {carriers.map(carrier => (
                                    <CarrierStatusCard
                                        key={carrier.carrierCode}
                                        carrier={carrier}
                                        onConfigure={handleConfigureCarrier}
                                        onRefresh={refetchCarriers}
                                    />
                                ))}
                            </div>
                        )}
                    </div>
                )}
            </div>

            {/* Section 2: Shipping Options */}
            <div className="bg-white border border-gray-200 rounded-2xl mb-6 overflow-hidden">
                <div className="flex items-center justify-between p-5 border-b border-gray-100">
                    <button
                        onClick={() => toggleSection('options')}
                        className="flex items-center gap-3 flex-1"
                    >
                        <div className="w-10 h-10 bg-emerald-100 rounded-xl flex items-center justify-center">
                            <Package className="text-emerald-600" size={20} />
                        </div>
                        <div className="text-left">
                            <h2 className="font-semibold text-gray-900">
                                {t('shippingSetup.shippingOptions', 'Shipping Options')}
                            </h2>
                            <p className="text-sm text-gray-500">
                                {t('shippingSetup.shippingOptionsDesc', 'What customers see and select at checkout')}
                            </p>
                        </div>
                    </button>

                    <div className="flex items-center gap-2">
                        <button
                            onClick={openAddModal}
                            className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-xl hover:bg-indigo-700 transition-colors"
                        >
                            <Plus size={18} />
                            {t('shippingSetup.addOption', 'Add Option')}
                        </button>
                        <button onClick={() => toggleSection('options')} className="p-2">
                            {expandedSections.options ? <ChevronUp size={20} /> : <ChevronDown size={20} />}
                        </button>
                    </div>
                </div>

                {expandedSections.options && (
                    <div className="p-5">
                        {optionsError ? (
                            <div className="p-4 bg-red-50 border border-red-200 rounded-xl text-red-700">
                                {optionsError}
                            </div>
                        ) : options.length === 0 ? (
                            <div className="p-8 text-center">
                                <Package className="mx-auto mb-3 text-gray-300" size={40} />
                                <p className="text-gray-500 mb-4">{t('shippingSetup.noOptions', 'No shipping options configured')}</p>
                                <button
                                    onClick={openAddModal}
                                    className="inline-flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-xl hover:bg-indigo-700 transition-colors"
                                >
                                    <Plus size={18} />
                                    {t('shippingSetup.createFirstOption', 'Create First Option')}
                                </button>
                            </div>
                        ) : (
                            <div className="space-y-3">
                                {options.map(option => (
                                    <ShippingOptionRow
                                        key={option.id}
                                        option={option}
                                        carriers={carriers}
                                        onEdit={openEditModal}
                                        onDelete={handleDeleteOption}
                                        onToggle={handleToggleOption}
                                        formatCurrency={formatCurrency}
                                    />
                                ))}
                            </div>
                        )}
                    </div>
                )}
            </div>

            {/* Section 3: How It Works */}
            <div className="bg-gradient-to-br from-indigo-50 to-purple-50 border border-indigo-200 rounded-2xl overflow-hidden">
                <button
                    onClick={() => toggleSection('flow')}
                    className="w-full flex items-center justify-between p-5 hover:bg-white/30 transition-colors"
                >
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 bg-indigo-500 rounded-xl flex items-center justify-center">
                            <ArrowRight className="text-white" size={20} />
                        </div>
                        <div className="text-left">
                            <h2 className="font-semibold text-gray-900">
                                {t('shippingSetup.howItWorks', 'How Shipping Works')}
                            </h2>
                            <p className="text-sm text-gray-500">
                                {t('shippingSetup.howItWorksDesc', 'The complete checkout → shipment flow')}
                            </p>
                        </div>
                    </div>
                    {expandedSections.flow ? <ChevronUp size={20} /> : <ChevronDown size={20} />}
                </button>

                {expandedSections.flow && (
                    <div className="p-5 pt-0 border-t border-indigo-200/50">
                        <div className="flex items-center justify-between gap-3 py-6">
                            <div className="flex-1 text-center p-4 bg-white rounded-xl border border-indigo-200 shadow-sm">
                                <div className="text-3xl mb-2">🛒</div>
                                <div className="font-semibold text-gray-900">{t('shippingSetup.step1', 'Checkout')}</div>
                                <div className="text-sm text-gray-500">{t('shippingSetup.step1Desc', 'Customer selects shipping')}</div>
                            </div>
                            <ArrowRight className="text-indigo-300 flex-shrink-0" size={32} />
                            <div className="flex-1 text-center p-4 bg-white rounded-xl border border-amber-200 shadow-sm">
                                <div className="text-3xl mb-2">⚙️</div>
                                <div className="font-semibold text-gray-900">{t('shippingSetup.step2', 'Order Complete')}</div>
                                <div className="text-sm text-gray-500">{t('shippingSetup.step2Desc', 'Maps to carrier')}</div>
                            </div>
                            <ArrowRight className="text-indigo-300 flex-shrink-0" size={32} />
                            <div className="flex-1 text-center p-4 bg-white rounded-xl border border-emerald-200 shadow-sm">
                                <div className="text-3xl mb-2">📦</div>
                                <div className="font-semibold text-gray-900">{t('shippingSetup.step3', 'Shipment Created')}</div>
                                <div className="text-sm text-gray-500">{t('shippingSetup.step3Desc', 'Instant')}</div>
                            </div>
                        </div>
                        <div className="grid grid-cols-3 gap-4 mt-4">
                            <div className="flex items-start gap-3 p-3 bg-white/50 rounded-lg">
                                <CheckCircle className="text-emerald-500 flex-shrink-0" size={20} />
                                <div className="text-sm text-gray-700">{t('shippingSetup.point1', 'Options drive checkout')}</div>
                            </div>
                            <div className="flex items-start gap-3 p-3 bg-white/50 rounded-lg">
                                <CheckCircle className="text-emerald-500 flex-shrink-0" size={20} />
                                <div className="text-sm text-gray-700">{t('shippingSetup.point2', 'Carrier mapping fulfills')}</div>
                            </div>
                            <div className="flex items-start gap-3 p-3 bg-white/50 rounded-lg">
                                <CheckCircle className="text-emerald-500 flex-shrink-0" size={20} />
                                <div className="text-sm text-gray-700">{t('shippingSetup.point3', 'Auto-created shipments')}</div>
                            </div>
                        </div>
                    </div>
                )}
            </div>
        </>
    )
}


// ═══════════════════════════════════════════════════════════════
// SHIPMENTS TAB
// ═══════════════════════════════════════════════════════════════
function ShipmentsTab({ shipments, pagination, loading, error, goToPage, navigate, formatDate, formatCurrency, t }) {
    if (loading && shipments.length === 0) {
        return (
            <div className="flex items-center justify-center h-64">
                <div className="animate-spin h-8 w-8 border-4 border-indigo-600 border-t-transparent rounded-full" />
            </div>
        )
    }

    if (error) {
        return (
            <div className="p-6 bg-red-50 border border-red-200 rounded-xl text-red-700">
                {error}
            </div>
        )
    }

    if (shipments.length === 0) {
        return (
            <div className="bg-white border border-gray-200 rounded-2xl p-12 text-center">
                <Package className="mx-auto mb-4 text-gray-300" size={48} />
                <h3 className="text-lg font-semibold text-gray-900 mb-2">
                    {t('shippingSetup.noShipments', 'No Shipments Yet')}
                </h3>
                <p className="text-gray-500">
                    {t('shippingSetup.noShipmentsDesc', 'Shipments will appear here when orders are completed')}
                </p>
            </div>
        )
    }

    return (
        <div className="bg-white border border-gray-200 rounded-2xl overflow-hidden">
            {/* Shipments Table */}
            <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-200">
                    <thead className="bg-gray-50">
                        <tr>
                            <th className="px-6 py-3 text-left text-xs font-semibold text-gray-500 uppercase">
                                {t('shippingSetup.trackingNumber', 'Tracking #')}
                            </th>
                            <th className="px-6 py-3 text-left text-xs font-semibold text-gray-500 uppercase">
                                {t('shippingSetup.carrier', 'Carrier')}
                            </th>
                            <th className="px-6 py-3 text-left text-xs font-semibold text-gray-500 uppercase">
                                {t('common.status', 'Status')}
                            </th>
                            <th className="px-6 py-3 text-left text-xs font-semibold text-gray-500 uppercase">
                                {t('common.date', 'Date')}
                            </th>
                            <th className="px-6 py-3 text-center text-xs font-semibold text-gray-500 uppercase">
                                {t('common.actions', 'Actions')}
                            </th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {shipments.map(shipment => {
                            const statusConfig = getStatusConfig(shipment.status)
                            return (
                                <tr key={shipment.id} className="hover:bg-gray-50">
                                    <td className="px-6 py-4">
                                        <div className="font-mono text-sm font-medium text-gray-900">
                                            {shipment.carrierTrackingNumber || shipment.id?.substring(0, 8)}
                                        </div>
                                        {shipment.orderId && (
                                            <div className="text-xs text-gray-500">
                                                Order: {shipment.orderId?.substring(0, 8)}
                                            </div>
                                        )}
                                    </td>
                                    <td className="px-6 py-4">
                                        <CarrierBadge carrierCode={shipment.carrierCode} />
                                    </td>
                                    <td className="px-6 py-4">
                                        <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-medium ${statusConfig.bgColor} ${statusConfig.textColor}`}>
                                            <span>{statusConfig.icon}</span>
                                            {t(`shipping.status.${shipment.status}`, shipment.status)}
                                        </span>
                                    </td>
                                    <td className="px-6 py-4 text-sm text-gray-500">
                                        {formatDate(shipment.createdAt)}
                                    </td>
                                    <td className="px-6 py-4 text-center">
                                        <button
                                            onClick={() => navigate(`/admin/shipping/shipments/${shipment.id}`)}
                                            className="p-2 text-gray-400 hover:text-indigo-600 hover:bg-indigo-50 rounded-lg transition-colors"
                                            title={t('common.view', 'View')}
                                        >
                                            <Eye size={18} />
                                        </button>
                                    </td>
                                </tr>
                            )
                        })}
                    </tbody>
                </table>
            </div>

            {/* Pagination */}
            {pagination.totalPages > 1 && (
                <div className="flex items-center justify-between px-6 py-4 border-t border-gray-100">
                    <div className="text-sm text-gray-500">
                        {t('shippingSetup.showingShipments', 'Showing {{from}} to {{to}} of {{total}}', {
                            from: (pagination.page - 1) * pagination.pageSize + 1,
                            to: Math.min(pagination.page * pagination.pageSize, pagination.totalCount),
                            total: pagination.totalCount
                        })}
                    </div>
                    <div className="flex gap-2">
                        <button
                            onClick={() => goToPage(pagination.page - 1)}
                            disabled={pagination.page === 1}
                            className="px-3 py-1 border border-gray-200 rounded-lg text-sm disabled:opacity-50 hover:bg-gray-50"
                        >
                            {t('common.previous', 'Previous')}
                        </button>
                        <button
                            onClick={() => goToPage(pagination.page + 1)}
                            disabled={pagination.page === pagination.totalPages}
                            className="px-3 py-1 border border-gray-200 rounded-lg text-sm disabled:opacity-50 hover:bg-gray-50"
                        >
                            {t('common.next', 'Next')}
                        </button>
                    </div>
                </div>
            )}
        </div>
    )
}


// ═══════════════════════════════════════════════════════════════
// HEALTH TAB
// ═══════════════════════════════════════════════════════════════
function HealthTab({ health, loading, error, refetch, carrierInfo, formatDate, t }) {
    if (loading && !health) {
        return (
            <div className="flex items-center justify-center h-64">
                <div className="animate-spin h-8 w-8 border-4 border-indigo-600 border-t-transparent rounded-full" />
            </div>
        )
    }

    if (error) {
        return (
            <div className="p-6 bg-red-50 border border-red-200 rounded-xl">
                <div className="flex items-center gap-3 text-red-700">
                    <AlertCircle size={24} />
                    <div>
                        <h3 className="font-semibold">Health Check Failed</h3>
                        <p className="text-sm">{error}</p>
                    </div>
                </div>
                <button
                    onClick={refetch}
                    className="mt-4 text-sm text-red-700 hover:underline"
                >
                    Try again
                </button>
            </div>
        )
    }

    if (!health) return null

    return (
        <div className="space-y-6">
            {/* Overall Status */}
            <div className="bg-white border border-gray-200 rounded-2xl p-6">
                <div className="flex items-center gap-4">
                    <div className={`w-16 h-16 rounded-2xl flex items-center justify-center ${health.status === 'healthy' ? 'bg-green-100' : 'bg-yellow-100'
                        }`}>
                        {health.status === 'healthy' ? (
                            <CheckCircle className="text-green-600" size={32} />
                        ) : (
                            <AlertCircle className="text-yellow-600" size={32} />
                        )}
                    </div>
                    <div>
                        <h2 className="text-xl font-bold text-gray-900">
                            {health.status === 'healthy'
                                ? t('shippingSetup.allSystemsOperational', 'All Systems Operational')
                                : t('shippingSetup.degradedPerformance', 'Degraded Performance')
                            }
                        </h2>
                        <p className="text-gray-500">
                            {t('shippingSetup.lastChecked', 'Last checked')}: {formatDate(health.timestamp)}
                        </p>
                    </div>
                </div>
            </div>

            {/* Supported Carriers */}
            <div className="bg-white border border-gray-200 rounded-2xl p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-4 flex items-center gap-2">
                    <Truck size={20} className="text-gray-400" />
                    {t('shippingSetup.supportedCarriers', 'Supported Carriers')}
                </h3>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    {(health.supportedCarriers || []).map(code => {
                        const info = carrierInfo[code] || { name: code, color: 'gray', icon: '📦' }
                        return (
                            <div
                                key={code}
                                className={`p-4 rounded-xl border-2 border-${info.color}-200 bg-${info.color}-50`}
                            >
                                <div className="text-2xl mb-2">{info.icon}</div>
                                <p className="font-semibold text-gray-900">{info.name}</p>
                                <p className="text-sm text-gray-500 font-mono">{code}</p>
                            </div>
                        )
                    })}
                </div>
            </div>

            {/* Webhook Endpoints */}
            <div className="bg-white border border-gray-200 rounded-2xl p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-4 flex items-center gap-2">
                    <Activity size={20} className="text-gray-400" />
                    {t('shippingSetup.webhookEndpoints', 'Webhook Endpoints')}
                </h3>
                <div className="space-y-3">
                    {(health.supportedCarriers || []).map(code => {
                        const info = carrierInfo[code] || { name: code }
                        return (
                            <div key={code} className="flex items-center justify-between p-4 bg-gray-50 rounded-lg">
                                <div className="flex items-center gap-3">
                                    <CheckCircle className="text-green-500" size={18} />
                                    <span className="font-medium text-gray-900">{info.name}</span>
                                </div>
                                <code className="text-sm text-gray-500 bg-gray-200 px-2 py-1 rounded">
                                    /api/webhooks/shipping/{code}
                                </code>
                            </div>
                        )
                    })}
                </div>
            </div>

            {/* Tracking Sync Info */}
            <div className="bg-white border border-gray-200 rounded-2xl p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-4">
                    {t('shippingSetup.trackingSyncJob', 'Tracking Sync Job')}
                </h3>
                <div className="grid grid-cols-2 gap-4">
                    <div className="p-4 bg-blue-50 rounded-lg">
                        <p className="text-sm text-blue-600 mb-1">{t('shippingSetup.schedule', 'Schedule')}</p>
                        <p className="text-lg font-semibold text-blue-900">{t('shippingSetup.every30min', 'Every 30 minutes')}</p>
                    </div>
                    <div className="p-4 bg-gray-50 rounded-lg">
                        <p className="text-sm text-gray-600 mb-1">{t('shippingSetup.jobId', 'Job ID')}</p>
                        <code className="text-sm font-mono text-gray-900">shipping-tracking-sync</code>
                    </div>
                </div>
                <p className="text-sm text-gray-500 mt-4">
                    {t('shippingSetup.trackingSyncDescription', 'Automatically polls carrier APIs to update shipment statuses for all active shipments.')}
                </p>
            </div>
        </div>
    )
}
