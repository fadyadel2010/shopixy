import { useEffect, useState } from 'react'
import { FooterConfigScreen } from '../../shared/content/FooterConfigScreen'
import { marketplaceCmsService } from './marketplaceCmsService'

export function MarketplaceFooterSettingsPage() {
    const [configuration, setConfiguration] = useState({ sections: [], socialLinks: [] })
    const [cmsPages, setCmsPages] = useState([])
    const [isLoading, setIsLoading] = useState(true)
    const [isSaving, setIsSaving] = useState(false)
    const [error, setError] = useState('')

    useEffect(() => {
        loadConfiguration()
    }, [])

    const loadConfiguration = async () => {
        setIsLoading(true)
        setError('')

        try {
            const [footerConfiguration, pages] = await Promise.all([
                marketplaceCmsService.getFooterConfiguration(),
                marketplaceCmsService.listPages(),
            ])

            setConfiguration(footerConfiguration)
            setCmsPages((pages || []).map((page) => ({ slug: page.slug, title: page.title })))
        } catch (loadFailure) {
            console.error('Failed to load marketplace footer settings', loadFailure)
            setError(loadFailure?.message || 'Unable to load marketplace footer settings.')
        } finally {
            setIsLoading(false)
        }
    }

    const handleSave = async (payload) => {
        setIsSaving(true)

        try {
            const savedConfiguration = await marketplaceCmsService.updateFooterConfiguration(payload)
            setConfiguration(savedConfiguration)
        } finally {
            setIsSaving(false)
        }
    }

    return (
        <FooterConfigScreen
            title="Marketplace Footer and Social Links"
            description="Manage the global Shopixy marketplace footer separately from tenant storefront content. These links render only on the marketplace domain."
            note="Marketplace footer rendering never reads tenant CMS or tenant social data. Configured marketplace sections are used first, and the public marketplace falls back only to published marketplace CMS pages when needed."
            configuration={configuration}
            cmsPages={cmsPages}
            localized
            isLoading={isLoading}
            isSaving={isSaving}
            error={error}
            onRefresh={loadConfiguration}
            onSave={handleSave}
            onTranslateContent={marketplaceCmsService.translateContent}
        />
    )
}

export default MarketplaceFooterSettingsPage
