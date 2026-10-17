/**
 * AF-23: Affiliate Readiness Warning Component
 * 
 * Reusable component that displays a warning card when affiliate prerequisites are not met.
 * Shows a checklist of requirements with action buttons to navigate to setup pages.
 * 
 * USAGE:
 * - Product Edit Modal: Inline warning inside affiliate card
 * - Affiliate Management Page: Full-page empty state
 * 
 * PROPS:
 * - storefrontLive: boolean
 * - hasPaymentMethod: boolean
 * - hasShippingMethod: boolean
 * - variant: 'inline' | 'fullpage' (default: 'inline')
 */
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

function ChecklistItem({ checked, label, actionLabel, actionPath, completeLabel, incompleteLabel }) {
    const navigate = useNavigate()

    return (
        <div className="flex items-center justify-between p-3 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors">
            <div className="flex items-center gap-3">
                {checked ? (
                    <div className="flex-shrink-0 w-6 h-6 bg-green-100 rounded-full flex items-center justify-center">
                        <svg aria-hidden="true" className="w-4 h-4 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                        </svg>
                    </div>
                ) : (
                    <div className="flex-shrink-0 w-6 h-6 bg-red-100 rounded-full flex items-center justify-center">
                        <svg aria-hidden="true" className="w-4 h-4 text-red-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </div>
                )}
                <span className={`text-sm font-medium ${checked ? 'text-gray-900' : 'text-gray-600'}`}>
                    {label}
                    <span className="sr-only">: {checked ? completeLabel : incompleteLabel}</span>
                </span>
            </div>
            {!checked && actionPath && (
                <button
                    type="button"
                    onClick={() => navigate(actionPath)}
                    className="text-sm text-primary-600 hover:text-primary-700 font-medium"
                >
                    {actionLabel}
                </button>
            )}
        </div>
    )
}

export default function AffiliateReadinessWarning({
    storefrontLive,
    hasPaymentMethod,
    hasShippingMethod,
    variant = 'inline'
}) {
    const { t } = useTranslation()
    const isFullPage = variant === 'fullpage'
    const checklistStatus = {
        completeLabel: t('affiliateReadiness.complete'),
        incompleteLabel: t('affiliateReadiness.incomplete'),
    }

    if (isFullPage) {
        return (
            <div className="flex flex-col items-center justify-center py-16 px-4">
                <div className="max-w-2xl w-full">
                    {/* Icon */}
                    <div className="flex justify-center mb-6">
                        <div className="w-20 h-20 bg-amber-100 rounded-full flex items-center justify-center">
                            <svg aria-hidden="true" className="w-10 h-10 text-amber-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                            </svg>
                        </div>
                    </div>

                    {/* Title */}
                    <div className="text-center mb-8">
                        <h2 className="text-2xl font-bold text-gray-900 mb-2">
                            {t('affiliateReadiness.fullTitle')}
                        </h2>
                        <p className="text-gray-600">
                            {t('affiliateReadiness.fullDescription')}
                        </p>
                    </div>

                    {/* Checklist */}
                    <div className="space-y-3 mb-8">
                        <ChecklistItem
                            checked={storefrontLive}
                            label={t('affiliateReadiness.storefront')}
                            actionLabel={t('affiliateReadiness.storefrontAction')}
                            actionPath="/storefront/status"
                            {...checklistStatus}
                        />
                        <ChecklistItem
                            checked={hasPaymentMethod}
                            label={t('affiliateReadiness.payment')}
                            actionLabel={t('affiliateReadiness.paymentAction')}
                            actionPath="/payment-methods"
                            {...checklistStatus}
                        />
                        <ChecklistItem
                            checked={hasShippingMethod}
                            label={t('affiliateReadiness.shipping')}
                            actionLabel={t('affiliateReadiness.shippingAction')}
                            actionPath="/shipping-methods"
                            {...checklistStatus}
                        />
                    </div>

                    {/* Info Box */}
                    <div className="p-4 bg-blue-50 border border-blue-200 rounded-lg">
                        <div className="flex gap-3">
                            <svg aria-hidden="true" className="w-5 h-5 text-blue-600 flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                            </svg>
                            <div className="text-sm text-blue-800">
                                <p className="font-medium mb-1">{t('affiliateReadiness.whyTitle')}</p>
                                <p>{t('affiliateReadiness.whyDescription')}</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        )
    }

    // Inline variant (for product edit modal)
    return (
        <div className="space-y-4">
            {/* Warning Alert */}
            <div className="p-4 bg-amber-50 border border-amber-200 rounded-lg">
                <div className="flex gap-3">
                    <svg aria-hidden="true" className="w-5 h-5 text-amber-600 flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                    </svg>
                    <div className="flex-1">
                        <h4 className="text-sm font-semibold text-amber-900 mb-1">
                            {t('affiliateReadiness.inlineTitle')}
                        </h4>
                        <p className="text-sm text-amber-800">
                            {t('affiliateReadiness.inlineDescription')}
                        </p>
                    </div>
                </div>
            </div>

            {/* Checklist */}
            <div className="space-y-2">
                <ChecklistItem
                    checked={storefrontLive}
                    label={t('affiliateReadiness.storefront')}
                    actionLabel={t('affiliateReadiness.setup')}
                    actionPath="/storefront/status"
                    {...checklistStatus}
                />
                <ChecklistItem
                    checked={hasPaymentMethod}
                    label={t('affiliateReadiness.payment')}
                    actionLabel={t('affiliateReadiness.setup')}
                    actionPath="/payment-methods"
                    {...checklistStatus}
                />
                <ChecklistItem
                    checked={hasShippingMethod}
                    label={t('affiliateReadiness.shipping')}
                    actionLabel={t('affiliateReadiness.setup')}
                    actionPath="/shipping-methods"
                    {...checklistStatus}
                />
            </div>
        </div>
    )
}
