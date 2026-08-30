/**
 * AF-33: Sort Dropdown — Pure Stateless Component
 *
 * RULES:
 * - Receives selectedSort from parent
 * - Emits onChange(newSort) — parent updates URL
 * - No internal state
 * - Extensible (add 'ranking' in AF-34)
 */
import { memo } from 'react'
import { ArrowUpDown } from 'lucide-react'

const SORT_OPTIONS = [
    { value: 'ranking', label: 'Best Match' },
    { value: 'commission_desc', label: 'Highest Commission' },
    { value: 'commission_asc', label: 'Lowest Commission' },
    { value: 'newest', label: 'Newest First' },
    { value: 'price_desc', label: 'Price: High to Low' },
    { value: 'price_asc', label: 'Price: Low to High' },
]

const VALID_SORTS = new Set(SORT_OPTIONS.map(o => o.value))

export function isValidSort(sort) {
    return VALID_SORTS.has(sort)
}

export const DEFAULT_SORT = 'commission_desc'

const SortDropdown = memo(function SortDropdown({ selectedSort, onChange, options = SORT_OPTIONS }) {
    return (
        <div className="relative flex items-center gap-2" id="af33-sort-dropdown">
            <ArrowUpDown className="w-4 h-4 text-gray-500 flex-shrink-0" />
            <select
                value={selectedSort || options[0]?.value || DEFAULT_SORT}
                onChange={(e) => {
                    if (e.target.value !== selectedSort) {
                        onChange(e.target.value)
                    }
                }}
                className="appearance-none bg-white dark:bg-white/[0.03] border border-gray-200 dark:border-white/[0.08] rounded-xl px-3 py-2.5 pr-8 text-sm text-gray-700 dark:text-gray-300 focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all cursor-pointer hover:bg-gray-50 dark:hover:bg-white/[0.05]"
                id="af33-sort-select"
            >
                {options.map(opt => (
                    <option key={opt.value} value={opt.value}>
                        {opt.label}
                    </option>
                ))}
            </select>
        </div>
    )
})

export default SortDropdown
