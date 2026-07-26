import { useEffect, useMemo, useRef, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Star } from 'lucide-react'
import { useServiceDetails } from '../hooks/useServiceDetails'
import { useRequestService } from '../hooks/useRequestService'
import { marketplaceApi } from '../api/marketplaceApi'
import { useFormatting } from '../../hooks/useFormatting'
import {
  getMarketplaceAuthorityScope,
  isMarketplaceAuthorityError,
  marketplaceAuthorityQueryOptions,
  useMarketplaceAuthorityScope,
} from '../authority'
import { marketplaceKeys } from '../queryKeys'
import {
  deriveServiceDetailPresentation,
} from '../serviceDetailAuthority'
import { deriveServiceReviewHistoryPresentation } from '../ratingTimelineAuthority'

const badgeColors: Record<string, string> = {
  New: 'bg-emerald-100 text-emerald-700',
  Rising: 'bg-purple-100 text-purple-700',
  Trusted: 'bg-blue-100 text-blue-700',
  TopRated: 'bg-yellow-100 text-yellow-800',
  Standard: 'bg-gray-100 text-gray-700',
}

const trustBadgeKeys: Record<string, string> = {
  New: 'servicesMarketplace.badges.new',
  Rising: 'servicesMarketplace.badges.rising',
  Trusted: 'servicesMarketplace.badges.trusted',
  TopRated: 'servicesMarketplace.badges.topRated',
  Standard: 'servicesMarketplace.badges.standard',
}

const MAX_REQUEST_MESSAGE_LENGTH = 2000

export function ServiceDetailsPage() {
  const { t } = useTranslation()
  const { formatCurrency, formatDate, formatNumber, isRTL } = useFormatting()
  const { serviceId } = useParams<{ serviceId: string }>()
  const navigate = useNavigate()
  const authorityScope = useMarketplaceAuthorityScope()
  const {
    data: serviceSnapshot,
    isLoading,
    isFetching: serviceAuthorityRefreshing,
    isError: serviceAuthorityFailed,
    isRefetchError: serviceAuthorityRefreshFailed,
  } = useServiceDetails(serviceId!)
  const { mutateAsync: requestService, isPending } = useRequestService()
  const {
    data: reviewHistorySnapshot,
    error: reviewsError,
    isError: reviewsAuthorityFailed,
    isFetching: reviewsAuthorityRefreshing,
    isRefetchError: reviewsAuthorityRefreshFailed,
  } = useQuery({
    queryKey: [...marketplaceKeys.service(serviceId!, authorityScope), 'reviews'],
    queryFn: () => marketplaceApi.getServiceReviews(serviceId!),
    enabled: !!serviceId,
    ...marketplaceAuthorityQueryOptions,
  })
  const [requestMessage, setRequestMessage] = useState('')
  const [showRequestForm, setShowRequestForm] = useState(false)
  const submissionRef = useRef(false)
  const mountedRef = useRef(true)
  const requestContextRef = useRef({ authorityScope, serviceId })
  requestContextRef.current = { authorityScope, serviceId }
  const service = useMemo(
    () => serviceSnapshot ? deriveServiceDetailPresentation(serviceSnapshot) : null,
    [serviceSnapshot],
  )
  const reviewHistory = useMemo(
    () => service && reviewHistorySnapshot
      ? deriveServiceReviewHistoryPresentation(
          reviewHistorySnapshot,
          service.serviceId,
          service.providerProfileId,
        )
      : null,
    [reviewHistorySnapshot, service],
  )
  const serviceAuthorityValidating = isLoading || serviceAuthorityRefreshing
  const serviceVisibilityFailed = serviceAuthorityFailed || serviceAuthorityRefreshFailed
  const reviewsVisibilityFailed = reviewsAuthorityFailed
    && isMarketplaceAuthorityError(reviewsError)
  const reviewsAvailable = !reviewsAuthorityRefreshing
    && !reviewsAuthorityFailed
    && !reviewsAuthorityRefreshFailed
    && reviewHistory !== null

  useEffect(() => {
    setRequestMessage('')
    setShowRequestForm(false)
    submissionRef.current = false
  }, [authorityScope, serviceId])

  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  useEffect(() => {
    if (service?.requestEntry.canRequest !== true) {
      setRequestMessage('')
      setShowRequestForm(false)
    }
  }, [service?.requestEntry.canRequest])

  const handleRequest = async () => {
    if (!serviceId || !service?.requestEntry.canRequest || submissionRef.current) return
    const submittedScope = authorityScope
    const submittedServiceId = serviceId
    submissionRef.current = true

    try {
      const normalizedMessage = requestMessage.trim()
      if (normalizedMessage.length > MAX_REQUEST_MESSAGE_LENGTH) {
        window.alert(t('servicesMarketplace.serviceDetails.requestFailed'))
        return
      }

      await requestService({
        serviceId: submittedServiceId,
        requestMessage: normalizedMessage || undefined,
      })

      if (
        !mountedRef.current
        || getMarketplaceAuthorityScope() !== submittedScope
        || requestContextRef.current.authorityScope !== submittedScope
        || requestContextRef.current.serviceId !== submittedServiceId
      ) {
        return
      }

      navigate('/marketplace?tab=my-requests')
    } catch (error) {
      console.error('Failed to request service:', error)
      if (
        mountedRef.current
        && getMarketplaceAuthorityScope() === submittedScope
        && requestContextRef.current.authorityScope === submittedScope
        && requestContextRef.current.serviceId === submittedServiceId
      ) {
        window.alert(t('servicesMarketplace.serviceDetails.requestFailed'))
      }
    } finally {
      submissionRef.current = false
    }
  }

  if (serviceAuthorityValidating) {
    return (
      <div>
        <div className="mb-6">
          <div className="h-8 bg-gray-200 rounded w-1/3 mb-3 animate-pulse" />
          <div className="h-4 bg-gray-200 rounded w-1/4 animate-pulse" />
        </div>
        <div className="card animate-pulse">
          <div className="h-4 bg-gray-200 rounded w-full mb-3" />
          <div className="h-4 bg-gray-200 rounded w-3/4 mb-3" />
          <div className="h-4 bg-gray-200 rounded w-1/2" />
        </div>
      </div>
    )
  }

  if (!service || serviceVisibilityFailed || reviewsVisibilityFailed) {
    return (
      <div className="card text-center py-12" dir={isRTL ? 'rtl' : 'ltr'}>
        <h3 className="text-lg font-semibold text-gray-700 mb-2">{t('servicesMarketplace.serviceDetails.notFoundTitle')}</h3>
        <p className="text-gray-500">{t('servicesMarketplace.serviceDetails.notFoundDescription')}</p>
        <button onClick={() => navigate('/marketplace')} className="btn-primary mt-4">
          {t('servicesMarketplace.serviceDetails.backToMarketplace')}
        </button>
      </div>
    )
  }

  const categoryLabel = service.category
    ? t(`servicesMarketplace.categories.${service.category}`, { defaultValue: service.category })
    : ''
  const pricingTypeLabel = service.pricingType
    ? t(`servicesMarketplace.pricingType.${service.pricingType}`, { defaultValue: service.pricingType })
    : ''
  const trustBadgeLabel = service.trust?.trustBadge
    ? trustBadgeKeys[service.trust.trustBadge]
      ? t(trustBadgeKeys[service.trust.trustBadge], { defaultValue: service.trust.trustBadge })
      : service.trust.trustBadge
    : ''
  const serviceReviewCount = reviewsAvailable ? reviewHistory.totalReviews : null
  const canRequestService = service.requestEntry.canRequest

  return (
    <div dir={isRTL ? 'rtl' : 'ltr'}>
      <button onClick={() => navigate('/marketplace')} className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4 transition-colors">
        {t('servicesMarketplace.serviceDetails.backToMarketplace')}
      </button>

      <div className="flex items-start justify-between mb-6 gap-4">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">{service.title}</h2>
          <div className="flex items-center gap-3 mt-2 flex-wrap">
            {!service.isActive ? <span className="px-2.5 py-0.5 text-xs font-semibold rounded-full bg-red-100 text-red-800">{t('common.inactive')}</span> : null}
            {service.trust?.trustBadge ? <span className={`px-2.5 py-0.5 text-xs font-semibold rounded-full ${badgeColors[service.trust.trustBadge] || 'bg-gray-100 text-gray-600'}`}>{trustBadgeLabel}</span> : null}
            {categoryLabel ? <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-indigo-100 text-indigo-700">{categoryLabel}</span> : null}
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2 space-y-6">
          {service.serviceImages.length > 0 ? <div className="card p-0 overflow-hidden"><img src={service.serviceImages[0]} alt={service.title} className="w-full h-64 object-cover" onError={(event) => { event.currentTarget.style.display = 'none' }} /></div> : null}

          {service.startingPrice !== null && service.startingPrice !== undefined ? (
            <div className={`card bg-indigo-50 border-indigo-500 ${isRTL ? 'border-r-4' : 'border-l-4'}`}>
              <div className="text-sm text-gray-600 mb-1">{t('servicesMarketplace.serviceCard.startingAt')}</div>
              <div className="text-3xl font-bold text-indigo-600">
                {formatCurrency(service.startingPrice, service.currency || undefined)}
                {service.pricingType === 'Hourly' ? <span className="text-lg font-normal text-gray-600"> {t('servicesMarketplace.common.perHour')}</span> : null}
              </div>
              {pricingTypeLabel ? <div className="text-sm text-gray-600 mt-1">{t('servicesMarketplace.serviceDetails.pricingTypeLabel')}: <span className="font-medium">{pricingTypeLabel}</span></div> : null}
            </div>
          ) : null}

          <div className="card">
            <h3 className="text-lg font-semibold text-gray-900 mb-3">{t('servicesMarketplace.serviceDetails.aboutTitle')}</h3>
            <p className="text-gray-600 leading-relaxed whitespace-pre-line">{service.description}</p>
          </div>

          <div className="card">
            <h3 className="text-lg font-semibold text-gray-900 mb-3">{t('servicesMarketplace.serviceDetails.detailsTitle')}</h3>
            <div className="space-y-3">
              {categoryLabel ? <div className="flex items-start gap-3"><span className="text-gray-500 font-medium min-w-[100px]">{t('servicesMarketplace.serviceDetails.categoryLabel')}:</span><span className="text-gray-900">{categoryLabel}</span></div> : null}
              {pricingTypeLabel ? <div className="flex items-start gap-3"><span className="text-gray-500 font-medium min-w-[100px]">{t('servicesMarketplace.serviceDetails.pricingLabel')}:</span><span className="text-gray-900">{pricingTypeLabel}</span></div> : null}
              {service.startingPrice !== null && service.startingPrice !== undefined ? <div className="flex items-start gap-3"><span className="text-gray-500 font-medium min-w-[100px]">{t('servicesMarketplace.serviceDetails.startingAtLabel')}:</span><span className="text-gray-900 font-semibold">{formatCurrency(service.startingPrice, service.currency || undefined)}</span></div> : null}
              <div className="flex items-start gap-3"><span className="text-gray-500 font-medium min-w-[100px]">{t('servicesMarketplace.serviceDetails.statusLabel')}:</span><span className={`px-2 py-0.5 text-xs font-semibold rounded-full ${service.isActive ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'}`}>{service.isActive ? t('common.active') : t('common.inactive')}</span></div>
            </div>
          </div>

          {service.trust && service.trust.totalReviews > 0 ? (
            <div className="card">
              <h3 className="text-lg font-semibold text-gray-900 mb-4">{t('servicesMarketplace.serviceDetails.ratingDistributionTitle')}</h3>
              <div className="space-y-2">
                {service.trust.ratingDistribution.map(([rating, count, percentage]) => (
                  <div key={rating} className="flex items-center gap-3">
                    <span className="text-sm font-medium text-gray-600 w-12">{rating}</span>
                    <div className="flex-1 h-3 bg-gray-100 rounded-full overflow-hidden"><div className="h-full bg-indigo-500 rounded-full transition-all duration-500" style={{ width: `${percentage}%` }} /></div>
                    <span className="text-sm text-gray-500 w-8 text-right">{formatNumber(Number(count))}</span>
                  </div>
                ))}
              </div>
            </div>
          ) : null}

          <div className="card">
            <h3 className="text-lg font-semibold text-gray-900 mb-4">
              {serviceReviewCount === null
                ? t('servicesMarketplace.serviceDetails.reviewsUnavailableTitle')
                : t('servicesMarketplace.serviceDetails.reviewsTitle', {
                    count: formatNumber(serviceReviewCount),
                  })}
            </h3>
            {reviewsAuthorityRefreshing ? (
              <div className="text-center py-8 text-gray-500">
                <p>{t('servicesMarketplace.discover.loading')}</p>
              </div>
            ) : !reviewsAvailable ? (
              <div className="text-center py-8 text-gray-500">
                <p>{t('servicesMarketplace.serviceDetails.reviewsUnavailable', { defaultValue: 'Reviews are temporarily unavailable.' })}</p>
              </div>
            ) : reviewHistory.items.length > 0 ? (
              <div className="space-y-4">
                {reviewHistory.items.map((review) => (
                  <div key={review.reviewId} className="border-b border-gray-100 last:border-0 pb-4 last:pb-0">
                        <div className="flex items-start gap-3">
                      <div className="w-10 h-10 rounded-full bg-gradient-to-br from-purple-500 to-indigo-600 flex items-center justify-center text-white font-semibold flex-shrink-0">{review.buyerInitial}</div>
                      <div className="flex-1">
                        <div className="flex items-center justify-between mb-1 gap-4"><span className="font-medium text-gray-900">{review.buyerName}</span><span className="text-xs text-gray-500">{formatDate(review.createdAt)}</span></div>
                        <div className={`flex items-center gap-1 mb-2 ${isRTL ? 'flex-row-reverse justify-end' : ''}`}>
                          {[...Array(5)].map((_, index: number) => (
                            <span key={index} className={`inline-flex text-sm ${index < review.rating ? 'text-yellow-500' : 'text-gray-300'}`}>
                              <Star className="h-4 w-4" fill={index < review.rating ? 'currentColor' : 'none'} aria-hidden="true" />
                            </span>
                          ))}
                          <span className="text-sm font-semibold text-gray-700 ml-1">{formatNumber(review.rating, { minimumFractionDigits: 1, maximumFractionDigits: 1 })}</span>
                        </div>
                        {review.comment ? <p className="text-sm text-gray-600 leading-relaxed">{review.comment}</p> : null}
                        {review.marketingEvidence ? (
                          <div className="mt-3 p-3 bg-indigo-50 rounded-lg border border-indigo-200">
                            <div className="flex items-center gap-2 mb-2 flex-wrap">
                              <span className="text-sm font-semibold text-indigo-800">{t('servicesMarketplace.serviceDetails.campaignEvidenceTitle')}</span>
                              <span className={`px-2 py-0.5 text-xs font-semibold rounded-full ${review.marketingEvidence.evidenceType === 'PosVerified' ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-700'}`}>{review.marketingEvidence.evidenceType === 'PosVerified' ? t('servicesMarketplace.serviceDetails.posVerified') : t('servicesMarketplace.serviceDetails.selfReported')}</span>
                            </div>
                            <div className="space-y-1.5 text-sm">
                              <div className="flex items-center gap-2"><span className="text-gray-500 min-w-[80px]">{t('servicesMarketplace.serviceDetails.campaignLabel')}:</span><span className="font-medium text-gray-900">{review.marketingEvidence.campaignName}</span></div>
                              <div className="flex items-center gap-2"><span className="text-gray-500 min-w-[80px]">{t('servicesMarketplace.serviceDetails.platformLabel')}:</span><span className="px-2 py-0.5 text-xs font-medium bg-white rounded border border-indigo-200 text-indigo-700">{t(`servicesMarketplace.platforms.${review.marketingEvidence.platform}`, { defaultValue: review.marketingEvidence.platform })}</span></div>
                              <div className="flex items-center gap-2"><span className="text-gray-500 min-w-[80px]">{t('servicesMarketplace.serviceDetails.periodLabel')}:</span><span className="text-gray-900">{formatDate(review.marketingEvidence.startDate)} - {formatDate(review.marketingEvidence.endDate)}</span></div>
                              {review.marketingEvidence.ordersCount != null ? <div className="flex items-center gap-4 mt-2 pt-2 border-t border-indigo-200"><div><span className="text-xs text-gray-500">{t('servicesMarketplace.serviceDetails.ordersLabel')}</span><div className="text-lg font-bold text-indigo-700">{formatNumber(review.marketingEvidence.ordersCount)}</div></div></div> : null}
                            </div>
                          </div>
                        ) : null}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            ) : <div className="text-center py-8 text-gray-500"><p>{t('servicesMarketplace.serviceDetails.noReviews')}</p></div>}
          </div>
        </div>

        <div className="space-y-6">
          <div className="card">
            <h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider mb-4">{t('servicesMarketplace.serviceDetails.providerTitle')}</h3>
            <div className="flex items-center gap-3 mb-4">
              {service.providerProfile.profileImageUrl ? <img src={service.providerProfile.profileImageUrl} alt={service.providerDisplayName} className="w-12 h-12 rounded-full object-cover shadow-sm" onError={(event) => { event.currentTarget.style.display = 'none' }} /> : <div className="w-12 h-12 bg-gradient-to-br from-indigo-500 to-indigo-600 rounded-full flex items-center justify-center text-white font-semibold shadow-sm">{service.providerDisplayName[0]?.toUpperCase()}</div>}
              <div><div className="text-lg font-medium text-gray-900">{service.providerDisplayName}</div>{service.providerProfile.location ? <div className="text-sm text-gray-500">{service.providerProfile.location}</div> : null}</div>
            </div>
            {service.providerProfile.bio ? <div className="mb-4"><p className="text-sm text-gray-600 leading-relaxed">{service.providerProfile.bio}</p></div> : null}
            {service.providerProfile.skills.length > 0 ? <div className="mb-4"><div className="text-xs font-medium text-gray-500 uppercase tracking-wider mb-2">{t('servicesMarketplace.serviceDetails.skillsTitle')}</div><div className="flex flex-wrap gap-2">{service.providerProfile.skills.map((skill, index) => <span key={`${skill}-${index}`} className="px-2 py-1 text-xs font-medium bg-indigo-50 text-indigo-700 rounded">{skill}</span>)}</div></div> : null}
            {service.providerProfile.portfolioLinks.length > 0 ? <div><div className="text-xs font-medium text-gray-500 uppercase tracking-wider mb-2">{t('servicesMarketplace.serviceDetails.portfolioTitle')}</div><div className="space-y-2">{service.providerProfile.portfolioLinks.map((link, index) => <a key={`${link}-${index}`} href={link} target="_blank" rel="noopener noreferrer" className="block text-sm text-indigo-600 hover:text-indigo-700 hover:underline truncate">{link}</a>)}</div></div> : null}
          </div>

          {service.trust ? <div className="card"><h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider mb-3">{t('servicesMarketplace.serviceDetails.trustTitle')}</h3><div className="space-y-3"><div className="flex justify-between items-center"><span className="text-sm text-gray-600">{t('servicesMarketplace.serviceDetails.averageRatingLabel')}</span><span className="text-lg font-bold text-gray-900">{formatNumber(service.trust.averageRating, { minimumFractionDigits: 1, maximumFractionDigits: 1 })}</span></div><div className="flex justify-between items-center"><span className="text-sm text-gray-600">{t('servicesMarketplace.serviceDetails.totalReviewsLabel')}</span><span className="text-lg font-bold text-gray-900">{formatNumber(service.trust.totalReviews)}</span></div></div></div> : null}

          {canRequestService ? (
            <div className={`card border-indigo-500 ${isRTL ? 'border-r-4' : 'border-l-4'}`}>
              {!showRequestForm ? (
                <button onClick={() => setShowRequestForm(true)} className="btn-primary w-full text-center">
                  {t('servicesMarketplace.serviceDetails.requestService')}
                </button>
              ) : (
                <div className="space-y-4">
                  <h3 className="text-lg font-semibold text-gray-900">{t('servicesMarketplace.serviceDetails.requestTitle')}</h3>
                  <div>
                    <label className="label">{t('servicesMarketplace.serviceDetails.requestMessageLabel')}</label>
                    <textarea value={requestMessage} onChange={(event) => setRequestMessage(event.target.value)} maxLength={MAX_REQUEST_MESSAGE_LENGTH} rows={4} className="input" placeholder={t('servicesMarketplace.serviceDetails.requestMessagePlaceholder')} />
                  </div>
                  <div className="flex gap-3">
                    <button onClick={handleRequest} disabled={isPending} className="btn-primary flex-1 disabled:opacity-50">{isPending ? t('servicesMarketplace.serviceDetails.submitting') : t('servicesMarketplace.serviceDetails.submitRequest')}</button>
                    <button onClick={() => setShowRequestForm(false)} disabled={isPending} className="btn-secondary disabled:opacity-50">{t('common.cancel')}</button>
                  </div>
                </div>
              )}
            </div>
          ) : service.requestEntry.reason === 'ActiveRequestExists' ? (
            <div className={`card border-blue-500 ${isRTL ? 'border-r-4' : 'border-l-4'}`}>
              <p className="text-sm text-gray-600 mb-3">
                {t('servicesMarketplace.serviceDetails.activeRequestExists', {
                  defaultValue: 'You already have an active request for this service.',
                })}
              </p>
              <button onClick={() => navigate('/marketplace?tab=my-requests')} className="btn-secondary w-full">
                {t('servicesMarketplace.serviceDetails.viewRequests', {
                  defaultValue: 'View my requests',
                })}
              </button>
            </div>
          ) : null}
        </div>
      </div>
    </div>
  )
}
