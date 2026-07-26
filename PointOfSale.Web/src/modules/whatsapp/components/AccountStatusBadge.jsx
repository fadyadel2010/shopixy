import React from 'react'
import { useTranslation } from 'react-i18next'
import { WHATSAPP_ACCOUNT_STATUS_CONFIG } from '../i18n'

/**
 * WhatsApp Account Status Badge
 * 
 * AUDIT FIX: Added all Baileys session states for proper UI feedback
 * Backend states: DISCONNECTED, CONNECTING, QR_PENDING, CONNECTED, POISONED, LOGGED_OUT
 */
export const STATUS_CONFIG = WHATSAPP_ACCOUNT_STATUS_CONFIG

export default function AccountStatusBadge({ status }) {
    const { t } = useTranslation()
    const config = STATUS_CONFIG[status] || { color: 'bg-gray-100 text-gray-800' }
    const label = config.labelKey ? t(config.labelKey) : status
    const description = config.descriptionKey ? t(config.descriptionKey) : null

    return (
        <div className="flex flex-col items-end">
            <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${config.color}`}>
                {config.isLoading && (
                    <svg className="animate-spin -ml-0.5 mr-1 h-3 w-3" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                )}
                {label}
            </span>
            {description && (
                <span className="text-xs text-gray-500 mt-1 max-w-[200px] text-right">
                    {description}
                </span>
            )}
        </div>
    )
}
