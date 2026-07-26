import { useEffect, useState } from 'react'
import { FooterConfigScreen } from '../../shared/content/FooterConfigScreen'
import { storefrontFooterService } from './storefrontFooterService'

export function StorefrontFooterDefaultsPage() {
    const [configuration, setConfiguration] = useState({ sections: [], socialLinks: [] })
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
            const footerConfiguration = await storefrontFooterService.getConfiguration()
            setConfiguration(footerConfiguration)
        } catch (loadFailure) {
            console.error('Failed to load storefront footer defaults', loadFailure)
            setError(loadFailure?.message || 'Unable to load storefront footer defaults.')
        } finally {
            setIsLoading(false)
        }
    }

    const handleSave = async (payload) => {
        setIsSaving(true)

        try {
            const savedConfiguration = await storefrontFooterService.updateConfiguration(payload)
            setConfiguration(savedConfiguration)
        } finally {
            setIsSaving(false)
        }
    }

    return (
        <FooterConfigScreen
            title="Storefront Footer Defaults"
            description="Manage the global fallback footer and social links used by tenant storefronts when a tenant has no effective footer configuration of its own. This system stays completely separate from marketplace pages and marketplace footer data."
            note="Storefront fallback defaults are tenant-aware, not marketplace-aware. CMS links here should use tenant page slugs such as `about` or `privacy`; each tenant resolves those links only against its own published CMS pages, and unresolved links stay hidden."
            configuration={configuration}
            cmsPages={[]}
            localized
            allowCmsSlugEntry
            isLoading={isLoading}
            isSaving={isSaving}
            error={error}
            onRefresh={loadConfiguration}
            onSave={handleSave}
        />
    )
}

export default StorefrontFooterDefaultsPage
