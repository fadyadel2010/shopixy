/**
 * AF-15: My Links Tab — Promotion links table with copy/disable
 * 
 * NOTE: The /affiliate/promotion-links endpoint requires the AF-15 migration
 * to be applied. If the endpoint is unavailable, we gracefully show empty state.
 */
import { useState, useEffect } from 'react'
import { motion } from 'framer-motion'
import affiliateApi from '../../../../services/affiliateApi'
import { Link2, Copy, Check, ToggleLeft, ToggleRight, Loader2, ExternalLink, Sparkles } from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'

export default function LinksTab({ formatCurrency }) {
    const { t } = useLanguage()
    const [links, setLinks] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const [copiedId, setCopiedId] = useState(null)

    useEffect(() => {
        fetchLinks()
    }, [])

    const fetchLinks = async () => {
        setLoading(true)
        setError(null)
        try {
            const data = await affiliateApi.listPromotionLinks()
            setLinks(data || [])
        } catch (err) {
            // Don't let 401/500 propagate — show friendly empty state instead
            console.warn('[LinksTab] Failed to fetch promotion links:', err?.response?.status || err.message)
            setLinks([])
            if (err?.response?.status === 401) {
                // Suppress — the interceptor may have already handled it
                setError('session')
            } else {
                setError('unavailable')
            }
        } finally {
            setLoading(false)
        }
    }

    const resolveBackendPromotionUrl = (link) => {
        if (!link?.isActive || typeof link.promotionUrl !== 'string' || !link.promotionUrl.trim()) {
            return null
        }

        return link.promotionUrl
    }

    const copyLink = (link) => {
        // PASS F3: Copy only the backend-issued URL. Browser state is not referral-link authority.
        const url = resolveBackendPromotionUrl(link)
        if (!url) {
            setError('unavailable')
            return
        }

        navigator.clipboard.writeText(url).then(() => {
            setCopiedId(link.id)
            setTimeout(() => setCopiedId(null), 2000)
        })
    }

    const toggleLink = async (link) => {
        try {
            if (link.isActive) {
                await affiliateApi.deactivatePromotionLink(link.id)
            }
            fetchLinks()
        } catch { /* silently fail */ }
    }

    if (loading) {
        return (
            <div className="space-y-3">
                {[...Array(3)].map((_, i) => (
                    <div key={i} className="h-16 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] animate-pulse" />
                ))}
            </div>
        )
    }

    if (error === 'unavailable') {
        return (
            <div className="text-center py-20">
                <Sparkles className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">{t('portal.linksTab.comingSoon')}</h3>
                <p className="text-gray-500 text-sm max-w-sm mx-auto">
                    {t('portal.linksTab.comingSoonDesc')}
                </p>
            </div>
        )
    }

    if (error === 'session') {
        return (
            <div className="text-center py-20">
                <Link2 className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">{t('portal.linksTab.sessionExpired')}</h3>
                <p className="text-gray-500 text-sm">{t('portal.linksTab.sessionExpiredDesc')}</p>
            </div>
        )
    }

    if (!links.length) {
        return (
            <div className="text-center py-20">
                <Link2 className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">{t('portal.linksTab.noLinks')}</h3>
                <p className="text-gray-500 text-sm max-w-sm mx-auto">
                    {t('portal.linksTab.noLinksDesc')}
                </p>
            </div>
        )
    }

    return (
        <div className="space-y-4">
            <div className="flex items-center justify-between">
                <p className="text-sm text-gray-500">{links.length} {t('portal.linksTab.linksCount')}</p>
            </div>

            <div className="rounded-xl border border-gray-200 dark:border-white/[0.06] overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="w-full">
                        <thead>
                            <tr className="border-b border-gray-200 dark:border-white/[0.06]">
                                <th className="px-4 py-3 text-left text-[10px] font-medium text-gray-500 uppercase tracking-widest">{t('portal.linksTab.colCode')}</th>
                                <th className="px-4 py-3 text-left text-[10px] font-medium text-gray-500 uppercase tracking-widest">{t('portal.linksTab.colProduct')}</th>
                                <th className="px-4 py-3 text-center text-[10px] font-medium text-gray-500 uppercase tracking-widest">{t('portal.linksTab.colStatus')}</th>
                                <th className="px-4 py-3 text-left text-[10px] font-medium text-gray-500 uppercase tracking-widest">{t('portal.linksTab.colCreated')}</th>
                                <th className="px-4 py-3 text-right text-[10px] font-medium text-gray-500 uppercase tracking-widest">{t('portal.linksTab.colActions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-white/[0.04]">
                            {links.map((link, i) => (
                                <motion.tr
                                    key={link.id}
                                    initial={{ opacity: 0 }}
                                    animate={{ opacity: 1 }}
                                    transition={{ delay: i * 0.04 }}
                                    className="hover:bg-white/[0.02] transition-colors"
                                >
                                    <td className="px-4 py-3">
                                        <code className="text-xs font-mono text-[#6C63FF] bg-[#6C63FF]/5 px-2 py-1 rounded">
                                            {link.referralCode}
                                        </code>
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-700 dark:text-gray-300 max-w-[200px] truncate">
                                        {link.productId?.substring(0, 8)}...
                                    </td>
                                    <td className="px-4 py-3 text-center">
                                        <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-medium ${link.isActive
                                            ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                                            : 'bg-red-500/10 text-red-400 border border-red-500/20'
                                            }`}>
                                            {link.isActive ? t('portal.linksTab.statusActive') : t('portal.linksTab.statusDisabled')}
                                        </span>
                                    </td>
                                    <td className="px-4 py-3 text-xs text-gray-500">
                                        {new Date(link.createdAt).toLocaleDateString()}
                                    </td>
                                    <td className="px-4 py-3">
                                        <div className="flex items-center justify-end gap-2">
                                            <button
                                                onClick={() => copyLink(link)}
                                                disabled={!resolveBackendPromotionUrl(link)}
                                                className={`p-2 rounded-lg transition-all ${copiedId === link.id
                                                    ? 'bg-emerald-500/10 text-emerald-400'
                                                    : 'bg-white dark:bg-white/[0.03] text-gray-500 hover:text-gray-700 dark:hover:text-gray-300'
                                                    } disabled:opacity-40 disabled:cursor-not-allowed`}
                                                title={t('portal.linksTab.copyLink')}
                                            >
                                                {copiedId === link.id ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
                                            </button>
                                            {link.isActive && (
                                                <button
                                                    onClick={() => toggleLink(link)}
                                                    className="p-2 rounded-lg bg-white dark:bg-white/[0.03] text-gray-500 hover:text-red-400 transition-all"
                                                    title={t('portal.linksTab.disable')}
                                                >
                                                    <ToggleLeft className="w-3.5 h-3.5" />
                                                </button>
                                            )}
                                        </div>
                                    </td>
                                </motion.tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    )
}
