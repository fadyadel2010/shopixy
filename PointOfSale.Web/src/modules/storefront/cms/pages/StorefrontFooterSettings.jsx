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
                // The page list describes the editable draft (including its slug).
                // Navigation targets own the currently published route even when a
                // newer draft has renamed the page.
                const targets = await cmsService.getNavigationTargets()
                return targets.cmsPages
                    .filter((page) => page.state === 'published' && /^\/[a-z0-9]+(?:-[a-z0-9]+)*$/.test(page.resolvedPath || ''))
                    .map((page) => ({ slug: page.resolvedPath.slice(1), title: page.label }))
            }}
        />
    )
}

export default StorefrontFooterSettings
