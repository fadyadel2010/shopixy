/**
 * AF-31: Affiliate Landing Page — Categories Section
 *
 * STATE MACHINE:
 * - loading  → skeleton shimmer
 * - loaded   → categories grid
 * - empty    → empty state
 * - error    → retry banner
 *
 * RULES:
 * - Fetches once on mount (no polling)
 * - AbortController on unmount
 * - Link-based navigation (SEO-friendly)
 * - CategoryCard is pure presentational (React.memo)
 * - Responsive grid: 4 cols desktop, 2 tablet, 1 mobile
 * - Subtle hover scale (1.02 max)
 * - Future-ready: trending/featured badges, avg commission
 */
import { useState, useEffect, useRef, memo } from 'react'
import { Link } from 'react-router-dom'
import { motion } from 'framer-motion'
import {
    Grid3X3, RefreshCw, AlertCircle,
    Smartphone, ShoppingBag, Shirt, Home, Briefcase,
    Sparkles, Heart, Wrench, BookOpen
} from 'lucide-react'

// ═══════════════════════════════════════════════════════════════════
// ANIMATION VARIANTS
// ═══════════════════════════════════════════════════════════════════

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({
        opacity: 1, y: 0,
        transition: { delay: i * 0.06, duration: 0.5, ease: [0.25, 0.46, 0.45, 0.94] }
    })
}

const stagger = {
    visible: { transition: { staggerChildren: 0.06 } }
}

// ═══════════════════════════════════════════════════════════════════
// CATEGORY ICON MAPPING (fallback to Grid3X3)
// ═══════════════════════════════════════════════════════════════════

const CATEGORY_ICONS = {
    'electronics': Smartphone,
    'fashion': Shirt,
    'home': Home,
    'beauty': Heart,
    'health': Heart,
    'sports': Sparkles,
    'books': BookOpen,
    'services': Briefcase,
    'tools': Wrench,
    'shopping': ShoppingBag,
}

function getCategoryIcon(slug) {
    const key = Object.keys(CATEGORY_ICONS).find(k =>
        slug?.toLowerCase().includes(k)
    )
    return key ? CATEGORY_ICONS[key] : Grid3X3
}

// ═══════════════════════════════════════════════════════════════════
// CATEGORY CARD (Pure, Stateless, Memoized)
// ═══════════════════════════════════════════════════════════════════

const GRADIENT_PALETTE = [
    { from: 'from-blue-500/10', to: 'to-cyan-500/10', border: 'border-blue-500/20', icon: 'text-blue-500', hoverBorder: 'hover:border-blue-400/40' },
    { from: 'from-violet-500/10', to: 'to-purple-500/10', border: 'border-violet-500/20', icon: 'text-violet-500', hoverBorder: 'hover:border-violet-400/40' },
    { from: 'from-emerald-500/10', to: 'to-green-500/10', border: 'border-emerald-500/20', icon: 'text-emerald-500', hoverBorder: 'hover:border-emerald-400/40' },
    { from: 'from-amber-500/10', to: 'to-orange-500/10', border: 'border-amber-500/20', icon: 'text-amber-500', hoverBorder: 'hover:border-amber-400/40' },
    { from: 'from-rose-500/10', to: 'to-pink-500/10', border: 'border-rose-500/20', icon: 'text-rose-500', hoverBorder: 'hover:border-rose-400/40' },
    { from: 'from-teal-500/10', to: 'to-cyan-500/10', border: 'border-teal-500/20', icon: 'text-teal-500', hoverBorder: 'hover:border-teal-400/40' },
    { from: 'from-indigo-500/10', to: 'to-blue-500/10', border: 'border-indigo-500/20', icon: 'text-indigo-500', hoverBorder: 'hover:border-indigo-400/40' },
    { from: 'from-fuchsia-500/10', to: 'to-purple-500/10', border: 'border-fuchsia-500/20', icon: 'text-fuchsia-500', hoverBorder: 'hover:border-fuchsia-400/40' },
]

/**
 * @param {{ category: CategoryViewModel, index: number }} props
 *
 * CategoryViewModel:
 *   id: string, name: string, slug: string, productCount: number,
 *   icon?: string|null, isTrending?: boolean, isFeatured?: boolean,
 *   avgCommission?: number|null
 */
const CategoryCard = memo(function CategoryCard({ category, index }) {
    const palette = GRADIENT_PALETTE[index % GRADIENT_PALETTE.length]
    const IconComponent = getCategoryIcon(category.slug)

    return (
        <motion.div variants={fadeUp} custom={index}>
            <Link
                to={`/affiliate/category/${encodeURIComponent(category.slug)}`}
                className="group block no-underline"
                id={`af31-category-${category.slug}`}
                aria-label={`Browse ${category.name} — ${category.productCount} products`}
            >
                <div className={`
                    relative p-6 rounded-2xl
                    bg-gradient-to-br ${palette.from} ${palette.to}
                    border ${palette.border} ${palette.hoverBorder}
                    backdrop-blur-sm
                    hover:scale-[1.02] hover:shadow-lg
                    dark:hover:shadow-none
                    transition-all duration-300
                    cursor-pointer
                `}>
                    {/* Badges (future-ready) */}
                    <div className="absolute top-3 right-3 flex gap-1.5">
                        {category.isTrending && (
                            <span className="px-2 py-0.5 text-[10px] font-bold bg-orange-500/20 text-orange-500 rounded-full border border-orange-500/30">
                                🔥 Trending
                            </span>
                        )}
                        {category.isFeatured && (
                            <span className="px-2 py-0.5 text-[10px] font-bold bg-yellow-500/20 text-yellow-600 rounded-full border border-yellow-500/30">
                                ⭐ Featured
                            </span>
                        )}
                    </div>

                    {/* Icon */}
                    <div className={`w-12 h-12 rounded-xl bg-white dark:bg-white/5 shadow-sm dark:shadow-none flex items-center justify-center mb-4 ${palette.icon} group-hover:scale-110 transition-transform duration-300`}>
                        <IconComponent className="w-6 h-6" />
                    </div>

                    {/* Name */}
                    <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-1 group-hover:text-gray-700 dark:group-hover:text-gray-100 transition-colors">
                        {category.name}
                    </h3>

                    {/* Product Count */}
                    <p className="text-sm text-gray-500 dark:text-gray-400">
                        {category.productCount.toLocaleString()} {category.productCount === 1 ? 'product' : 'products'}
                    </p>

                    {/* Average Commission (future) */}
                    {category.avgCommission != null && (
                        <p className="mt-2 text-xs text-emerald-500 font-medium">
                            Avg. {(category.avgCommission * 100).toFixed(0)}% commission
                        </p>
                    )}
                </div>
            </Link>
        </motion.div>
    )
})

// ═══════════════════════════════════════════════════════════════════
// SKELETON LOADER
// ═══════════════════════════════════════════════════════════════════

function CategoriesSkeleton() {
    return (
        <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-5">
            {[...Array(4)].map((_, i) => (
                <div key={i} className="p-6 rounded-2xl border border-gray-200 dark:border-white/[0.06] bg-white/50 dark:bg-white/[0.02] animate-pulse">
                    <div className="w-12 h-12 bg-gray-200 dark:bg-white/5 rounded-xl mb-4" />
                    <div className="h-5 bg-gray-200 dark:bg-white/5 rounded w-2/3 mb-2" />
                    <div className="h-4 bg-gray-200 dark:bg-white/5 rounded w-1/3" />
                </div>
            ))}
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// EMPTY STATE
// ═══════════════════════════════════════════════════════════════════

function CategoriesEmptyState() {
    return (
        <div className="text-center py-16">
            <Grid3X3 className="w-14 h-14 text-gray-400 mx-auto mb-4" />
            <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">
                No categories available yet
            </h3>
            <p className="text-gray-500 text-sm max-w-md mx-auto">
                Categories will appear here as products are added to the marketplace.
                Check back soon!
            </p>
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// ERROR STATE
// ═══════════════════════════════════════════════════════════════════

function CategoriesError({ onRetry }) {
    return (
        <div className="text-center py-12">
            <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
            <h3 className="text-base font-semibold text-gray-900 dark:text-white mb-2">
                Failed to load categories
            </h3>
            <p className="text-gray-500 text-sm mb-4">
                Something went wrong. Please try again.
            </p>
            <button
                onClick={onRetry}
                className="inline-flex items-center gap-2 px-5 py-2 rounded-lg border border-gray-200 dark:border-white/10 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-white/5 transition-all"
            >
                <RefreshCw className="w-4 h-4" />
                Retry
            </button>
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// CATEGORIES SECTION (State Machine Owner)
// ═══════════════════════════════════════════════════════════════════

export default function CategoriesSection() {
    const [state, setState] = useState('loading') // loading | loaded | empty | error
    const [categories, setCategories] = useState([])
    const abortRef = useRef(null)

    const fetchCategories = async () => {
        setState('loading')
        // Cancel previous request
        if (abortRef.current) abortRef.current.abort()
        const controller = new AbortController()
        abortRef.current = controller

        try {
            const response = await fetch('/api/affiliate/discover/categories', {
                signal: controller.signal
            })
            if (!response.ok) throw new Error(`HTTP ${response.status}`)
            const data = await response.json()

            if (!data || data.length === 0) {
                setState('empty')
                setCategories([])
            } else {
                setCategories(data)
                setState('loaded')
            }
        } catch (err) {
            if (err.name === 'AbortError') return
            setState('error')
        }
    }

    useEffect(() => {
        fetchCategories()
        return () => {
            if (abortRef.current) abortRef.current.abort()
        }
    }, [])

    return (
        <section className="relative py-20 px-6" id="af31-categories-section">
            <div className="max-w-6xl mx-auto">
                {/* Section Header */}
                <motion.div
                    variants={fadeUp}
                    initial="hidden"
                    whileInView="visible"
                    viewport={{ once: true }}
                    className="text-center mb-12"
                >
                    <h2 className="text-3xl md:text-4xl font-bold text-gray-900 dark:text-white mb-4">
                        Browse Categories
                    </h2>
                    <p className="text-gray-600 dark:text-gray-400 text-lg max-w-lg mx-auto">
                        Discover products by category and start earning commissions.
                    </p>
                </motion.div>

                {/* State Machine Render */}
                {state === 'loading' && <CategoriesSkeleton />}
                {state === 'empty' && <CategoriesEmptyState />}
                {state === 'error' && <CategoriesError onRetry={fetchCategories} />}
                {state === 'loaded' && (
                    <motion.div
                        variants={stagger}
                        initial="hidden"
                        whileInView="visible"
                        viewport={{ once: true }}
                        className="grid sm:grid-cols-2 lg:grid-cols-4 gap-5"
                    >
                        {categories.map((cat, i) => (
                            <CategoryCard key={cat.id} category={cat} index={i} />
                        ))}
                    </motion.div>
                )}
            </div>
        </section>
    )
}
