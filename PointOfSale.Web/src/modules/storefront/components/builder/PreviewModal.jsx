/**
 * Preview Modal
 * Authenticated draft preview that renders backend preview data locally.
 */

import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, Loader2, Monitor, Smartphone, Tablet, X } from 'lucide-react'

import { ThemeProvider } from '@/storefront/themes/ThemeContext'
import { SectionRenderer } from '@/components/sections/SectionRenderer'
import { getProductTemplatePreviewProduct } from '@/storefront/products/ProductTemplateRegistry'
import { isTenantResolved, setResolvedTenant } from '@/bootstrap'
import { useStoreConfigStore } from '@/store/storeConfigStore'

import { normalizeStoreConfig } from './BuilderThemeProvider'

const DEVICE_SIZES = [
    { key: 'desktop', icon: Monitor, width: '100%', label: { en: 'Desktop', ar: 'سطح المكتب' } },
    { key: 'tablet', icon: Tablet, width: '768px', label: { en: 'Tablet', ar: 'لوحي' } },
    { key: 'mobile', icon: Smartphone, width: '375px', label: { en: 'Mobile', ar: 'جوال' } },
]

function PreviewThemeProvider({ tenantId, storeConfig, children }) {
    const setConfig = useStoreConfigStore((state) => state.setConfig)
    const normalizedConfig = useMemo(() => normalizeStoreConfig(storeConfig), [storeConfig])
    const previousConfigRef = useRef(useStoreConfigStore.getState().config)
    const [isReady, setIsReady] = useState(false)

    useLayoutEffect(() => {
        if (!storeConfig) {
            setIsReady(false)
            return undefined
        }

        setIsReady(false)

        if (tenantId) {
            setResolvedTenant(tenantId, { overwrite: isTenantResolved() })
        }

        setConfig(normalizedConfig)
        setIsReady(true)

        return () => {
            setConfig(previousConfigRef.current)
            setIsReady(false)
        }
    }, [normalizedConfig, setConfig, storeConfig, tenantId])

    if (!storeConfig || !isReady) {
        return null
    }

    return <ThemeProvider>{children}</ThemeProvider>
}

function resolvePreviewSections(previewConfig, currentPage, templateId) {
    if (!previewConfig) {
        return []
    }

    if (currentPage === 'product-template') {
        return previewConfig.productTemplates?.[templateId]?.sections || []
    }

    return previewConfig.pages?.[currentPage]?.sections || []
}

export function PreviewModal({
    isOpen,
    onClose,
    previewConfig,
    previewError,
    isLoading,
    currentPage,
    templateId,
    tenantId,
}) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const [activeDevice, setActiveDevice] = useState('desktop')
    const currentDevice = DEVICE_SIZES.find((device) => device.key === activeDevice)
    const previewSections = useMemo(
        () => resolvePreviewSections(previewConfig, currentPage, templateId),
        [currentPage, previewConfig, templateId],
    )
    
    const dialogRef = useRef(null)
    const previousFocusRef = useRef(null)

    // Focus management and keyboard trap
    useEffect(() => {
        if (!isOpen) {
            return undefined
        }

        // Save current focus
        previousFocusRef.current = document.activeElement

        // Move focus into dialog
        const titleElement = document.getElementById('preview-modal-title')
        if (titleElement) {
            titleElement.setAttribute('tabindex', '-1')
            titleElement.focus()
        }

        // Handle Escape key
        const handleEscape = (event) => {
            if (event.key === 'Escape') {
                onClose()
            }
        }

        // Focus trap implementation
        const handleTabKey = (event) => {
            if (event.key !== 'Tab' || !dialogRef.current) {
                return
            }

            const focusableElements = dialogRef.current.querySelectorAll(
                'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
            )
            const focusableArray = Array.from(focusableElements)
            const firstElement = focusableArray[0]
            const lastElement = focusableArray[focusableArray.length - 1]

            if (event.shiftKey) {
                // Shift + Tab
                if (document.activeElement === firstElement) {
                    event.preventDefault()
                    lastElement?.focus()
                }
            } else {
                // Tab
                if (document.activeElement === lastElement) {
                    event.preventDefault()
                    firstElement?.focus()
                }
            }
        }

        document.addEventListener('keydown', handleEscape)
        document.addEventListener('keydown', handleTabKey)

        // Mark background as inert (only #root, not the portal container)
        const mainContent = document.getElementById('root')
        if (mainContent) {
            mainContent.setAttribute('aria-hidden', 'true')
        }

        return () => {
            document.removeEventListener('keydown', handleEscape)
            document.removeEventListener('keydown', handleTabKey)

            // Restore focus
            if (previousFocusRef.current && typeof previousFocusRef.current.focus === 'function') {
                previousFocusRef.current.focus()
            }

            // Remove inert state from #root
            if (mainContent) {
                mainContent.removeAttribute('aria-hidden')
            }
        }
    }, [isOpen, onClose])

    if (!isOpen) {
        return null
    }

    const sectionProps = currentPage === 'product-template'
        ? { product: getProductTemplatePreviewProduct() }
        : undefined

    const modalContent = (
        <div 
            ref={dialogRef}
            className="fixed inset-0 z-50 flex flex-col bg-slate-950/95 backdrop-blur-sm"
            role="dialog"
            aria-modal="true"
            aria-labelledby="preview-modal-title"
        >
            <div className="flex h-14 items-center justify-between border-b border-slate-700 bg-slate-900 px-4">
                <div className="flex items-center gap-3">
                    <div 
                        id="preview-modal-title"
                        className="flex items-center gap-2 rounded bg-amber-400 px-3 py-1.5 text-sm font-semibold text-amber-950"
                    >
                        <AlertTriangle size={14} />
                        {isRTL ? 'معاينة المسودة' : 'DRAFT PREVIEW'}
                    </div>
                    <span className="text-sm text-slate-300">
                        {isRTL
                            ? 'تعرض هذه النافذة المسودة المصادق عليها فقط ولن تستخدم أي محتوى حي كبديل.'
                            : 'This preview uses authenticated draft data only and will never fall back to live content.'}
                    </span>
                </div>

                <div className="flex items-center gap-3">
                    <div className="flex items-center gap-2 rounded-lg bg-slate-800 p-1">
                        {DEVICE_SIZES.map((device) => {
                            const Icon = device.icon
                            return (
                                <button
                                    key={device.key}
                                    onClick={() => setActiveDevice(device.key)}
                                    className={`flex items-center gap-2 rounded px-3 py-1.5 text-sm transition-colors ${
                                        activeDevice === device.key
                                            ? 'bg-sky-600 text-white'
                                            : 'text-slate-300 hover:text-white'
                                    }`}
                                    title={isRTL ? device.label.ar : device.label.en}
                                >
                                    <Icon size={16} />
                                    <span className="hidden lg:inline">{isRTL ? device.label.ar : device.label.en}</span>
                                </button>
                            )
                        })}
                    </div>

                    <button
                        onClick={onClose}
                        className="flex items-center gap-2 rounded-lg px-3 py-2 text-slate-300 transition-colors hover:bg-slate-800 hover:text-white"
                    >
                        <X size={16} />
                        <span>{isRTL ? 'إغلاق' : 'Close Preview'}</span>
                    </button>
                </div>
            </div>

            <div className="flex-1 overflow-auto p-4">
                <div
                    className="mx-auto overflow-hidden rounded-[28px] border border-slate-700 bg-white shadow-[0_32px_100px_-40px_rgba(15,23,42,0.85)] transition-all duration-300"
                    style={{
                        width: currentDevice?.width ?? '100%',
                        maxWidth: '100%',
                        minHeight: activeDevice === 'desktop' ? 'calc(100vh - 120px)' : '80vh',
                    }}
                >
                    {isLoading ? (
                        <div className="grid min-h-[420px] place-items-center bg-slate-50">
                            <div className="flex items-center gap-3 text-sm font-semibold text-slate-600">
                                <Loader2 className="h-4 w-4 animate-spin" />
                                {isRTL ? 'جارٍ تحميل معاينة المسودة...' : 'Loading draft preview...'}
                            </div>
                        </div>
                    ) : previewError ? (
                        <div className="grid min-h-[420px] place-items-center bg-slate-50 px-8 py-12">
                            <div className="max-w-lg text-center">
                                <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-3xl bg-red-100 text-red-700">
                                    <AlertTriangle className="h-7 w-7" />
                                </div>
                                <h3 className="mt-6 text-2xl font-semibold text-slate-950">
                                    {isRTL ? 'تعذر تحميل المعاينة' : 'Preview could not be loaded'}
                                </h3>
                                <p className="mt-3 text-sm leading-7 text-slate-500">{previewError}</p>
                            </div>
                        </div>
                    ) : !previewConfig ? (
                        <div className="grid min-h-[420px] place-items-center bg-slate-50 px-8 py-12 text-center">
                            <div className="max-w-lg">
                                <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-3xl bg-slate-200 text-slate-600">
                                    <AlertTriangle className="h-7 w-7" />
                                </div>
                                <h3 className="mt-6 text-2xl font-semibold text-slate-950">
                                    {isRTL ? 'المعاينة غير متاحة' : 'Preview unavailable'}
                                </h3>
                                <p className="mt-3 text-sm leading-7 text-slate-500">
                                    {isRTL
                                        ? 'لم يتم تحميل مسودة صالحة لهذه الصفحة.'
                                        : 'No valid draft preview payload was returned for this page.'}
                                </p>
                            </div>
                        </div>
                    ) : (
                        <PreviewThemeProvider tenantId={tenantId} storeConfig={previewConfig}>
                            <main className="min-h-full bg-background text-text">
                                {previewSections.length === 0 ? (
                                    <div className="grid min-h-[420px] place-items-center px-8 py-12 text-center">
                                        <div className="max-w-lg">
                                            <h3 className="text-2xl font-semibold text-slate-950">
                                                {isRTL ? 'لا توجد أقسام منشورة في هذه المسودة بعد' : 'No draft sections are available yet'}
                                            </h3>
                                            <p className="mt-3 text-sm leading-7 text-slate-500">
                                                {isRTL
                                                    ? 'لن يتم عرض أي محتوى حي تلقائياً. أضف أو احفظ أقسام المسودة أولاً.'
                                                    : 'Live content will not be substituted automatically. Add or save draft sections first.'}
                                            </p>
                                        </div>
                                    </div>
                                ) : (
                                    <div className="space-y-5 p-4 md:p-6">
                                        <SectionRenderer sections={previewSections} sectionProps={sectionProps} />
                                    </div>
                                )}
                            </main>
                        </PreviewThemeProvider>
                    )}
                </div>
            </div>
        </div>
    )

    return createPortal(modalContent, document.body)
}
