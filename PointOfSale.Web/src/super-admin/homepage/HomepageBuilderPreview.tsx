import { useMemo } from 'react'
import { EyeOff, LayoutTemplate, Loader2, Sparkles } from 'lucide-react'

import { SectionRenderer } from '@/components/sections/SectionRenderer'
import type { HomepageSection } from '@/components/sections/homepageSections'
import { StorefrontLayout } from '@/storefront/layout/StorefrontLayout'
import type { StoreConfig } from '@/types/storeConfig'

import BuilderThemeProvider from '@web/modules/storefront/components/builder/BuilderThemeProvider'
import {
    getSectionSchema,
    resolveSectionDataForLanguage,
    type HomepageBuilderSection,
} from './homepageBuilderUtils'

function mapBuilderSectionToHomepageSection(
    section: HomepageBuilderSection,
    language: 'en' | 'ar',
    schemas: Parameters<typeof getSectionSchema>[1],
): HomepageSection {
    return {
        id: section.clientId,
        type: section.type,
        order: section.order,
        enabled: section.isActive,
        data: resolveSectionDataForLanguage(section, getSectionSchema(section.type, schemas), language),
    }
}

function EmptyPreviewState({
    title,
    message,
    icon,
}: {
    title: string
    message: string
    icon: 'empty' | 'hidden'
}) {
    const Icon = icon === 'hidden' ? EyeOff : LayoutTemplate

    return (
        <div className="grid min-h-[480px] place-items-center px-8 py-12">
            <div className="max-w-lg text-center">
                <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-3xl bg-slate-100 text-slate-500">
                    <Icon className="h-7 w-7" />
                </div>
                <h3 className="mt-6 text-2xl font-semibold text-slate-950">{title}</h3>
                <p className="mt-3 text-sm leading-7 text-slate-500">{message}</p>
            </div>
        </div>
    )
}

export default function HomepageBuilderPreview({
    sections,
    selectedSectionClientId,
    onSelectSection,
    storeConfig,
    previewTenantId,
    previewTenantName,
    isLoadingConfig = false,
    language = 'en',
    schemas = [],
}: {
    sections: HomepageBuilderSection[]
    selectedSectionClientId: string | null
    onSelectSection: (clientId: string) => void
    storeConfig: StoreConfig | null
    previewTenantId?: string | null
    previewTenantName?: string | null
    isLoadingConfig?: boolean
    language?: 'en' | 'ar'
    schemas?: Parameters<typeof getSectionSchema>[1]
}) {
    const homepageSections = useMemo(
        () => sections.map((section) => mapBuilderSectionToHomepageSection(section, language, schemas)),
        [language, schemas, sections],
    )

    const visibleSections = useMemo(
        () => homepageSections.filter((section) => section.enabled),
        [homepageSections],
    )

    if (isLoadingConfig) {
        return (
            <div className="grid min-h-[520px] place-items-center rounded-[32px] border border-slate-200 bg-white">
                <div className="flex items-center gap-3 text-sm font-semibold text-slate-600">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Loading storefront preview theme...
                </div>
            </div>
        )
    }

    if (!storeConfig) {
        return (
            <div className="grid min-h-[520px] place-items-center rounded-[32px] border border-dashed border-slate-300 bg-white px-8 py-12 text-center">
                <div className="max-w-lg">
                    <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-3xl bg-sky-100 text-sky-700">
                        <Sparkles className="h-7 w-7" />
                    </div>
                    <h3 className="mt-6 text-2xl font-semibold text-slate-950">
                        {previewTenantId ? 'Storefront preview is not ready yet' : 'Pick a tenant preview context'}
                    </h3>
                    <p className="mt-3 text-sm leading-7 text-slate-500">
                        {previewTenantId
                            ? 'The builder is waiting for the storefront theme configuration before it can render the real customer-facing homepage canvas.'
                            : 'Homepage sections are now rendered through the real storefront theme. Choose a tenant so the builder can use the same layout, theme, and catalog context customers see.'}
                    </p>
                </div>
            </div>
        )
    }

    return (
        <BuilderThemeProvider tenantId={previewTenantId ?? undefined} storeConfig={storeConfig}>
            <div
                dir={language === 'ar' ? 'rtl' : 'ltr'}
                className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]"
            >
                <StorefrontLayout>
                    <main className="flex-1 bg-background">
                        <div className="border-b border-slate-200/80 bg-white/90 px-4 py-3 backdrop-blur md:px-6">
                            <div className="flex flex-wrap items-center justify-between gap-3">
                                <div>
                                    <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-sky-600">
                                        Unified Renderer
                                    </p>
                                    <p className="mt-1 text-sm font-semibold text-slate-950">
                                        Previewing the real storefront homepage renderer
                                    </p>
                                </div>

                                <div className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-semibold text-slate-600">
                                    {previewTenantName ? `Theme + catalog: ${previewTenantName}` : 'Shared storefront preview'}
                                </div>
                            </div>
                        </div>

                        {homepageSections.length === 0 ? (
                            <EmptyPreviewState
                                icon="empty"
                                title="No sections in this draft yet"
                                message="Generate the starter template or add a section from the left rail to see the storefront render here."
                            />
                        ) : visibleSections.length === 0 ? (
                            <EmptyPreviewState
                                icon="hidden"
                                title="All draft sections are hidden"
                                message="Re-enable a section from the editor panel to bring it back into the storefront preview."
                            />
                        ) : (
                            <div className="space-y-5 p-4 md:p-6">
                                <SectionRenderer
                                    sections={homepageSections}
                                    selectedSectionId={selectedSectionClientId}
                                    onSectionSelect={onSelectSection}
                                />
                            </div>
                        )}
                    </main>
                </StorefrontLayout>
            </div>
        </BuilderThemeProvider>
    )
}
