import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMarketplaceProfileQuery } from '../../hooks/useMarketplaceProfileQuery'
import { useMarketplaceProfileEditor, useMarketplaceProfileStats } from '../../hooks/useMarketplaceProfileEditor'
import { ProfileFormModal } from '../../components/ProfileFormModal'
import { useFormatting } from '../../../hooks/useFormatting'
import {
    useMarketplaceAuthorityScope,
} from '../../authority'
import { deriveProviderAnalyticsDashboardPresentation } from '../../dashboardTrendAuthority'
import { deriveProviderProfilePresentation } from '../../providerProfileAuthority'
import type { MarketplaceProfileInput } from '../../types'

const reputationBadgeKeys: Record<string, string> = {
    New: 'servicesMarketplace.badges.new',
    Rising: 'servicesMarketplace.badges.rising',
    Trusted: 'servicesMarketplace.badges.trusted',
    TopRated: 'servicesMarketplace.badges.topRated',
    Standard: 'servicesMarketplace.badges.standard',
}

export function ProfileTab() {
    const { t } = useTranslation()
    const { formatNumber, isRTL } = useFormatting()
    const authorityScope = useMarketplaceAuthorityScope()
    const [showCreateModal, setShowCreateModal] = useState(false)
    const [showEditModal, setShowEditModal] = useState(false)

    const {
        data: profileData,
        isLoading,
        isFetching: profileAuthorityRefreshing,
        isError: profileAuthorityFailed,
        isRefetchError: profileAuthorityRefreshFailed,
    } = useMarketplaceProfileQuery()
    const profile = !profileAuthorityRefreshing
        && !profileAuthorityFailed
        && !profileAuthorityRefreshFailed
        ? deriveProviderProfilePresentation(profileData)
        : undefined

    const {
        data: statsData,
        isLoading: statsAuthorityLoading,
        isFetching: statsAuthorityRefreshing,
        isError: statsAuthorityFailed,
        isRefetchError: statsAuthorityRefreshFailed,
    } = useMarketplaceProfileStats(!profileAuthorityFailed && profile?.hasMarketplaceProfile === true && profile.isActive === true)
    const dashboard = !statsAuthorityLoading
        && !statsAuthorityRefreshing
        && !statsAuthorityFailed
        && !statsAuthorityRefreshFailed
        ? deriveProviderAnalyticsDashboardPresentation(statsData, {
            authorityScope,
            providerProfileId: profile?.profileId,
        })
        : null
    const stats = dashboard?.cards ?? null

    const { createProfileMutation, updateProfileMutation } = useMarketplaceProfileEditor(() => setShowCreateModal(false), () => setShowEditModal(false))

    const handleSubmitCreate = async (data: MarketplaceProfileInput) => {
        await createProfileMutation.mutateAsync(data)
    }

    const handleSubmitEdit = async (data: MarketplaceProfileInput) => {
        await updateProfileMutation.mutateAsync(data)
    }

    if (isLoading || profileAuthorityRefreshing) {
        return (
            <div className="flex justify-center py-12">
                <div className="text-gray-500">{t('servicesMarketplace.profile.loading')}</div>
            </div>
        )
    }

    if (
        profileAuthorityFailed
        || profileAuthorityRefreshFailed
        || profile === null
    ) {
        return (
            <div className="card text-center py-12" dir={isRTL ? 'rtl' : 'ltr'}>
                <h2 className="text-xl font-bold text-gray-900 mb-2">
                    {t('servicesMarketplace.profile.unavailableTitle', { defaultValue: 'Marketplace profile unavailable' })}
                </h2>
                <p className="text-gray-500">
                    {t('servicesMarketplace.profile.unavailableDescription', {
                        defaultValue: 'Current backend authority does not allow this profile to be used.',
                    })}
                </p>
            </div>
        )
    }

    if (!profile?.hasMarketplaceProfile) {
        return (
            <>
                <div className="card text-center py-12" dir={isRTL ? 'rtl' : 'ltr'}>
                    <h2 className="text-2xl font-bold text-gray-900 mb-2">
                        {t('servicesMarketplace.profile.emptyTitle')}
                    </h2>
                    <p className="text-gray-500 mb-6">
                        {t('servicesMarketplace.profile.emptyDescription')}
                    </p>
                    <button className="btn-primary" onClick={() => setShowCreateModal(true)}>
                        {t('servicesMarketplace.profile.create')}
                    </button>
                </div>

                {showCreateModal ? (
                    <ProfileFormModal
                        isRTL={isRTL}
                        onSubmit={handleSubmitCreate}
                        onClose={() => setShowCreateModal(false)}
                    />
                ) : null}
            </>
        )
    }

    const skills = profile.skills
    const portfolioLinks = profile.portfolioLinks

    return (
        <div className="space-y-6" dir={isRTL ? 'rtl' : 'ltr'}>
            {profile.coverImageUrl ? (
                <div className="relative h-48 rounded-lg overflow-hidden">
                    <img
                        src={profile.coverImageUrl}
                        alt={t('servicesMarketplace.profile.coverAlt')}
                        className="w-full h-full object-cover"
                    />
                </div>
            ) : null}

            <div className="card">
                <div className="flex items-start gap-6">
                    <div className="relative">
                        {profile.profileImageUrl ? (
                            <img
                                src={profile.profileImageUrl}
                                alt={profile.displayName || ''}
                                className="w-24 h-24 rounded-full object-cover border-4 border-white shadow-lg"
                            />
                        ) : (
                            <div className="w-24 h-24 rounded-full bg-gradient-to-br from-indigo-500 to-purple-600 flex items-center justify-center text-white text-3xl font-bold border-4 border-white shadow-lg">
                                {(profile.displayName || 'U').charAt(0).toUpperCase()}
                            </div>
                        )}
                    </div>

                    <div className="flex-1">
                        <div className="flex items-start justify-between gap-4">
                            <div>
                                <h2 className="text-2xl font-bold text-gray-900">{profile.displayName}</h2>
                                {profile.location ? (
                                    <div className="flex items-center gap-1 text-gray-600 mt-1">
                                        <span className="text-sm">{profile.location}</span>
                                    </div>
                                ) : null}
                            </div>
                            <button className="btn-secondary" onClick={() => setShowEditModal(true)}>
                                {t('servicesMarketplace.profile.edit')}
                            </button>
                        </div>

                        {profile.bio ? (
                            <p className="text-gray-700 mt-4 leading-relaxed">{profile.bio}</p>
                        ) : null}
                    </div>
                </div>
            </div>

            {skills.length > 0 ? (
                <div className="card">
                    <h3 className="text-lg font-semibold text-gray-900 mb-4">
                        {t('servicesMarketplace.profile.skillsTitle')}
                    </h3>
                    <div className="flex flex-wrap gap-2">
                            {skills.map((skill: string, index: number) => (
                            <span
                                key={`${skill}-${index}`}
                                className="px-4 py-2 bg-indigo-50 text-indigo-700 rounded-full text-sm font-medium border border-indigo-200"
                            >
                                {skill}
                            </span>
                        ))}
                    </div>
                </div>
            ) : null}

            {portfolioLinks.length > 0 ? (
                <div className="card">
                    <h3 className="text-lg font-semibold text-gray-900 mb-4">
                        {t('servicesMarketplace.profile.portfolioTitle')}
                    </h3>
                    <div className="space-y-2">
                        {portfolioLinks.map((link: string, index: number) => (
                            <a
                                key={`${link}-${index}`}
                                href={link}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="flex items-center gap-2 text-indigo-600 hover:text-indigo-700 hover:underline"
                            >
                                <span className="text-sm">{link}</span>
                            </a>
                        ))}
                    </div>
                </div>
            ) : null}

            {stats ? (
                <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                    <div className="card bg-gradient-to-br from-blue-50 to-blue-100 border-blue-200">
                        <div className="text-sm text-blue-700 font-medium">
                            {t('servicesMarketplace.profile.stats.servicesOffered')}
                        </div>
                        <div className="text-3xl font-bold text-blue-900 mt-2">
                            {formatNumber(stats.servicesOffered)}
                        </div>
                    </div>
                    <div className="card bg-gradient-to-br from-green-50 to-green-100 border-green-200">
                        <div className="text-sm text-green-700 font-medium">
                            {t('servicesMarketplace.profile.stats.completedRequests')}
                        </div>
                        <div className="text-3xl font-bold text-green-900 mt-2">
                            {formatNumber(stats.completedRequests)}
                        </div>
                    </div>
                    <div className="card bg-gradient-to-br from-purple-50 to-purple-100 border-purple-200">
                        <div className="text-sm text-purple-700 font-medium">
                            {t('servicesMarketplace.profile.stats.averageRating')}
                        </div>
                        <div className="text-3xl font-bold text-purple-900 mt-2">
                            {formatNumber(stats.reputation.averageRating, {
                                minimumFractionDigits: 1,
                                maximumFractionDigits: 1,
                            })}
                            <div className="mt-1 text-sm font-medium text-purple-700">
                                {formatNumber(stats.reputation.totalReviews)} {t('servicesMarketplace.profile.stats.reviews')}
                            </div>
                        </div>
                    </div>
                    <div className="card bg-gradient-to-br from-amber-50 to-amber-100 border-amber-200">
                        <div className="text-sm text-amber-700 font-medium">
                            {t('servicesMarketplace.profile.stats.reputation')}
                        </div>
                        <div className="text-2xl font-bold text-amber-900 mt-2">
                            {t(reputationBadgeKeys[stats.reputation.trustBadge])}
                        </div>
                    </div>
                </div>
            ) : null}

            {showCreateModal ? (
                <ProfileFormModal
                    isRTL={isRTL}
                    onSubmit={handleSubmitCreate}
                    onClose={() => setShowCreateModal(false)}
                />
            ) : null}

            {showEditModal ? (
                <ProfileFormModal
                    isRTL={isRTL}
                    initialData={profileData}
                    onSubmit={handleSubmitEdit}
                    onClose={() => setShowEditModal(false)}
                />
            ) : null}
        </div>
    )
}
