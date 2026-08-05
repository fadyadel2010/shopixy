import { FooterConfigScreen } from '../../../../shared/content/FooterConfigScreen'
import { cmsService } from '../services'

export function StorefrontFooterSettings() {
    return (
        <FooterConfigScreen
            scope="Tenant"
            title="Footer Content"
            description="Manage the tenant storefront footer without touching marketplace content. Tenant data stays isolated and can fall back to store-global defaults when needed."
            note="Saving publishes this tenant footer immediately. Published Pages are the only valid page-link targets."
            localized
            loadCmsPages={async () => {
                const pages = await cmsService.listPages()
                return (pages || []).map((page) => ({ slug: page.slug, title: page.title }))
            }}
        />
    )
}

export default StorefrontFooterSettings
