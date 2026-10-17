import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import type { ServiceResult } from '../types'
import type { PresentationFormatting } from '../presentationFormatting'
import { deriveListingCardPresentation } from '../listingCardAuthority'

interface ServiceCardProps extends Pick<PresentationFormatting, 'formatCurrency' | 'formatNumber' | 'isRTL'> {
    service: ServiceResult
}

const trustBadgeColorMap: Record<string, string> = {
    TopRated: 'bg-yellow-100 text-yellow-800 border-yellow-200',
    Trusted: 'bg-blue-100 text-blue-800 border-blue-200',
    Rising: 'bg-green-100 text-green-800 border-green-200',
    New: 'bg-emerald-100 text-emerald-800 border-emerald-200',
    Standard: 'bg-gray-100 text-gray-700 border-gray-200',
}

const trustBadgeTranslationMap: Record<string, string> = {
    TopRated: 'servicesMarketplace.badges.topRated',
    Trusted: 'servicesMarketplace.badges.trusted',
    Rising: 'servicesMarketplace.badges.rising',
    New: 'servicesMarketplace.badges.new',
    Standard: 'servicesMarketplace.badges.standard',
}

export function ServiceCard({ service, formatCurrency, formatNumber, isRTL }: ServiceCardProps) {
    const { t } = useTranslation()
    const card = deriveListingCardPresentation(service)

    if (!card) return null

    const trustBadgeClass =
        trustBadgeColorMap[card.trustBadge ?? ''] || 'bg-gray-100 text-gray-600 border-gray-200'
    const trustBadgeKey = trustBadgeTranslationMap[card.trustBadge ?? '']
    const categoryLabel = card.category
        ? t(`servicesMarketplace.categories.${card.category}`, { defaultValue: card.category })
        : null

    return (
        <Link to={`/marketplace/service/${encodeURIComponent(card.serviceId)}`}>
            <div className="bg-white rounded-lg shadow hover:shadow-lg transition-shadow duration-200 overflow-hidden h-full flex flex-col">
                <div className="relative h-48 bg-gradient-to-br from-indigo-500 to-purple-600 overflow-hidden">
                    {card.primaryImage ? (
                        <img
                            src={card.primaryImage}
                            alt={card.title}
                            className="w-full h-full object-cover"
                            onError={(event) => {
                                event.currentTarget.style.display = 'none'
                            }}
                        />
                    ) : (
                        <div className="w-full h-full flex items-center justify-center text-white text-6xl">
                            <span aria-hidden="true">+</span>
                        </div>
                    )}

                    {trustBadgeKey ? (
                        <div className={`absolute top-3 ${isRTL ? 'left-3' : 'right-3'}`}>
                            <span className={`px-3 py-1 text-xs font-semibold rounded-full border ${trustBadgeClass}`}>
                                {t(trustBadgeKey)}
                            </span>
                        </div>
                    ) : null}
                </div>

                <div className="p-5 flex-1 flex flex-col">
                    <h3 className="text-lg font-semibold text-gray-900 mb-2 line-clamp-2">
                        {card.title}
                    </h3>

                    <p className="text-sm text-gray-600 mb-4 line-clamp-2 flex-1">
                        {card.description}
                    </p>

                    {categoryLabel ? (
                        <div className="mb-3">
                            <span className="inline-flex rounded-full bg-indigo-50 px-2.5 py-1 text-xs font-medium text-indigo-700">
                                {categoryLabel}
                            </span>
                        </div>
                    ) : null}

                    <div className="flex items-center gap-2 mb-4 pb-4 border-b border-gray-100">
                        <div className="w-8 h-8 rounded-full bg-gradient-to-br from-indigo-500 to-purple-600 flex items-center justify-center text-white text-sm font-semibold">
                            {card.providerInitial}
                        </div>
                        <span className="text-sm text-gray-700 font-medium">
                            {card.providerDisplayName}
                        </span>
                    </div>

                    <div className={`flex items-center justify-between ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <div>
                            {card.averageRating !== null && card.totalReviews !== null ? (
                                <div className={`flex items-center gap-1 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                    <span className="text-yellow-500" aria-hidden="true">*</span>
                                    <span className="text-sm font-semibold text-gray-900">
                                        {formatNumber(card.averageRating, {
                                            minimumFractionDigits: 1,
                                            maximumFractionDigits: 1,
                                        })}
                                    </span>
                                    <span className="text-xs text-gray-500">
                                        ({formatNumber(card.totalReviews)})
                                    </span>
                                </div>
                            ) : null}
                            {card.completedRequests !== null ? (
                                <div className="mt-1 text-xs text-gray-500">
                                    {t('servicesMarketplace.serviceCard.completedRequests', {
                                        count: formatNumber(card.completedRequests),
                                    })}
                                </div>
                            ) : null}
                        </div>

                        {card.startingPrice !== null ? (
                            <div className={isRTL ? 'text-left' : 'text-right'}>
                                <div className="text-xs text-gray-500">
                                    {t('servicesMarketplace.serviceCard.startingAt')}
                                </div>
                                <div className="text-lg font-bold text-indigo-600">
                                    {formatCurrency(card.startingPrice, card.currency || undefined)}
                                    {card.pricingType === 'Hourly' ? (
                                        <span className="text-sm font-normal">
                                            {' '}
                                            {t('servicesMarketplace.common.perHour')}
                                        </span>
                                    ) : null}
                                </div>
                            </div>
                        ) : (
                            <div className="text-sm text-gray-500 italic">
                                {t('servicesMarketplace.serviceCard.contactForPricing')}
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </Link>
    )
}
