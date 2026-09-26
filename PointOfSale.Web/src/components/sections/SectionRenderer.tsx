import React, { KeyboardEvent, MouseEvent, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../../services/api'
import { normalizeSection, normalizeSections, type HomepageSection, type SectionRendererInputSection } from './homepageSections'

interface SectionRendererProps {
  section?: SectionRendererInputSection
  sections?: SectionRendererInputSection[]
  sectionProps?: Record<string, unknown>
  selectedSectionId?: string | null
  onSectionSelect?: (sectionId: string) => void
}

function textValue(value: unknown, fallback = ''): string {
  return typeof value === 'string' && value.trim() ? value : fallback
}

function CuratedShelfPreview({ ids, maxItems }: { ids: string[]; maxItems: number }) {
  const { t, i18n } = useTranslation()
  const [products, setProducts] = useState<Array<{ id: string; name: string; nameAr?: string }>>([])
  const key = ids.join(',')
  useEffect(() => {
    const controller = new AbortController()
    if (!ids.length) { setProducts([]); return () => controller.abort() }
    api.get('/products/lookup/selected', { params: { ids }, paramsSerializer: { indexes: null }, signal: controller.signal })
      .then(({ data }) => setProducts(Array.isArray(data) ? data : []))
      .catch(() => { if (!controller.signal.aborted) setProducts([]) })
    return () => controller.abort()
  }, [key])
  return <div className="mt-4 grid grid-cols-2 gap-3 md:grid-cols-4">
    {ids.slice(0, maxItems).map(id => {
      const product = products.find(item => item.id.toLowerCase() === id.toLowerCase())
      return <div key={id} className="rounded-lg border border-slate-200 p-3 text-sm">
        {product ? i18n.language === 'ar' && product.nameAr || product.name : t('storefront.homepageEditor.unavailable')}
      </div>
    })}
  </div>
}

function SectionPreview({ section, sectionProps }: { section: HomepageSection; sectionProps?: Record<string, unknown> }) {
  const { t } = useTranslation()
  const data = section.data
  const title = textValue(data.title, textValue(data.heading, section.type))
  const subtitle = textValue(data.subtitle, textValue(data.description))

  if (section.type.includes('product') && sectionProps?.product && typeof sectionProps.product === 'object') {
    const product = sectionProps.product as { name?: string; price?: number; imageUrl?: string }
    return (
      <div className="rounded-2xl border border-slate-200 bg-white p-6">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-sky-600">Product Preview</p>
        <h3 className="mt-2 text-2xl font-semibold text-slate-950">{product.name || title}</h3>
        <p className="mt-2 text-sm text-slate-500">Template section: {section.type}</p>
        {typeof product.price === 'number' && <p className="mt-4 text-lg font-bold text-slate-900">{product.price}</p>}
      </div>
    )
  }

  if (section.type.includes('hero')) {
    const highlights = Array.isArray(data.highlights) ? data.highlights as Array<{ value: string; label: string }> : []
    return (
      <section className="rounded-3xl px-8 py-12" style={{ backgroundColor: textValue(data.backgroundColor, '#111827'), color: textValue(data.textColor, '#ffffff') }}>
        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-sky-200">{t('storefront.homepageEditor.hero')}</p>
        <h2 className="mt-3 text-4xl font-bold">{title}</h2>
        {subtitle && <p className="mt-3 max-w-2xl text-sm leading-7 opacity-80">{subtitle}</p>}
        {highlights.length > 0 && <div className="mt-5 flex flex-wrap gap-5 border-t border-current/20 pt-4">
          {highlights.map((highlight, index) => <div key={index}><strong>{highlight.value}</strong><p className="text-xs opacity-75">{highlight.label}</p></div>)}
        </div>}
      </section>
    )
  }

  if (section.type === 'featured-products' || section.type === 'product-grid') {
    return <section className="rounded-2xl border border-slate-200 bg-white p-6">
      <h2 className="text-xl font-semibold text-slate-950">{title}</h2>
      {subtitle && <p className="mt-2 text-sm text-slate-500">{subtitle}</p>}
      {data.source === 'curated' && Array.isArray(data.productIds) &&
        <CuratedShelfPreview ids={data.productIds as string[]} maxItems={Number(data.maxItems) || 8} />}
    </section>
  }

  if (section.type.includes('category')) {
    return (
      <section className="rounded-2xl border border-slate-200 bg-slate-50 p-6">
        <h2 className="text-xl font-semibold text-slate-950">{title}</h2>
        <div className="mt-4 grid grid-cols-2 gap-3 md:grid-cols-4">
          {[1, 2, 3, 4].map((item) => <div key={item} className="h-20 rounded-xl bg-white shadow-sm" />)}
        </div>
      </section>
    )
  }

  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">{t(`storefront.homepageEditor.sectionTypes.${section.type}`, { defaultValue: section.type })}</p>
      <h2 className="mt-2 text-xl font-semibold text-slate-950">{title}</h2>
      {subtitle && <p className="mt-2 text-sm leading-6 text-slate-500">{subtitle}</p>}
    </section>
  )
}

function RenderedSectionItem({
  section,
  sectionProps,
  isSelected,
  onSectionSelect,
}: {
  section: HomepageSection
  sectionProps?: Record<string, unknown>
  isSelected: boolean
  onSectionSelect?: (sectionId: string) => void
}) {
  if (!section.enabled) {
    return null
  }

  const handleClickCapture = (event: MouseEvent<HTMLDivElement>) => {
    if (!onSectionSelect) return
    event.preventDefault()
    event.stopPropagation()
    onSectionSelect(section.id)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!onSectionSelect) return
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      onSectionSelect(section.id)
    }
  }

  return (
    <div
      className={['section-wrapper', onSectionSelect ? 'cursor-pointer transition-shadow' : '', isSelected ? 'rounded-[28px] ring-4 ring-sky-200/75' : ''].join(' ')}
      data-section-id={section.id}
      data-section-type={section.type}
      onClickCapture={onSectionSelect ? handleClickCapture : undefined}
      onKeyDown={onSectionSelect ? handleKeyDown : undefined}
      role={onSectionSelect ? 'button' : undefined}
      tabIndex={onSectionSelect ? 0 : undefined}
    >
      <SectionPreview section={section} sectionProps={sectionProps} />
    </div>
  )
}

export const SectionRenderer: React.FC<SectionRendererProps> = ({
  section,
  sections,
  sectionProps,
  selectedSectionId = null,
  onSectionSelect,
}) => {
  const normalizedSections = useMemo(() => {
    if (sections?.length) return normalizeSections(sections)
    if (section) return [normalizeSection(section)]
    return []
  }, [section, sections])

  if (normalizedSections.length === 0) return null

  return (
    <>
      {normalizedSections.map((normalizedSection) => (
        <RenderedSectionItem
          key={normalizedSection.id}
          section={normalizedSection}
          sectionProps={sectionProps}
          isSelected={selectedSectionId === normalizedSection.id}
          onSectionSelect={onSectionSelect}
        />
      ))}
    </>
  )
}

export default SectionRenderer
