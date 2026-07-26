import React, { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { QrCode, Power, Trash2, RefreshCw, AlertCircle } from 'lucide-react'
import AccountStatusBadge from './AccountStatusBadge'
import CircuitBreakerBadge from './CircuitBreakerBadge'
import { useWhatsAppApi } from '../hooks/useWhatsAppApi'
import { useToast } from '../../../components/Toast'

/**
 * WhatsApp Account Card
 * 
 * AUDIT FIXES:
 * - Added QR countdown timer (60s expiry)
 * - Added status polling to detect scan success
 * - Added "Refresh QR" button in modal
 * - Fixed button disabled states for terminal states
 * - Added lastError display
 */
export default function AccountCard({ account, onRefresh }) {
    const { t } = useTranslation()
    const { toggleAccount, disconnectAccount, getQrCode, getAccountStatus } = useWhatsAppApi()
    const { showSuccess, showError } = useToast()
    const [loading, setLoading] = useState(false)
    const [showQr, setShowQr] = useState(false)
    const [qrData, setQrData] = useState(null)
    const [qrError, setQrError] = useState(null)
    const [qrCountdown, setQrCountdown] = useState(60)
    const [qrExpired, setQrExpired] = useState(false)
    const statusPollRef = useRef(null)
    const countdownRef = useRef(null)

    // QR expiry countdown timer
    useEffect(() => {
        if (!showQr || !qrData) return

        setQrCountdown(60)
        setQrExpired(false)

        countdownRef.current = setInterval(() => {
            setQrCountdown(prev => {
                if (prev <= 1) {
                    setQrExpired(true)
                    clearInterval(countdownRef.current)
                    return 0
                }
                return prev - 1
            })
        }, 1000)

        return () => {
            if (countdownRef.current) clearInterval(countdownRef.current)
        }
    }, [showQr, qrData])

    // Status polling to detect scan success
    useEffect(() => {
        if (!showQr) return

        statusPollRef.current = setInterval(async () => {
            try {
                const status = await getAccountStatus(account.id)
                if (status?.isConnected) {
                    showSuccess(t('whatsapp.accounts.success.connected'))
                    setShowQr(false)
                    onRefresh()
                }
            } catch (err) {
                // Silently ignore polling errors
                console.debug('Status poll failed:', err)
            }
        }, 3000)

        return () => {
            if (statusPollRef.current) clearInterval(statusPollRef.current)
        }
    }, [account.id, getAccountStatus, onRefresh, showSuccess, showQr, t])

    const handleToggle = async () => {
        if (!confirm(t(account.enabled ? 'whatsapp.accounts.confirm.disable' : 'whatsapp.accounts.confirm.enable'))) return

        setLoading(true)
        try {
            await toggleAccount(account.id, !account.enabled)
            showSuccess(t(account.enabled ? 'whatsapp.accounts.success.disabled' : 'whatsapp.accounts.success.enabled'))
            onRefresh()
        } catch (error) {
            showError(error.message)
        } finally {
            setLoading(false)
        }
    }

    const handleDisconnect = async () => {
        if (!confirm(t('whatsapp.accounts.confirm.disconnect'))) return

        setLoading(true)
        try {
            await disconnectAccount(account.id)
            showSuccess(t('whatsapp.accounts.success.disconnected'))
            onRefresh()
        } catch (error) {
            showError(error.message)
        } finally {
            setLoading(false)
        }
    }

    const handleShowQr = async () => {
        setLoading(true)
        setQrError(null)
        setQrExpired(false)
        try {
            const data = await getQrCode(account.id)
            if (data.alreadyConnected) {
                showSuccess(t('whatsapp.accounts.success.alreadyConnected'))
                onRefresh()
                return
            }
            setQrData(data.qrImageBase64 || data.qrCodeBase64)
            setShowQr(true)
        } catch (error) {
            setQrError(error.message)
            showError(error.message)
        } finally {
            setLoading(false)
        }
    }

    const handleRefreshQr = async () => {
        setLoading(true)
        setQrError(null)
        setQrExpired(false)
        try {
            const data = await getQrCode(account.id)
            if (data.alreadyConnected) {
                showSuccess(t('whatsapp.accounts.success.alreadyConnected'))
                setShowQr(false)
                onRefresh()
                return
            }
            setQrData(data.qrImageBase64 || data.qrCodeBase64)
        } catch (error) {
            setQrError(error.message)
            showError(error.message)
        } finally {
            setLoading(false)
        }
    }

    // Determine button states based on status
    const isTerminalState = ['POISONED', 'LOGGED_OUT'].includes(account.status)
    const isConnecting = account.status === 'CONNECTING'
    const isConnected = ['Connected', 'CONNECTED'].includes(account.status)
    const isDisconnected = ['Disconnected', 'DISCONNECTED'].includes(account.status) || isTerminalState

    return (
        <>
            <div className="bg-white overflow-hidden shadow rounded-lg divide-y divide-gray-200">
                <div className="px-4 py-5 sm:px-6 flex justify-between items-center">
                    <div>
                        <h3 className="text-lg leading-6 font-medium text-gray-900">{account.displayName}</h3>
                        <p className="mt-1 text-sm text-gray-500">{account.phoneNumber}</p>
                    </div>
                    <AccountStatusBadge status={account.status} />
                </div>

                {/* Last Error Display - AUDIT FIX */}
                {account.lastError && (
                    <div className="px-4 py-3 bg-red-50 flex items-start space-x-2">
                        <AlertCircle className="h-5 w-5 text-red-500 flex-shrink-0 mt-0.5" />
                        <div>
                            <p className="text-sm text-red-700 font-medium">{t('whatsapp.accounts.lastError')}</p>
                            <p className="text-sm text-red-600">{account.lastError}</p>
                        </div>
                    </div>
                )}

                <div className="px-4 py-5 sm:p-6">
                    <div className="flex space-x-2 mb-4">
                        <span className={`inline-flex items-center px-2.5 py-0.5 rounded-md text-sm font-medium ${account.useForOrderConfirmation ? 'bg-blue-100 text-blue-800' : 'bg-gray-100 text-gray-800'}`}>
                            {t('whatsapp.types.orderConfirmation')}
                        </span>
                        <span className={`inline-flex items-center px-2.5 py-0.5 rounded-md text-sm font-medium ${account.useForOtpVerification ? 'bg-purple-100 text-purple-800' : 'bg-gray-100 text-gray-800'}`}>
                            {t('whatsapp.types.otp')}
                        </span>
                        <CircuitBreakerBadge isOpen={false} />
                    </div>
                </div>
                <div className="px-4 py-4 sm:px-6 flex space-x-2 text-sm text-gray-500">
                    <button
                        onClick={handleShowQr}
                        disabled={loading || isConnected || isConnecting}
                        className="inline-flex items-center px-3 py-1.5 border border-gray-300 shadow-sm text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 focus:outline-none disabled:opacity-50"
                        title={
                            isConnected
                                ? t('whatsapp.accounts.titles.alreadyConnected')
                                : isConnecting
                                    ? t('whatsapp.accounts.titles.connectionInProgress')
                                    : t('whatsapp.accounts.titles.scanQrToConnect')
                        }
                    >
                        <QrCode className="-ml-0.5 mr-2 h-4 w-4" aria-hidden="true" />
                        {isTerminalState ? t('whatsapp.accounts.actions.reconnect') : t('whatsapp.accounts.scanQr')}
                    </button>

                    <button
                        onClick={handleToggle}
                        disabled={loading || !isConnected}
                        className={`inline-flex items-center px-3 py-1.5 border border-transparent shadow-sm text-xs font-medium rounded text-white ${account.enabled ? 'bg-red-600 hover:bg-red-700' : 'bg-green-600 hover:bg-green-700'} focus:outline-none disabled:opacity-50`}
                    >
                        <Power className="-ml-0.5 mr-2 h-4 w-4" aria-hidden="true" />
                        {account.enabled ? t('common.disable') : t('common.enable')}
                    </button>

                    <button
                        onClick={handleDisconnect}
                        disabled={loading}
                        className="inline-flex items-center px-3 py-1.5 border border-gray-300 shadow-sm text-xs font-medium rounded text-red-700 bg-white hover:bg-gray-50 focus:outline-none disabled:opacity-50 ml-auto"
                        title={t('whatsapp.accounts.titles.disconnectAndReset')}
                    >
                        <Trash2 className="-ml-0.5 mr-2 h-4 w-4" aria-hidden="true" />
                        {t('whatsapp.accounts.actions.disconnect')}
                    </button>
                </div>
            </div>

            {/* QR Modal - AUDIT FIX: Enhanced with countdown and refresh */}
            {showQr && (
                <div className="fixed z-10 inset-0 overflow-y-auto" aria-labelledby="modal-title" role="dialog" aria-modal="true">
                    <div className="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
                        <div className="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" aria-hidden="true" onClick={() => setShowQr(false)}></div>
                        <span className="hidden sm:inline-block sm:align-middle sm:h-screen" aria-hidden="true">&#8203;</span>
                        <div className="inline-block align-bottom bg-white rounded-lg px-4 pt-5 pb-4 text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-sm sm:w-full sm:p-6">
                            <div>
                                <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-green-100">
                                    <QrCode className="h-6 w-6 text-green-600" aria-hidden="true" />
                                </div>
                                <div className="mt-3 text-center sm:mt-5">
                                    <h3 className="text-lg leading-6 font-medium text-gray-900" id="modal-title">
                                        {t('whatsapp.accounts.qr.title')}
                                    </h3>
                                    <div className="mt-2">
                                        <p className="text-sm text-gray-500 mb-4">
                                            {t('whatsapp.accounts.qr.description')}
                                        </p>

                                        {qrError ? (
                                            <div className="h-48 w-48 mx-auto bg-red-50 flex flex-col items-center justify-center p-4 rounded">
                                                <AlertCircle className="h-8 w-8 text-red-500 mb-2" />
                                                <p className="text-sm text-red-600 text-center">{qrError}</p>
                                            </div>
                                        ) : qrExpired ? (
                                            <div className="h-48 w-48 mx-auto bg-yellow-50 flex flex-col items-center justify-center p-4 rounded">
                                                <RefreshCw className="h-8 w-8 text-yellow-500 mb-2" />
                                                <p className="text-sm text-yellow-700 text-center font-medium">{t('whatsapp.accounts.qr.expiredTitle')}</p>
                                                <p className="text-xs text-yellow-600 text-center mt-1">{t('whatsapp.accounts.qr.expiredHint')}</p>
                                            </div>
                                        ) : qrData ? (
                                            <img src={qrData} alt={t('whatsapp.accounts.qr.alt')} className="mx-auto border p-2" />
                                        ) : (
                                            <div className="h-48 w-48 mx-auto bg-gray-100 flex items-center justify-center">
                                                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
                                            </div>
                                        )}

                                        {/* Countdown Timer */}
                                        {!qrExpired && !qrError && qrData && (
                                            <p className={`text-sm mt-2 ${qrCountdown <= 10 ? 'text-red-500 font-medium' : 'text-gray-400'}`}>
                                                {t('whatsapp.accounts.qr.expiresIn', { count: qrCountdown })}
                                            </p>
                                        )}
                                    </div>
                                </div>
                            </div>
                            <div className="mt-5 sm:mt-6 flex space-x-3">
                                <button
                                    type="button"
                                    disabled={loading}
                                    className="flex-1 inline-flex justify-center items-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none sm:text-sm disabled:opacity-50"
                                    onClick={handleRefreshQr}
                                >
                                    <RefreshCw className={`-ml-1 mr-2 h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
                                    {t('whatsapp.accounts.actions.refreshQr')}
                                </button>
                                <button
                                    type="button"
                                    className="flex-1 inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-indigo-600 text-base font-medium text-white hover:bg-indigo-700 focus:outline-none sm:text-sm"
                                    onClick={() => setShowQr(false)}
                                >
                                    {t('whatsapp.accounts.actions.done')}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </>
    )
}

