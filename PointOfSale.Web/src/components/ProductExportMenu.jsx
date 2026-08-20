import * as DropdownMenu from '@radix-ui/react-dropdown-menu'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'
import { EXPORT_SCOPE_CURRENT_VIEW, EXPORT_SCOPE_SELECTED } from '../utils/productExport'

// Phase 3 — compact Export menu for the Product Listing.
//
// One trigger, grouped by format (Excel / PDF) with two scopes each
// (Current View / Selected). "Selected" items are disabled with a visible
// reason when nothing is selected, instead of hiding unpredictably.
// Radix provides keyboard navigation, focus management and RTL-aware
// positioning; `dir` keeps alignment correct in Arabic.

const ITEM_CLASS = [
    'flex w-full items-center justify-between gap-4 rounded-md px-3 py-2 text-sm text-gray-700',
    'cursor-pointer select-none outline-none hover:bg-gray-100',
    'data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
].join(' ')

function ExportScopeItem({ label, reason, disabled, onSelect }) {
    return (
        <DropdownMenu.Item className={ITEM_CLASS} disabled={disabled} onSelect={onSelect}>
            <span>{label}</span>
            {reason && (
                <span className="text-xs text-gray-400">{reason}</span>
            )}
        </DropdownMenu.Item>
    )
}

export default function ProductExportMenu({ onExport, selectedCount, available, exporting }) {
    const { t } = useTranslation()
    const { dir } = useRTL()

    const hasSelection = selectedCount > 0
    const triggerDisabled = !available || !!exporting

    const handleSelect = (format) => (scope) => (event) => {
        // Guard against keyboard activation of disabled items racing with state.
        if (scope === EXPORT_SCOPE_SELECTED && !hasSelection) {
            event.preventDefault()
            return
        }
        onExport(format, scope)
    }

    return (
        <DropdownMenu.Root dir={dir}>
            <DropdownMenu.Trigger asChild>
                <button
                    type="button"
                    disabled={triggerDisabled}
                    aria-label={t('products.exportMenuAria')}
                    title={!available ? t('products.exportNothingToExport') : undefined}
                    className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 inline-flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                >
                    <span>{exporting ? t('products.exporting') : t('common.export')}</span>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                    </svg>
                </button>
            </DropdownMenu.Trigger>

            <DropdownMenu.Portal>
                <DropdownMenu.Content
                    dir={dir}
                    sideOffset={6}
                    align="end"
                    className="z-50 min-w-[260px] rounded-lg border border-gray-200 bg-white p-1.5 shadow-lg"
                >
                    <DropdownMenu.Label className="px-3 py-1.5 text-xs font-semibold uppercase tracking-wide text-gray-500">
                        {t('products.exportToExcel')}
                    </DropdownMenu.Label>
                    <ExportScopeItem
                        label={t('products.exportCurrentView')}
                        onSelect={handleSelect('excel')(EXPORT_SCOPE_CURRENT_VIEW)}
                    />
                    <ExportScopeItem
                        label={t('products.exportSelected')}
                        disabled={!hasSelection}
                        reason={
                            hasSelection
                                ? t('products.productsSelected', { count: selectedCount })
                                : t('products.exportNoSelection')
                        }
                        onSelect={handleSelect('excel')(EXPORT_SCOPE_SELECTED)}
                    />

                    <DropdownMenu.Separator className="my-1.5 h-px bg-gray-100" />

                    <DropdownMenu.Label className="px-3 py-1.5 text-xs font-semibold uppercase tracking-wide text-gray-500">
                        {t('products.exportToPdf')}
                    </DropdownMenu.Label>
                    <ExportScopeItem
                        label={t('products.exportCurrentView')}
                        onSelect={handleSelect('pdf')(EXPORT_SCOPE_CURRENT_VIEW)}
                    />
                    <ExportScopeItem
                        label={t('products.exportSelected')}
                        disabled={!hasSelection}
                        reason={
                            hasSelection
                                ? t('products.productsSelected', { count: selectedCount })
                                : t('products.exportNoSelection')
                        }
                        onSelect={handleSelect('pdf')(EXPORT_SCOPE_SELECTED)}
                    />
                </DropdownMenu.Content>
            </DropdownMenu.Portal>
        </DropdownMenu.Root>
    )
}
