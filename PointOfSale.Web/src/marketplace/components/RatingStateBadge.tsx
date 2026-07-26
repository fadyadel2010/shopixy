import { useTranslation } from 'react-i18next'
import type { ServiceRequestRatingStatePresentation } from '../ratingStateAuthority'

export function RatingStateBadge({
  ratingState,
}: {
  ratingState: ServiceRequestRatingStatePresentation
}) {
  const { t } = useTranslation()
  const rated = ratingState.state === 'rated'

  return (
    <span
      className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold ${
        rated
          ? 'bg-green-100 text-green-800'
          : 'bg-amber-100 text-amber-800'
      }`}
    >
      {t(
        rated
          ? 'servicesMarketplace.ratingState.rated'
          : 'servicesMarketplace.ratingState.unrated',
      )}
    </span>
  )
}
