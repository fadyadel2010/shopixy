import { FooterConfigScreen } from '../../shared/content/FooterConfigScreen'

export function StorefrontFooterDefaultsPage() {
    return (
        <FooterConfigScreen
            scope="Global"
            title="Storefront Footer Defaults"
            description="Manage the global fallback footer and social links used by tenant storefronts when a tenant has no effective footer configuration of its own."
            note="Saving publishes these fallback defaults immediately. CMS slugs are resolved independently against each tenant's published pages at runtime."
            localized
            allowCmsSlugEntry
        />
    )
}

export default StorefrontFooterDefaultsPage
