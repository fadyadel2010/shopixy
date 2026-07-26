import React from 'react'
import { useTranslation } from 'react-i18next'

export default function CircuitBreakerBadge({ isOpen }) {
    const { t } = useTranslation()
    const normalizedValue = typeof isOpen === 'string'
        ? isOpen.trim().toLowerCase().replace(/[\s_-]/g, '')
        : isOpen
    const isClosedState = normalizedValue === 'closed'
    const isHalfOpenState = normalizedValue === 'halfopen'
    const isCircuitOpen = normalizedValue === 'open'
        || (Boolean(isOpen) && !isClosedState && !isHalfOpenState)

    // Support both the current boolean shape and backend enum strings.
    if (isCircuitOpen) {
        return (
            <span
                className="px-2 inline-flex text-xs leading-5 font-semibold rounded-full bg-red-100 text-red-800"
                title={t('whatsapp.analytics.circuitBreakerOpenTitle')}
            >
                {t('whatsapp.analytics.circuitBreakerOpenBadge')}
            </span>
        )
    }

    return (
        <span
            className="px-2 inline-flex text-xs leading-5 font-semibold rounded-full bg-green-100 text-green-800"
            title={t('whatsapp.analytics.circuitBreakerClosedTitle')}
        >
            {t('whatsapp.analytics.circuitBreakerClosedBadge')}
        </span>
    )
}
