import React, { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import type { CreateReviewRequest, MarketingPlatform } from '../types'

const RATING_VALUES = [1, 2, 3, 4, 5] as const

const MARKETING_PLATFORMS: MarketingPlatform[] = [
    'Facebook',
    'Instagram',
    'Google',
    'TikTok',
    'Twitter',
    'LinkedIn',
    'YouTube',
    'Snapchat',
    'Email',
    'WhatsApp',
    'Other',
]

function StarIcon({ filled }: { filled: boolean }) {
    return (
        <svg
            aria-hidden="true"
            viewBox="0 0 24 24"
            className="h-8 w-8"
            fill={filled ? 'currentColor' : 'none'}
            stroke="currentColor"
            strokeWidth="1.75"
            strokeLinecap="round"
            strokeLinejoin="round"
        >
            <path d="M12 3.75l2.55 5.17 5.7.83-4.12 4.01.97 5.67L12 16.75l-5.1 2.68.97-5.67-4.12-4.01 5.7-.83L12 3.75z" />
        </svg>
    )
}

interface ReviewModalProps {
    isRTL: boolean
    requestId: string
    serviceCategory?: string
    onSubmit: (review: CreateReviewRequest) => Promise<void>
    onClose: () => void
}

export function ReviewModal({ requestId, serviceCategory, onSubmit, onClose, isRTL }: ReviewModalProps) {
    const { t } = useTranslation()
    const isMarketing = serviceCategory?.toLowerCase() === 'marketing'

    const [rating, setRating] = useState<number | null>(null)
    const [comment, setComment] = useState('')
    const [isSubmitting, setIsSubmitting] = useState(false)
    const [campaignName, setCampaignName] = useState('')
    const [platform, setPlatform] = useState<MarketingPlatform>('Facebook')
    const [startDate, setStartDate] = useState('')
    const [endDate, setEndDate] = useState('')

    const panelRef = useRef<HTMLDivElement>(null)
    const previousActiveElement = useRef<HTMLElement | null>(null)
    const submitPendingRef = useRef(false)

    // Focus management: capture and restore focus
    useEffect(() => {
        previousActiveElement.current = document.activeElement as HTMLElement

        // Focus the panel or first focusable element
        const timer = setTimeout(() => {
            if (panelRef.current) {
                const focusable = panelRef.current.querySelectorAll<HTMLElement>(
                    'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
                )
                const firstFocusable = focusable[0]
                if (firstFocusable) {
                    firstFocusable.focus()
                } else {
                    panelRef.current.focus()
                }
            }
        }, 0)

        return () => {
            clearTimeout(timer)
            // Restore focus on unmount
            if (previousActiveElement.current) {
                previousActiveElement.current.focus()
            }
        }
    }, [])

    // Handle Escape key
    useEffect(() => {
        const handleKeyDown = (event: KeyboardEvent) => {
            if (event.key === 'Escape' && !isSubmitting) {
                onClose()
            }
        }
        document.addEventListener('keydown', handleKeyDown)
        return () => document.removeEventListener('keydown', handleKeyDown)
    }, [isSubmitting, onClose])

    // Focus trap
    const handlePanelKeyDown = (event: React.KeyboardEvent) => {
        if (event.key !== 'Tab' || !panelRef.current) return

        const focusable = panelRef.current.querySelectorAll<HTMLElement>(
            'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
        )
        const firstFocusable = focusable[0]
        const lastFocusable = focusable[focusable.length - 1]

        if (event.shiftKey && document.activeElement === firstFocusable) {
            event.preventDefault()
            lastFocusable?.focus()
        } else if (!event.shiftKey && document.activeElement === lastFocusable) {
            event.preventDefault()
            firstFocusable?.focus()
        }
    }

    const isMarketingValid = !isMarketing || (
        campaignName.trim() !== '' &&
        startDate !== '' &&
        endDate !== '' &&
        new Date(endDate) >= new Date(startDate)
    )

    const canSubmit = rating !== null && isMarketingValid && comment.length <= 5000

    const handleClose = () => {
        if (isSubmitting) {
            return
        }

        onClose()
    }

    const handleSubmit = async (event: React.FormEvent) => {
        event.preventDefault()
        if (!canSubmit || submitPendingRef.current) {
            return
        }

        submitPendingRef.current = true
        setIsSubmitting(true)

        try {
            const review: CreateReviewRequest = {
                serviceRequestId: requestId,
                rating,
                comment: comment || undefined,
            }

            if (isMarketing) {
                review.evidence = {
                    campaignName: campaignName.trim(),
                    platform,
                    startDate: new Date(startDate).toISOString(),
                    endDate: new Date(endDate).toISOString(),
                }
            }

            await onSubmit(review)
            onClose()
        } catch (error) {
            console.error('Failed to submit review:', error)
            window.alert(t('servicesMarketplace.review.saveFailed'))
        } finally {
            submitPendingRef.current = false
            setIsSubmitting(false)
        }
    }

    return (
        <div
            className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4"
            onClick={handleClose}
            dir={isRTL ? 'rtl' : 'ltr'}
        >
            <div
                ref={panelRef}
                role="dialog"
                aria-modal="true"
                aria-labelledby="review-modal-title"
                className="bg-white rounded-lg shadow-xl max-w-md w-full max-h-[90vh] overflow-y-auto"
                onClick={(event) => event.stopPropagation()}
                onKeyDown={handlePanelKeyDown}
                tabIndex={-1}
            >
                <div className="p-6">
                    <h3 id="review-modal-title" className="text-2xl font-bold text-gray-900 mb-1">
                        {t('servicesMarketplace.review.title')}
                    </h3>
                    {isMarketing ? (
                        <p className="text-sm text-indigo-600 mb-4">
                            {t('servicesMarketplace.review.marketingHint')}
                        </p>
                    ) : null}

                    <form onSubmit={handleSubmit} className="space-y-4">
                        <div>
                            <label className="label">{t('servicesMarketplace.review.fields.rating')}</label>
                            <div
                                role="radiogroup"
                                aria-label={t('servicesMarketplace.review.fields.rating')}
                                aria-required="true"
                                className={`flex gap-1 ${isRTL ? 'flex-row-reverse' : ''}`}
                            >
                                {RATING_VALUES.map((value) => {
                                    const isSelected = rating === value
                                    const isFilled = rating !== null && value <= rating

                                    return (
                                        <button
                                            key={value}
                                            type="button"
                                            role="radio"
                                            aria-checked={isSelected}
                                            aria-label={t('servicesMarketplace.review.ratingOption', {
                                                count: value,
                                            })}
                                            onClick={() => setRating(value)}
                                            className={`rounded-md p-1 transition-transform hover:scale-110 focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                                                isFilled ? 'text-amber-400' : 'text-gray-300'
                                            }`}
                                        >
                                            <StarIcon filled={isFilled} />
                                        </button>
                                    )
                                })}
                            </div>
                            {rating === null ? (
                                <p className="text-xs text-gray-500 mt-1">
                                    {t('servicesMarketplace.review.ratingRequired')}
                                </p>
                            ) : null}
                        </div>

                        {isMarketing ? (
                            <div className="space-y-3 p-4 bg-indigo-50 rounded-lg border border-indigo-200">
                                <h4 className="text-sm font-semibold text-indigo-800 uppercase tracking-wider">
                                    {t('servicesMarketplace.review.marketingEvidenceTitle')}
                                </h4>

                                <div>
                                    <label className="label">
                                        {t('servicesMarketplace.review.fields.campaignName')}
                                    </label>
                                    <input
                                        type="text"
                                        value={campaignName}
                                        onChange={(event) => setCampaignName(event.target.value)}
                                        className="input"
                                        placeholder={t('servicesMarketplace.review.placeholders.campaignName')}
                                        required
                                    />
                                </div>

                                <div>
                                    <label className="label">
                                        {t('servicesMarketplace.review.fields.platform')}
                                    </label>
                                    <select
                                        value={platform}
                                        onChange={(event) => setPlatform(event.target.value as MarketingPlatform)}
                                        className="input"
                                        required
                                    >
                                        {MARKETING_PLATFORMS.map((platformValue) => (
                                            <option key={platformValue} value={platformValue}>
                                                {t(`servicesMarketplace.platforms.${platformValue}`)}
                                            </option>
                                        ))}
                                    </select>
                                </div>

                                <div className="grid grid-cols-2 gap-3">
                                    <div>
                                        <label className="label">
                                            {t('servicesMarketplace.review.fields.startDate')}
                                        </label>
                                        <input
                                            type="date"
                                            value={startDate}
                                            onChange={(event) => setStartDate(event.target.value)}
                                            className="input"
                                            required
                                        />
                                    </div>
                                    <div>
                                        <label className="label">
                                            {t('servicesMarketplace.review.fields.endDate')}
                                        </label>
                                        <input
                                            type="date"
                                            value={endDate}
                                            onChange={(event) => setEndDate(event.target.value)}
                                            className="input"
                                            min={startDate || undefined}
                                            required
                                        />
                                    </div>
                                </div>

                                {startDate && endDate && new Date(endDate) < new Date(startDate) ? (
                                    <p className="text-xs text-red-600">
                                        {t('servicesMarketplace.review.endDateValidation')}
                                    </p>
                                ) : null}
                            </div>
                        ) : null}

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.review.fields.comment')}
                            </label>
                            <textarea
                                value={comment}
                                onChange={(event) => setComment(event.target.value)}
                                rows={4}
                                maxLength={5000}
                                className="input"
                                placeholder={t('servicesMarketplace.review.placeholders.comment')}
                            />
                            {comment.length > 4500 ? (
                                <p className={`text-xs mt-1 ${comment.length >= 5000 ? 'text-red-600' : 'text-gray-400'}`}>
                                    {comment.length}/5000
                                </p>
                            ) : null}
                        </div>

                        <div className="flex gap-3 pt-2">
                            <button
                                type="submit"
                                disabled={isSubmitting || !canSubmit}
                                className="btn-primary flex-1 disabled:opacity-50"
                            >
                                {isSubmitting
                                    ? t('servicesMarketplace.review.submitting')
                                    : t('servicesMarketplace.review.submit')}
                            </button>
                            <button
                                type="button"
                                disabled={isSubmitting}
                                onClick={handleClose}
                                className="btn-secondary disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {t('common.cancel')}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </div>
    )
}
