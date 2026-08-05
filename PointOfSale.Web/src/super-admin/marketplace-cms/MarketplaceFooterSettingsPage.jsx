import { FooterConfigScreen } from '../../shared/content/FooterConfigScreen'
import { marketplaceCmsService } from './marketplaceCmsService'

export function MarketplaceFooterSettingsPage() {
    return (
        <FooterConfigScreen
            scope="Marketplace"
            title="Marketplace Footer and Social Links"
            description="Manage the global Shopixy marketplace footer separately from tenant storefront content. These links render only on the marketplace domain."
            note="Saving publishes the marketplace footer immediately. Only published Marketplace CMS pages are valid CMS link targets."
            localized
            loadCmsPages={async () => {
                const pages = await marketplaceCmsService.listPages()
                return (pages || [])
                    .filter((page) => page.publishedVersion)
                    .map((page) => ({ slug: page.slug, title: page.title }))
            }}
            onTranslateContent={marketplaceCmsService.translateContent}
        />
    )
}

export default MarketplaceFooterSettingsPage
