/**
 * FE-1: Trend Badge Component (Placeholder)
 * 
 * Future: Show trending products, new arrivals, etc.
 */
import { Sparkles } from 'lucide-react'

export default function TrendBadge({ type = 'new', className = '' }) {
    if (type === 'new') {
        return (
            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-purple-500/10 text-purple-400 border border-purple-500/20 text-[10px] font-medium ${className}`}>
                <Sparkles className="w-3 h-3" />
                New
            </span>
        )
    }

    if (type === 'trending') {
        return (
            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-pink-500/10 text-pink-400 border border-pink-500/20 text-[10px] font-medium ${className}`}>
                <Sparkles className="w-3 h-3" />
                Trending
            </span>
        )
    }

    return null
}
