import { useEffect, useState } from 'react'
import { FooterConfigScreen } from '../../../../shared/content/FooterConfigScreen'
import { cmsService, extractCmsConflict } from '../services'

export function StorefrontFooterSettings() {
    const [configuration, setConfiguration] = useState({ sections: [], socialLinks: [] })
    const [cmsPages, setCmsPages] = useState([])
    const [isLoading, setIsLoading] = useState(true)
    const [isSaving, setIsSaving] = useState(false)
    const [error, setError] = useState('')
    const [conflict, setConflict] = useState(null)

    useEffect(() => {
        loadConfiguration()
    }, [])

    const loadConfiguration = async () => {
        setIsLoading(true)
        setError('')
        setConflict(null)

        try {
            const [footerConfiguration, pages] = await Promise.all([
                cmsService.getFooterConfiguration(),
                cmsService.listPages(),
            ])

            setConfiguration(footerConfiguration)
            setCmsPages((pages || []).map((page) => ({ slug: page.slug, title: page.title })))
        } catch (loadFailure) {
            console.error('Failed to load storefront footer settings', loadFailure)
            setError(loadFailure?.message || 'Unable to load storefront footer settings.')
        } finally {
            setIsLoading(false)
        }
    }

    const handleSave = async (payload) => {
        setIsSaving(true)

        try {
            const savedConfiguration = await cmsService.updateFooterConfiguration({
                ...payload,
                updatedAtUtc: configuration.updatedAtUtc,
            })
            setConfiguration(savedConfiguration)
            setConflict(null)
        } catch (loadFailure) {
            const conflictState = extractCmsConflict(loadFailure)
            if (conflictState) {
                setConflict(conflictState)
            }
            throw loadFailure
        } finally {
            setIsSaving(false)
        }
    }

    return (
        <FooterConfigScreen
            title="Storefront Footer and Social Links"
            description="Manage the tenant storefront footer without touching marketplace content. Tenant data stays isolated and can fall back to store-global defaults when needed."
            note="Tenant storefront configuration is resolved first. If the tenant footer or social set becomes effectively empty, the storefront falls back to the store-global configuration and then to published tenant CMS pages when appropriate."
            configuration={configuration}
            cmsPages={cmsPages}
            localized
            isLoading={isLoading}
            isSaving={isSaving}
            error={error}
            conflict={conflict}
            onRefresh={loadConfiguration}
            onSave={handleSave}
            onLoadLatest={loadConfiguration}
            onDismissConflict={() => setConflict(null)}
        />
    )
}

export default StorefrontFooterSettings
