import { Link } from 'react-router-dom'
import { prefixMarketplacePath } from './marketplaceLanguage'
import { resolveSafeNavigationTarget } from '../security/safeNavigationTarget'

export function MarketplacePublicFooter({ footer, language, followLabel }) {
    if (!footer?.sections?.length && !footer?.socialLinks?.length) {
        return null
    }

    return (
        <footer className="rounded-[36px] border border-amber-100 bg-white px-8 py-10 shadow-[0_30px_90px_-50px_rgba(120,53,15,0.35)]">
            <div className="grid gap-8 md:grid-cols-2">
                <div className="grid gap-8 sm:grid-cols-2">
                    {(footer.sections || []).map((section) => (
                        <div key={`${section.title}-${section.sortOrder}`}>
                            <h2 className="text-sm font-semibold uppercase tracking-[0.22em] text-amber-700">{section.title}</h2>
                            <div className="mt-4 space-y-3">
                                {(section.links || []).map((link) => (
                                    <MarketplaceFooterLink
                                        key={`${link.label}-${link.url}`}
                                        link={link}
                                        language={language}
                                    />
                                ))}
                            </div>
                        </div>
                    ))}
                </div>

                {(footer.socialLinks || []).length > 0 ? (
                    <div>
                        <h2 className="text-sm font-semibold uppercase tracking-[0.22em] text-amber-700">{followLabel}</h2>
                        <div className="mt-4 flex flex-wrap gap-3">
                            {footer.socialLinks.map((link) => (
                                <a
                                    key={`${link.platform}-${link.url}`}
                                    href={link.url}
                                    target="_blank"
                                    rel="noreferrer"
                                    className="inline-flex rounded-full border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 transition hover:border-slate-300 hover:text-slate-950"
                                >
                                    {link.platform}
                                </a>
                            ))}
                        </div>
                    </div>
                ) : null}
            </div>
        </footer>
    )
}

function MarketplaceFooterLink({ link, language }) {
    const href = resolveSafeNavigationTarget(prefixMarketplacePath(language, link.url), '#')
    const className = 'block text-sm text-slate-600 transition hover:text-slate-950'

    if (shouldUseRouterLink(href, link.openInNewTab)) {
        return (
            <Link to={href} className={className}>
                {link.label}
            </Link>
        )
    }

    return (
        <a
            href={href}
            target={link.openInNewTab ? '_blank' : undefined}
            rel={link.openInNewTab ? 'noreferrer' : undefined}
            className={className}
        >
            {link.label}
        </a>
    )
}

function shouldUseRouterLink(url, openInNewTab) {
    if (!url || openInNewTab) {
        return false
    }

    if (url.startsWith('#')) {
        return false
    }

    return !/^(https?:\/\/|mailto:|tel:)/i.test(url)
}

export default MarketplacePublicFooter
