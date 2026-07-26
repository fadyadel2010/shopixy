/**
 * AF-15: Premium Affiliate Landing Page
 * 
 * Dark-first design with glassmorphism, animated gradients, and
 * conversion-optimized layout. Inspired by Stripe, Linear, and Vercel.
 */
import { useState, useEffect } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { motion } from 'framer-motion'
import axios from 'axios'
import { affiliateAuthApi } from '../../../services/affiliateAuthApi'
import {
    TrendingUp, Zap, Shield, DollarSign, ArrowRight,
    Link2, BarChart3, Users, Star, ChevronRight,
    Search, Package, Sparkles, ExternalLink
} from 'lucide-react'
import { useLanguage } from '../i18n/LanguageContext'
import LanguageSwitcher from '../components/LanguageSwitcher'
import ThemeSwitcher from '../components/ThemeSwitcher'
import CategoriesSection from './landing/CategoriesSection'

// ═══════════════════════════════════════════════════════════════════
// ANIMATION VARIANTS
// ═══════════════════════════════════════════════════════════════════

const fadeUp = {
    hidden: { opacity: 0, y: 30 },
    visible: (i = 0) => ({
        opacity: 1, y: 0,
        transition: { delay: i * 0.1, duration: 0.6, ease: [0.25, 0.46, 0.45, 0.94] }
    })
}

const scaleIn = {
    hidden: { opacity: 0, scale: 0.9 },
    visible: { opacity: 1, scale: 1, transition: { duration: 0.5 } }
}

const stagger = {
    visible: { transition: { staggerChildren: 0.08 } }
}

const resolveProductCurrency = (product) => {
    const currency = typeof product?.currency === 'string' ? product.currency.trim().toUpperCase() : ''
    return currency || null
}

// ═══════════════════════════════════════════════════════════════════
// ANIMATED COUNTER
// ═══════════════════════════════════════════════════════════════════

function AnimatedCounter({ target, prefix = '', suffix = '', duration = 2000 }) {
    const [count, setCount] = useState(0)

    useEffect(() => {
        let start = 0
        const end = target
        const range = end - start
        const startTime = performance.now()

        const step = (timestamp) => {
            const progress = Math.min((timestamp - startTime) / duration, 1)
            const eased = 1 - Math.pow(1 - progress, 3)
            setCount(Math.floor(start + range * eased))
            if (progress < 1) requestAnimationFrame(step)
        }
        requestAnimationFrame(step)
    }, [target, duration])

    return <span>{prefix}{count.toLocaleString()}{suffix}</span>
}

// ═══════════════════════════════════════════════════════════════════
// HERO SECTION
// ═══════════════════════════════════════════════════════════════════

function HeroSection({ onSearch }) {
    const { t } = useLanguage()
    const navigate = useNavigate()
    const [query, setQuery] = useState('')

    return (
        <section className="relative min-h-[90vh] flex items-center justify-center overflow-hidden">
            {/* Animated background */}
            <div className="absolute inset-0">
                <div className="absolute top-0 left-1/2 -translate-x-1/2 w-[800px] h-[800px] rounded-full bg-[radial-gradient(circle,rgba(108,99,255,0.3)_0%,transparent_65%)] dark:bg-[radial-gradient(circle,rgba(108,99,255,0.15)_0%,transparent_65%)] blur-3xl transition-colors duration-500" />
                <div className="absolute top-1/4 right-0 w-[500px] h-[500px] rounded-full bg-[radial-gradient(circle,rgba(139,92,246,0.25)_0%,transparent_65%)] dark:bg-[radial-gradient(circle,rgba(139,92,246,0.1)_0%,transparent_65%)] blur-2xl transition-colors duration-500" />
                <div className="absolute bottom-0 left-0 w-[600px] h-[600px] rounded-full bg-[radial-gradient(circle,rgba(59,130,246,0.2)_0%,transparent_65%)] dark:bg-[radial-gradient(circle,rgba(59,130,246,0.08)_0%,transparent_65%)] blur-2xl transition-colors duration-500" />
                {/* Grid pattern */}
                <div className="absolute inset-0 bg-[linear-gradient(rgba(0,0,0,0.03)_1px,transparent_1px),linear-gradient(90deg,rgba(0,0,0,0.03)_1px,transparent_1px)] dark:bg-[linear-gradient(rgba(255,255,255,0.02)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,0.02)_1px,transparent_1px)] bg-[size:60px_60px] transition-colors duration-500" />
            </div>

            <div className="relative z-10 max-w-5xl mx-auto px-6 text-center">
                {/* Badge */}
                <motion.div
                    variants={fadeUp}
                    initial="hidden"
                    animate="visible"
                    custom={0}
                    className="inline-flex items-center gap-2 px-4 py-2 rounded-full bg-white dark:bg-white/5 border border-gray-200 shadow-sm dark:shadow-none dark:border-white/10 backdrop-blur-sm mb-8"
                >
                    <Sparkles className="w-4 h-4 text-[#6C63FF]" />
                    <span className="text-sm text-gray-700 dark:text-gray-300">{t('landing.badge')}</span>
                    <ChevronRight className="w-3 h-3 text-gray-500" />
                </motion.div>

                {/* Headline */}
                <motion.h1
                    variants={fadeUp}
                    initial="hidden"
                    animate="visible"
                    custom={1}
                    className="text-5xl md:text-7xl font-extrabold leading-[1.1] tracking-tight text-gray-900 dark:text-white mb-6"
                >
                    {t('landing.headline1')}
                    <br />
                    <span className="bg-gradient-to-r from-[#6C63FF] via-[#A855F7] to-[#EC4899] bg-clip-text text-transparent">
                        {t('landing.headline2')}
                    </span>
                </motion.h1>

                {/* Subtext */}
                <motion.p
                    variants={fadeUp}
                    initial="hidden"
                    animate="visible"
                    custom={2}
                    className="text-lg md:text-xl text-gray-600 dark:text-gray-400 max-w-2xl mx-auto mb-10 leading-relaxed"
                >
                    {t('landing.subtext')}
                </motion.p>

                {/* CTA Buttons */}
                <motion.div
                    variants={fadeUp}
                    initial="hidden"
                    animate="visible"
                    custom={3}
                    className="flex flex-col sm:flex-row items-center justify-center gap-4 mb-16"
                >
                    <button
                        onClick={() => navigate('/affiliate/register')}
                        className="group flex items-center gap-2 px-8 py-4 rounded-xl bg-gradient-to-r from-[#6C63FF] to-[#8B5CF6] text-white font-semibold text-lg shadow-lg shadow-[#6C63FF]/25 hover:shadow-[#6C63FF]/40 hover:scale-[1.02] transition-all duration-300"
                    >
                        {t('landing.ctaJoin')}
                        <ArrowRight className="w-5 h-5 group-hover:translate-x-1 transition-transform" />
                    </button>
                    <Link
                        to="/affiliate/login"
                        className="flex items-center gap-2 px-8 py-4 rounded-xl border border-gray-300 dark:border-white/10 bg-white/50 dark:bg-transparent text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-white hover:border-gray-400 dark:hover:border-white/20 hover:bg-gray-50 dark:hover:bg-white/5 font-medium text-lg transition-all duration-300 shadow-sm dark:shadow-none"
                    >
                        {t('landing.ctaLogin')}
                        <ExternalLink className="w-4 h-4" />
                    </Link>
                </motion.div>

                {/* Stats */}
                <motion.div
                    variants={stagger}
                    initial="hidden"
                    animate="visible"
                    className="grid grid-cols-3 gap-8 max-w-xl mx-auto"
                >
                    {[
                        { value: 2500, suffix: '+', label: t('landing.statAffiliates') },
                        { value: 98, suffix: '%', label: t('landing.statPayout') },
                        { value: 150, prefix: '$', suffix: 'K+', label: t('landing.statCommissions') },
                    ].map((stat, i) => (
                        <motion.div key={i} variants={fadeUp} custom={4 + i} className="text-center">
                            <p className="text-2xl md:text-3xl font-bold text-gray-900 dark:text-white">
                                <AnimatedCounter target={stat.value} prefix={stat.prefix} suffix={stat.suffix} />
                            </p>
                            <p className="text-sm text-gray-500 mt-1">{stat.label}</p>
                        </motion.div>
                    ))}
                </motion.div>
            </div>

            {/* Bottom fade */}
            <div className="absolute bottom-0 left-0 right-0 h-32 bg-gradient-to-t from-gray-50 dark:from-[#0B0F19] to-transparent pointer-events-none transition-colors duration-500" />
        </section>
    )
}

// ═══════════════════════════════════════════════════════════════════
// HOW IT WORKS
// ═══════════════════════════════════════════════════════════════════

function HowItWorksSection() {
    const { t } = useLanguage()
    const steps = [
        {
            icon: <Package className="w-7 h-7" />,
            title: t('landing.step1Title'),
            description: t('landing.step1Desc'),
            color: 'from-blue-500/20 to-cyan-500/20',
            iconColor: 'text-blue-400',
            borderColor: 'border-blue-500/20',
        },
        {
            icon: <Link2 className="w-7 h-7" />,
            title: t('landing.step2Title'),
            description: t('landing.step2Desc'),
            color: 'from-violet-500/20 to-purple-500/20',
            iconColor: 'text-violet-400',
            borderColor: 'border-violet-500/20',
        },
        {
            icon: <DollarSign className="w-7 h-7" />,
            title: t('landing.step3Title'),
            description: t('landing.step3Desc'),
            color: 'from-emerald-500/20 to-green-500/20',
            iconColor: 'text-emerald-400',
            borderColor: 'border-emerald-500/20',
        },
    ]

    return (
        <section className="relative py-24 px-6">
            <div className="max-w-5xl mx-auto">
                <motion.div
                    variants={fadeUp}
                    initial="hidden"
                    whileInView="visible"
                    viewport={{ once: true }}
                    className="text-center mb-16"
                >
                    <h2 className="text-3xl md:text-4xl font-bold text-gray-900 dark:text-white mb-4">
                        How It Works
                    </h2>
                    <p className="text-gray-600 dark:text-gray-400 text-lg max-w-lg mx-auto">
                        Three simple steps to start earning. No tech skills required.
                    </p>
                </motion.div>

                <motion.div
                    variants={stagger}
                    initial="hidden"
                    whileInView="visible"
                    viewport={{ once: true }}
                    className="grid md:grid-cols-3 gap-6"
                >
                    {steps.map((step, i) => (
                        <motion.div
                            key={i}
                            variants={fadeUp}
                            custom={i}
                            className={`relative p-8 rounded-2xl bg-gradient-to-br ${step.color} border ${step.borderColor} backdrop-blur-sm group hover:scale-[1.02] transition-all duration-300`}
                        >
                            <div className="absolute top-6 right-6 text-6xl font-black text-gray-900 dark:text-white/5">
                                {i + 1}
                            </div>
                            <div className={`w-14 h-14 rounded-xl bg-white dark:bg-white/5 shadow-sm dark:shadow-none flex items-center justify-center mb-6 ${step.iconColor}`}>
                                {step.icon}
                            </div>
                            <h3 className="text-xl font-semibold text-gray-900 dark:text-white mb-3">{step.title}</h3>
                            <p className="text-gray-600 dark:text-gray-400 leading-relaxed">{step.description}</p>
                        </motion.div>
                    ))}
                </motion.div>
            </div>
        </section>
    )
}

// ═══════════════════════════════════════════════════════════════════
// WHY JOIN
// ═══════════════════════════════════════════════════════════════════

function WhyJoinSection() {
    const { t } = useLanguage()
    const features = [
        { icon: <Zap className="w-5 h-5" />, title: t('landing.feat1Title'), desc: t('landing.feat1Desc') },
        { icon: <Shield className="w-5 h-5" />, title: t('landing.feat3Title'), desc: t('landing.feat3Desc') },
        { icon: <BarChart3 className="w-5 h-5" />, title: t('landing.feat2Title'), desc: t('landing.feat2Desc') },
        { icon: <Users className="w-5 h-5" />, title: t('landing.feat6Title'), desc: t('landing.feat6Desc') },
        { icon: <TrendingUp className="w-5 h-5" />, title: t('landing.feat4Title'), desc: t('landing.feat4Desc') },
        { icon: <Star className="w-5 h-5" />, title: t('landing.feat5Title'), desc: t('landing.feat5Desc') },
    ]

    return (
        <section className="relative py-24 px-6">
            <div className="max-w-5xl mx-auto">
                <motion.div
                    variants={fadeUp}
                    initial="hidden"
                    whileInView="visible"
                    viewport={{ once: true }}
                    className="text-center mb-16"
                >
                    <h2 className="text-3xl md:text-4xl font-bold text-gray-900 dark:text-white mb-4">
                        {t('landing.whyTitle')}
                    </h2>
                    <p className="text-gray-600 dark:text-gray-400 text-lg max-w-lg mx-auto">
                        {t('landing.whySubtitle')}
                    </p>
                </motion.div>

                <motion.div
                    variants={stagger}
                    initial="hidden"
                    whileInView="visible"
                    viewport={{ once: true }}
                    className="grid sm:grid-cols-2 lg:grid-cols-3 gap-5"
                >
                    {features.map((f, i) => (
                        <motion.div
                            key={i}
                            variants={fadeUp}
                            custom={i}
                            className="group p-6 rounded-xl border border-gray-200 dark:border-white/5 bg-white dark:bg-white/[0.02] shadow-sm dark:shadow-none hover:bg-gray-50 dark:hover:bg-white/[0.04] hover:shadow-md dark:hover:shadow-none hover:border-gray-300 dark:hover:border-white/10 transition-all duration-300 transform hover:-translate-y-1"
                        >
                            <div className="w-10 h-10 rounded-lg bg-[#6C63FF]/10 flex items-center justify-center text-[#6C63FF] mb-4 group-hover:bg-[#6C63FF]/20 transition-colors">
                                {f.icon}
                            </div>
                            <h4 className="text-gray-900 dark:text-white font-semibold mb-2">{f.title}</h4>
                            <p className="text-gray-500 text-sm leading-relaxed">{f.desc}</p>
                        </motion.div>
                    ))}
                </motion.div>
            </div>
        </section>
    )
}

// ═══════════════════════════════════════════════════════════════════
// FEATURED PRODUCTS
// ═══════════════════════════════════════════════════════════════════

function FeaturedProductsSection() {
    const navigate = useNavigate()
    const { t } = useLanguage()
    const [products, setProducts] = useState([])
    const [loading, setLoading] = useState(true)
    const [searchQuery, setSearchQuery] = useState('')

    useEffect(() => {
        fetchProducts()
    }, [])

    const fetchProducts = async (q = '') => {
        setLoading(true)
        try {
            const response = await axios.get('/api/public/affiliate/products', { params: { q, pageSize: 12 } })
            setProducts(response.data.items || [])
        } catch {
            setProducts([])
        } finally {
            setLoading(false)
        }
    }

    const handleSearch = (e) => {
        e.preventDefault()
        fetchProducts(searchQuery)
    }

    const handlePromote = (slug) => {
        if (!affiliateAuthApi.isAuthenticated()) {
            navigate('/affiliate/register')
        } else {
            navigate(`/affiliate/portal?tab=discover&q=${encodeURIComponent(slug)}`)
        }
    }

    const formatProductPrice = (product) => {
        const currency = resolveProductCurrency(product)
        if (!currency) {
            return 'Currency unavailable'
        }

        if (product.hasVariants) {
            if (product.minVariantPrice == null) {
                return '—'
            }

            if (product.maxVariantPrice != null && product.maxVariantPrice > product.minVariantPrice) {
                return `${currency} ${product.minVariantPrice.toFixed(2)} - ${product.maxVariantPrice.toFixed(2)}`
            }

            return `${currency} ${product.minVariantPrice.toFixed(2)}`
        }

        return product.price != null ? `${currency} ${product.price.toFixed(2)}` : '—'
    }

    return (
        <section className="relative py-24 px-6">
            <div className="max-w-6xl mx-auto">
                <motion.div
                    variants={fadeUp}
                    initial="hidden"
                    whileInView="visible"
                    viewport={{ once: true }}
                    className="text-center mb-12"
                >
                    <h2 className="text-3xl md:text-4xl font-bold text-gray-900 dark:text-white mb-4">
                        {t('landing.featuredTitle')}
                    </h2>
                    <p className="text-gray-600 dark:text-gray-400 text-lg max-w-lg mx-auto mb-8">
                        {t('landing.featuredSubtitle')}
                    </p>

                    {/* Search */}
                    <form onSubmit={handleSearch} className="max-w-xl mx-auto relative">
                        <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
                        <input
                            id="affiliate-landing-search"
                            type="text"
                            value={searchQuery}
                            onChange={(e) => setSearchQuery(e.target.value)}
                            placeholder={t('landing.searchProducts')}
                            className="w-full pl-12 pr-6 py-4 rounded-xl border border-gray-200 dark:border-white/10 bg-white dark:bg-white/5 shadow-sm dark:shadow-none text-gray-900 dark:text-white placeholder:text-gray-500 focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all text-base"
                        />
                    </form>
                </motion.div>

                {/* Loading */}
                {loading && (
                    <div className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
                        {[...Array(8)].map((_, i) => (
                            <div key={i} className="rounded-xl border border-gray-100 dark:border-white/5 bg-white dark:bg-white/[0.02] p-5 shadow-sm dark:shadow-none animate-pulse">
                                <div className="aspect-square bg-gray-200 dark:bg-white/5 rounded-lg mb-4" />
                                <div className="h-4 bg-gray-200 dark:bg-white/5 rounded w-3/4 mb-2" />
                                <div className="h-3 bg-gray-200 dark:bg-white/5 rounded w-1/2 mb-4" />
                                <div className="h-10 bg-gray-200 dark:bg-white/5 rounded-lg" />
                            </div>
                        ))}
                    </div>
                )}

                {/* Products grid */}
                {!loading && products.length > 0 && (
                    <motion.div
                        variants={stagger}
                        initial="hidden"
                        whileInView="visible"
                        viewport={{ once: true }}
                        className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6"
                    >
                        {products.map((product, i) => (
                            <motion.div
                                key={product.productId}
                                variants={fadeUp}
                                custom={i}
                                className="group rounded-xl border border-gray-200 dark:border-white/5 bg-white dark:bg-white/[0.02] shadow-sm dark:shadow-none hover:bg-gray-50 dark:hover:bg-white/[0.04] hover:shadow-md dark:hover:shadow-none hover:border-[#6C63FF]/40 dark:hover:border-[#6C63FF]/30 overflow-hidden transition-all duration-300 transform hover:-translate-y-1"
                            >
                                {/* Image */}
                                <div className="aspect-square bg-gradient-to-br from-gray-100 to-gray-50 dark:from-white/5 dark:to-white/[0.02] flex items-center justify-center overflow-hidden relative">
                                    {product.thumbnail ? (
                                        <img
                                            src={product.thumbnail}
                                            alt={product.name}
                                            className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500"
                                            loading="lazy"
                                        />
                                    ) : (
                                        <Package className="w-12 h-12 text-gray-600" />
                                    )}
                                    {/* Commission rate badge */}
                                    {product.commissionRate != null && (
                                        <div className="absolute top-2 right-2 px-2 py-1 rounded-lg bg-emerald-500/90 text-white text-[11px] font-bold shadow-lg">
                                            {(product.commissionRate * 100).toFixed(0)}% {t('landing.commission')}
                                        </div>
                                    )}
                                </div>

                                {/* Info */}
                                <div className="p-5 space-y-3">
                                    <h4 className="text-gray-900 dark:text-white font-medium text-sm line-clamp-2 leading-snug min-h-[2.5rem]">
                                        {product.name}
                                    </h4>

                                    {product.storeName && (
                                        <p className="text-xs text-gray-500">{t('landing.byStore')} {product.storeName}</p>
                                    )}

                                    <div className="flex items-center justify-between">
                                        <span className="text-lg font-bold text-gray-900 dark:text-white">
                                            {formatProductPrice(product)}
                                        </span>
                                        {product.category && (
                                            <span className="text-xs px-2 py-1 rounded-full bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20">
                                                {product.category}
                                            </span>
                                        )}
                                    </div>

                                    {/* Estimated commission */}
                                    {product.estimatedCommission != null && (
                                        <div className="flex items-center gap-1.5 py-1.5 px-2.5 rounded-lg bg-emerald-500/5 border border-emerald-500/10">
                                            <DollarSign className="w-3.5 h-3.5 text-emerald-400" />
                                            <span className="text-xs text-emerald-400 font-medium">
                                                {t('landing.youEarn')}: <span className="font-bold">{formatProductPrice({ ...product, hasVariants: false, price: product.estimatedCommission })}</span>
                                            </span>
                                        </div>
                                    )}

                                    <button
                                        id={`landing-promote-${product.productId}`}
                                        onClick={() => handlePromote(product.slug)}
                                        className="w-full mt-2 py-2.5 rounded-lg bg-gradient-to-r from-[#6C63FF] to-[#8B5CF6] text-white text-sm font-medium hover:shadow-lg hover:shadow-[#6C63FF]/20 hover:scale-[1.01] transition-all duration-300 flex items-center justify-center gap-2"
                                    >
                                        <Sparkles className="w-4 h-4" />
                                        {t('landing.promote')}
                                    </button>
                                </div>
                            </motion.div>
                        ))}
                    </motion.div>
                )}

                {/* Empty state */}
                {!loading && products.length === 0 && (
                    <div className="text-center py-20">
                        <Package className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                        <h3 className="text-xl font-semibold text-gray-900 dark:text-white mb-2">{t('landing.noProducts')}</h3>
                        <p className="text-gray-500">{t('landing.noProductsDesc')}</p>
                    </div>
                )}
            </div>
        </section>
    )
}

// ═══════════════════════════════════════════════════════════════════
// CTA SECTION
// ═══════════════════════════════════════════════════════════════════

function CTASection() {
    const { t } = useLanguage()
    const navigate = useNavigate()

    return (
        <section className="relative py-24 px-6">
            <div className="max-w-3xl mx-auto text-center">
                {/* Glow */}
                <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[400px] rounded-full bg-[radial-gradient(circle,rgba(108,99,255,0.25)_0%,transparent_65%)] dark:bg-[radial-gradient(circle,rgba(108,99,255,0.15)_0%,transparent_65%)] blur-3xl pointer-events-none transition-colors duration-500" />

                <motion.div
                    variants={fadeUp}
                    initial="hidden"
                    whileInView="visible"
                    viewport={{ once: true }}
                    className="relative z-10"
                >
                    <h2 className="text-3xl md:text-5xl font-bold text-gray-900 dark:text-white mb-6 leading-tight">
                        {t('landing.ctaTitle1')}<br />
                        <span className="bg-gradient-to-r from-[#6C63FF] to-[#A855F7] bg-clip-text text-transparent">
                            {t('landing.ctaTitle2')}
                        </span>
                    </h2>
                    <p className="text-gray-600 dark:text-gray-400 text-lg mb-10 max-w-xl mx-auto">
                        {t('landing.ctaDesc')}
                    </p>
                    <button
                        onClick={() => navigate('/affiliate/register')}
                        className="group inline-flex items-center gap-3 px-10 py-5 rounded-xl bg-gradient-to-r from-[#6C63FF] to-[#8B5CF6] text-white font-semibold text-lg shadow-xl shadow-[#6C63FF]/25 hover:shadow-[#6C63FF]/40 hover:scale-[1.02] transition-all duration-300"
                    >
                        {t('landing.ctaButton')}
                        <ArrowRight className="w-5 h-5 group-hover:translate-x-1 transition-transform" />
                    </button>
                </motion.div>
            </div>
        </section>
    )
}

// ═══════════════════════════════════════════════════════════════════
// FOOTER
// ═══════════════════════════════════════════════════════════════════

function Footer() {
    const { t } = useLanguage()
    return (
        <footer className="border-t border-gray-200 dark:border-white/5 py-12 px-6">
            <div className="max-w-5xl mx-auto flex flex-col md:flex-row items-center justify-between gap-4">
                <div className="flex items-center gap-3">
                    <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-[#6C63FF] to-[#8B5CF6] flex items-center justify-center">
                        <DollarSign className="w-4 h-4 text-white" />
                    </div>
                    <span className="text-gray-600 dark:text-gray-400 font-medium">{t('landing.footerAffiliate')}</span>
                </div>
                <p className="text-sm text-gray-600">
                    © {new Date().getFullYear()} {t('landing.footerRight')}
                </p>
            </div>
        </footer>
    )
}

// ═══════════════════════════════════════════════════════════════════
// MAIN PAGE
// ═══════════════════════════════════════════════════════════════════

const AffiliateLanding = () => {
    const { t, dir, isRTL } = useLanguage()

    return (
        <div dir={dir} className="min-h-screen bg-gray-50 dark:bg-[#0B0F19] text-gray-800 dark:text-gray-200" style={{ fontFamily: isRTL ? "'Noto Kufi Arabic', 'Inter', sans-serif" : "'Inter', -apple-system, system-ui, sans-serif" }}>
            {/* Header */}
            <header className="fixed top-0 left-0 right-0 z-50 border-b border-gray-200 dark:border-white/5 backdrop-blur-xl bg-gray-50 dark:bg-[#0B0F19]/70">
                <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between">
                    <Link to="/affiliate" className="flex items-center gap-3 no-underline">
                        <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-[#6C63FF] to-[#8B5CF6] flex items-center justify-center shadow-lg shadow-[#6C63FF]/20">
                            <DollarSign className="w-5 h-5 text-white" />
                        </div>
                        <span className="text-lg font-bold bg-gradient-to-r from-[#6C63FF] to-[#A855F7] bg-clip-text text-transparent">
                            {t('landing.footerAffiliate')}
                        </span>
                    </Link>
                    <div className="flex items-center gap-3">
                        <ThemeSwitcher />
                        <LanguageSwitcher />
                        <Link
                            to="/affiliate/login"
                            className="px-4 py-2 text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white transition-colors no-underline"
                        >
                            {t('landing.ctaLogin')}
                        </Link>
                        <Link
                            to="/affiliate/register"
                            className="px-5 py-2.5 rounded-lg bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20 hover:bg-[#6C63FF]/20 text-sm font-medium transition-all no-underline"
                        >
                            {t('landing.ctaJoin')}
                        </Link>
                    </div>
                </div>
            </header>

            {/* Spacer for fixed header */}
            <div className="h-16" />

            {/* Sections */}
            <HeroSection />
            <HowItWorksSection />
            <CategoriesSection />
            <FeaturedProductsSection />
            <WhyJoinSection />
            <CTASection />
            <Footer />
        </div>
    )
}

export default AffiliateLanding
