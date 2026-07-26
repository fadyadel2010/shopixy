/**
 * Online Customer Details Drawer Component
 * Phase P1: POS Admin - Online Customers Management
 * Phase 10.3.6: Added Wishlist Tab
 *
 * Slide-out drawer showing full customer details with tabs:
 * - Profile: Basic info (email, display name, phone)
 * - Sessions: Active session management
 * - Wishlist: Read-only wishlist view
 * - Security: Account status with activate/deactivate actions
 *
 * SECURITY:
 * - All changes are audited
 * - Deactivation requires reason
 * - Session revocation is immediate
 * - Wishlist is read-only
 */

import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import {
    X,
    User,
    Mail,
    Phone,
    Shield,
    Key,
    Calendar,
    Clock,
    CheckCircle,
    XCircle,
    Loader2,
    Power,
    PowerOff,
    Heart,
    Monitor
} from 'lucide-react'
import { onlineCustomersApi } from '../services'
import { OnlineCustomerSessions } from './OnlineCustomerSessions'
import { OnlineCustomerWishlist } from './OnlineCustomerWishlist'
import { ConfirmActionModal } from './ConfirmActionModal'
import { useToast } from '../../../components/Toast'
import { format, formatDistanceToNow } from 'date-fns'
import { ar, enUS } from 'date-fns/locale'

/**
 * Tab definitions
 */
const TABS = {
    PROFILE: 'profile',
    SESSIONS: 'sessions',
    WISHLIST: 'wishlist',
    SECURITY: 'security'
}

export function OnlineCustomerDetails({ customer, isOpen, onClose, onUpdate }) {
    const { t, i18n } = useTranslation()
    const currentCustomerId = customer?.id ?? null
    const [loading, setLoading] = useState(false)
    const [details, setDetails] = useState(null)
    const [detailsOwner, setDetailsOwner] = useState({ customerId: null, requestId: 0 })
    const [activeTab, setActiveTab] = useState(TABS.PROFILE)
    const [activeTabOwner, setActiveTabOwner] = useState({ customerId: null })
    const [wishlistCount, setWishlistCount] = useState(0)
    const [wishlistOwner, setWishlistOwner] = useState({ customerId: null })
    const [actionModal, setActionModal] = useState(null) // { type: 'activate' | 'deactivate' }
    const [actionLoading, setActionLoading] = useState(false)
    const requestIdRef = useRef(0)
    const isOpenRef = useRef(isOpen)
    const customerIdRef = useRef(currentCustomerId)

    isOpenRef.current = isOpen
    customerIdRef.current = currentCustomerId

    const isSameCustomerId = (left, right) => String(left ?? '').toLowerCase() === String(right ?? '').toLowerCase()

    const isCurrentDrawerState = (customerId, requestId = requestIdRef.current) => {
        return Boolean(customerId)
            && isOpenRef.current
            && isSameCustomerId(customerIdRef.current, customerId)
            && requestId === requestIdRef.current
    }

    const setActiveTabIfCurrent = (tab, customerId = currentCustomerId) => {
        if (!isCurrentDrawerState(customerId)) {
            return
        }

        setActiveTab(tab)
        setActiveTabOwner({ customerId })
    }

    const setWishlistCountIfCurrent = (count, customerId = currentCustomerId) => {
        if (!isCurrentDrawerState(customerId)) {
            return
        }

        setWishlistCount(count)
        setWishlistOwner({ customerId })
    }

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    useEffect(() => {
        if (!isOpen) {
            requestIdRef.current += 1
        }
    }, [isOpen])

    const { showSuccess, showError } = useToast()

    const fetchDetails = async (customerId = currentCustomerId) => {
        const targetCustomerId = customerId ?? null
        const startingRequestId = requestIdRef.current

        if (!targetCustomerId) {
            requestIdRef.current = startingRequestId + 1
            setDetails(null)
            setDetailsOwner({ customerId: null, requestId: requestIdRef.current })
            setLoading(false)
            return
        }

        const requestId = startingRequestId + 1
        requestIdRef.current = requestId

        if (!isCurrentDrawerState(targetCustomerId, requestId)) {
            return
        }

        setLoading(true)

        try {
            const data = await onlineCustomersApi.getOnlineCustomer(targetCustomerId)

            if (!isCurrentDrawerState(targetCustomerId, requestId) || !isSameCustomerId(data?.id, targetCustomerId)) {
                return
            }

            setDetails(data)
            setDetailsOwner({ customerId: targetCustomerId, requestId })
        } catch (error) {
            if (!isCurrentDrawerState(targetCustomerId, requestId)) {
                return
            }

            console.error('Failed to fetch customer details:', error)
            showError(t('common.noData'))
        } finally {
            if (isCurrentDrawerState(targetCustomerId, requestId)) {
                setLoading(false)
            }
        }
    }

    useEffect(() => {
        if (isOpen && currentCustomerId) {
            void fetchDetails(currentCustomerId)
            setDetails(null)
            setDetailsOwner({ customerId: currentCustomerId, requestId: requestIdRef.current })
            setWishlistCount(0)
            setWishlistOwner({ customerId: currentCustomerId })
            setActiveTabIfCurrent(TABS.PROFILE, currentCustomerId)
        }
    }, [isOpen, currentCustomerId])

    const handleActivate = () => {
        setActionModal({ type: 'activate' })
    }

    const handleDeactivate = () => {
        setActionModal({ type: 'deactivate' })
    }

    const confirmAction = async (reason) => {
        setActionLoading(true)
        try {
            if (actionModal.type === 'activate') {
                await onlineCustomersApi.activateCustomer(customer.id)
                showSuccess(t('onlineCustomers.details.activateSuccess'))
            } else {
                await onlineCustomersApi.deactivateCustomer(customer.id, reason)
                showSuccess(t('onlineCustomers.details.deactivateSuccess'))
            }
            setActionModal(null)
            await fetchDetails()
            onUpdate?.()
        } catch (error) {
            console.error('Failed to update customer status:', error)
            showError(error.response?.data?.message || t('common.error') || 'Error')
        } finally {
            setActionLoading(false)
        }
    }

    const getProviderIcon = (provider) => {
        const p = provider?.toLowerCase()
        if (p === 'google') return '🔵'
        if (p === 'facebook') return '🔷'
        return '🔑'
    }

    const getProviderLabel = (provider) => {
        const p = provider?.toLowerCase()
        if (p === 'google') return 'Google'
        if (p === 'facebook') return 'Facebook'
        return t('onlineCustomers.filters.allProviders') === 'All Providers' ? 'Email & Password' : 'البريد وكلمة المرور'
    }

    const getDateLocale = () => i18n.language.startsWith('ar') ? ar : enUS
    const isRtl = i18n.dir() === 'rtl'
    const currentRequestId = requestIdRef.current
    const detailsAreCurrent = Boolean(
        isOpen
        && currentCustomerId
        && isSameCustomerId(details?.id, currentCustomerId)
        && isSameCustomerId(detailsOwner.customerId, currentCustomerId)
        && detailsOwner.requestId === currentRequestId
    )
    const currentDetails = detailsAreCurrent ? details : null
    const currentWishlistCount = isOpen
        && currentCustomerId
        && isSameCustomerId(wishlistOwner.customerId, currentCustomerId)
        ? wishlistCount
        : 0
    const currentActiveTab = isOpen
        && currentCustomerId
        && isSameCustomerId(activeTabOwner.customerId, currentCustomerId)
        ? activeTab
        : TABS.PROFILE
    const isAwaitingCurrentDetails = Boolean(
        isOpen
        && currentCustomerId
        && !currentDetails
        && (
            loading
            || !isSameCustomerId(detailsOwner.customerId, currentCustomerId)
            || detailsOwner.requestId !== currentRequestId
        )
    )

    if (!isOpen) return null

    /**
     * Tab button component
     */
    const TabButton = ({ tab, label, icon: Icon, badge }) => (
        <button
            onClick={() => setActiveTabIfCurrent(tab, currentCustomerId)}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 transition-colors
                ${currentActiveTab === tab
                    ? 'text-primary-600 border-primary-600 bg-primary-50/50'
                    : 'text-gray-500 border-transparent hover:text-gray-700 hover:border-gray-300'
                }`}
        >
            <Icon className="w-4 h-4" />
            <span>{label}</span>
            {badge !== undefined && badge > 0 && (
                <span className="ml-1 px-1.5 py-0.5 text-xs font-medium bg-gray-100 text-gray-600 rounded-full">
                    {badge}
                </span>
            )}
        </button>
    )

    /**
     * Profile Tab Content
     */
    const ProfileContent = () => (
        <div className="space-y-6">
            {/* Account Status Banner */}
            <div className={`p-4 rounded-lg flex items-center justify-between ${currentDetails.isActive ? 'bg-green-50 border border-green-200' : 'bg-red-50 border border-red-200'
                }`}>
                <div className="flex items-center gap-3">
                    {currentDetails.isActive ? (
                        <CheckCircle className="w-5 h-5 text-green-600" />
                    ) : (
                        <XCircle className="w-5 h-5 text-red-600" />
                    )}
                    <div>
                        <span className={`text-sm font-medium ${currentDetails.isActive ? 'text-green-700' : 'text-red-700'}`}>
                            {currentDetails.isActive ? t('onlineCustomers.filters.active') : t('onlineCustomers.filters.inactive')}
                        </span>
                        <p className="text-xs text-gray-500 mt-0.5">
                            {t('onlineCustomers.details.accountStatus')}
                        </p>
                    </div>
                </div>
            </div>

            {/* Basic Info */}
            <div className="space-y-4">
                <h3 className="text-sm font-semibold text-gray-900 uppercase tracking-wider">
                    {t('onlineCustomers.details.basicInfo')}
                </h3>
                <div className="grid grid-cols-2 gap-4">
                    <div className="space-y-1">
                        <label className="text-xs text-gray-500 flex items-center gap-1">
                            <Mail className="w-3 h-3" /> {t('onlineCustomers.details.email')}
                        </label>
                        <p className="text-sm font-medium text-gray-900">{currentDetails.email}</p>
                    </div>
                    <div className="space-y-1">
                        <label className="text-xs text-gray-500 flex items-center gap-1">
                            <User className="w-3 h-3" /> {t('common.name')}
                        </label>
                        <p className="text-sm font-medium text-gray-900">{currentDetails.displayName || '—'}</p>
                    </div>
                    <div className="space-y-1">
                        <label className="text-xs text-gray-500 flex items-center gap-1">
                            <Phone className="w-3 h-3" /> {t('onlineCustomers.details.phone')}
                        </label>
                        <p className="text-sm font-medium text-gray-900">{currentDetails.phone || '—'}</p>
                    </div>
                    <div className="space-y-1">
                        <label className="text-xs text-gray-500 flex items-center gap-1">
                            <Shield className="w-3 h-3" /> {t('onlineCustomers.table.provider')}
                        </label>
                        <p className="text-sm font-medium text-gray-900 flex items-center gap-1.5">
                            {getProviderIcon(currentDetails.provider)}
                            {getProviderLabel(currentDetails.provider)}
                        </p>
                    </div>
                </div>
            </div>

            {/* Account Details */}
            <div className="space-y-4">
                <h3 className="text-sm font-semibold text-gray-900 uppercase tracking-wider">
                    {t('onlineCustomers.details.accountStatus')}
                </h3>
                <div className="grid grid-cols-2 gap-4">
                    <div className="space-y-1">
                        <label className="text-xs text-gray-500 flex items-center gap-1">
                            <Mail className="w-3 h-3" /> {t('onlineCustomers.table.emailVerified')}
                        </label>
                        <p className="text-sm font-medium flex items-center gap-1.5">
                            {currentDetails.isEmailVerified ? (
                                <>
                                    <CheckCircle className="w-4 h-4 text-green-500" />
                                    <span className="text-green-600">{t('onlineCustomers.filters.verified')}</span>
                                </>
                            ) : (
                                <>
                                    <XCircle className="w-4 h-4 text-amber-500" />
                                    <span className="text-amber-600">{t('onlineCustomers.filters.unverified')}</span>
                                </>
                            )}
                        </p>
                    </div>
                    <div className="space-y-1">
                        <label className="text-xs text-gray-500 flex items-center gap-1">
                            <Key className="w-3 h-3" /> {t('common.password')}
                        </label>
                        <p className="text-sm font-medium text-gray-900">
                            {currentDetails.provider === 'local' ? t('common.yes') : t('common.no')}
                        </p>
                    </div>
                    <div className="space-y-1">
                        <label className="text-xs text-gray-500 flex items-center gap-1">
                            <Calendar className="w-3 h-3" /> {t('onlineCustomers.details.createdAt')}
                        </label>
                        <p className="text-sm font-medium text-gray-900">
                            {format(new Date(currentDetails.createdAtUtc), 'MMM d, yyyy', { locale: getDateLocale() })}
                        </p>
                        <p className="text-xs text-gray-400">
                            {formatDistanceToNow(new Date(currentDetails.createdAtUtc), { addSuffix: true, locale: getDateLocale() })}
                        </p>
                    </div>
                    <div className="space-y-1">
                        <label className="text-xs text-gray-500 flex items-center gap-1">
                            <Clock className="w-3 h-3" /> {t('onlineCustomers.table.lastLogin')}
                        </label>
                        <p className="text-sm font-medium text-gray-900">
                            {currentDetails.lastLoginAtUtc
                                ? format(new Date(currentDetails.lastLoginAtUtc), 'MMM d, yyyy h:mm a', { locale: getDateLocale() })
                                : t('common.never')
                            }
                        </p>
                        {currentDetails.lastLoginAtUtc && (
                            <p className="text-xs text-gray-400">
                                {formatDistanceToNow(new Date(currentDetails.lastLoginAtUtc), { addSuffix: true, locale: getDateLocale() })}
                            </p>
                        )}
                    </div>
                </div>
            </div>
        </div>
    )

    /**
     * Security Tab Content
     */
    const SecurityContent = () => (
        <div className="space-y-6">
            {/* Account Status Banner with Action */}
            <div className={`p-4 rounded-lg flex items-center justify-between ${currentDetails.isActive ? 'bg-green-50 border border-green-200' : 'bg-red-50 border border-red-200'
                }`}>
                <div className="flex items-center gap-3">
                    {currentDetails.isActive ? (
                        <CheckCircle className="w-5 h-5 text-green-600" />
                    ) : (
                        <XCircle className="w-5 h-5 text-red-600" />
                    )}
                    <div>
                        <span className={`text-sm font-medium ${currentDetails.isActive ? 'text-green-700' : 'text-red-700'}`}>
                            {currentDetails.isActive ? t('onlineCustomers.filters.active') : t('onlineCustomers.filters.inactive')}
                        </span>
                        <p className="text-xs text-gray-500 mt-0.5">
                            {currentDetails.isActive
                                ? t('onlineCustomers.details.accountStatus')
                                : t('onlineCustomers.details.accountStatus')
                            }
                        </p>
                    </div>
                </div>
                {currentDetails.isActive ? (
                    <button
                        onClick={handleDeactivate}
                        className="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-red-600 bg-white border border-red-200 rounded-lg hover:bg-red-50"
                    >
                        <PowerOff className="w-4 h-4" />
                        {t('onlineCustomers.details.deactivate')}
                    </button>
                ) : (
                    <button
                        onClick={handleActivate}
                        className="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-green-600 bg-white border border-green-200 rounded-lg hover:bg-green-50"
                    >
                        <Power className="w-4 h-4" />
                        {t('onlineCustomers.details.activate')}
                    </button>
                )}
            </div>

            {/* Security Info */}
            <div className="space-y-4">
                <h3 className="text-sm font-semibold text-gray-900 uppercase tracking-wider">
                    {t('onlineCustomers.security.title')}
                </h3>
                <div className="text-sm text-gray-600 space-y-2">
                    <p>• {t('onlineCustomers.security.deactivateInfo')}</p>
                    <p>• {t('onlineCustomers.security.noDelete')}</p>
                    <p>• {t('onlineCustomers.security.auditLog')}</p>
                </div>
            </div>

            {/* Active Sessions Count */}
            {currentDetails.activeSessions !== undefined && (
                <div className="p-4 bg-gray-50 rounded-lg">
                    <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                            <Monitor className="w-5 h-5 text-gray-500" />
                            <span className="text-sm font-medium text-gray-700">
                                {t('onlineCustomers.table.sessions')}
                            </span>
                        </div>
                        <span className={`px-2 py-1 text-xs font-medium rounded-full ${currentDetails.activeSessions > 0
                                ? 'bg-blue-100 text-blue-700'
                                : 'bg-gray-100 text-gray-600'
                            }`}>
                            {currentDetails.activeSessions} {t('onlineCustomers.filters.active').toLowerCase()}
                        </span>
                    </div>
                </div>
            )}
        </div>
    )

    return (
        <>
            {/* Backdrop */}
            <div
                className="fixed inset-0 bg-black bg-opacity-50 z-40 transition-opacity"
                onClick={onClose}
            />

            {/* Drawer */}
            <div className={`fixed inset-y-0 ${isRtl ? 'left-0' : 'right-0'} w-full max-w-lg bg-white shadow-xl z-50 flex flex-col ${isRtl ? 'animate-slide-in-left' : 'animate-slide-in-right'}`}>
                {/* Header */}
                <div className="flex items-center justify-between px-6 py-4 border-b bg-gray-50">
                    <div className="flex items-center gap-3">
                        <div className="p-2 bg-primary-100 rounded-full">
                            <User className="w-5 h-5 text-primary-600" />
                        </div>
                        <div>
                            <h2 className="text-lg font-semibold text-gray-900">{t('onlineCustomers.details.title')}</h2>
                            <p className="text-sm text-gray-500">{customer?.email}</p>
                        </div>
                    </div>
                    <button
                        onClick={onClose}
                        className="p-2 text-gray-400 hover:text-gray-600 rounded-lg hover:bg-gray-100"
                    >
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Tabs */}
                <div className="flex border-b bg-white overflow-x-auto">
                    <TabButton
                        tab={TABS.PROFILE}
                        label={t('onlineCustomers.tabs.profile')}
                        icon={User}
                    />
                    <TabButton
                        tab={TABS.SESSIONS}
                        label={t('onlineCustomers.tabs.sessions')}
                        icon={Monitor}
                        badge={currentDetails?.activeSessions}
                    />
                    <TabButton
                        tab={TABS.WISHLIST}
                        label={t('onlineCustomers.tabs.wishlist')}
                        icon={Heart}
                        badge={currentWishlistCount}
                    />
                    <TabButton
                        tab={TABS.SECURITY}
                        label={t('onlineCustomers.tabs.security')}
                        icon={Shield}
                    />
                </div>

                {/* Content */}
                <div className="flex-1 overflow-y-auto">
                    {isAwaitingCurrentDetails ? (
                        <div className="flex items-center justify-center py-16">
                            <Loader2 className="w-8 h-8 animate-spin text-primary-600" />
                        </div>
                    ) : currentDetails ? (
                        <div className="p-6">
                            {currentActiveTab === TABS.PROFILE && <ProfileContent />}
                            {currentActiveTab === TABS.SESSIONS && (
                                <OnlineCustomerSessions
                                    customerId={customer.id}
                                    onSessionsChange={fetchDetails}
                                />
                            )}
                            {currentActiveTab === TABS.WISHLIST && (
                                <OnlineCustomerWishlist
                                    customerId={customer.id}
                                    onWishlistLoaded={(count) => setWishlistCountIfCurrent(count, currentCustomerId)}
                                />
                            )}
                            {currentActiveTab === TABS.SECURITY && <SecurityContent />}
                        </div>
                    ) : (
                        <div className="flex items-center justify-center py-16 text-gray-500">
                            {t('common.noData')}
                        </div>
                    )}
                </div>
            </div>

            {/* Activate/Deactivate Confirmation Modal */}
            <ConfirmActionModal
                isOpen={!!actionModal}
                onClose={() => setActionModal(null)}
                onConfirm={confirmAction}
                title={actionModal?.type === 'activate' ? t('onlineCustomers.details.activate') : t('onlineCustomers.details.deactivate')}
                message={
                    actionModal?.type === 'activate'
                        ? t('onlineCustomers.details.activateConfirm')
                        : t('onlineCustomers.details.deactivateWarning')
                }
                confirmLabel={actionModal?.type === 'activate' ? t('onlineCustomers.details.activate') : t('onlineCustomers.details.deactivate')}
                variant={actionModal?.type === 'activate' ? 'info' : 'danger'}
                requireReason={actionModal?.type === 'deactivate'}
                reasonLabel={t('onlineCustomers.sessions.reasonLabel')}
                reasonPlaceholder={t('onlineCustomers.sessions.reasonPlaceholder')}
                loading={actionLoading}
            />

            <style>{`
                @keyframes slideInRight {
                    from { transform: translateX(100%); }
                    to { transform: translateX(0); }
                }
                @keyframes slideInLeft {
                    from { transform: translateX(-100%); }
                    to { transform: translateX(0); }
                }
                .animate-slide-in-right {
                    animation: slideInRight 0.2s ease-out;
                }
                .animate-slide-in-left {
                    animation: slideInLeft 0.2s ease-out;
                }
            `}</style>
        </>
    )
}

export default OnlineCustomerDetails
