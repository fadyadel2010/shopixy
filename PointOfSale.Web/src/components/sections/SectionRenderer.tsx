import React, { KeyboardEvent, MouseEvent, useMemo } from 'react'
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

function SectionPreview({ section, sectionProps }: { section: HomepageSection; sectionProps?: Record<string, unknown> }) {
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
    return (
      <section className="rounded-3xl bg-gradient-to-br from-slate-950 to-sky-800 px-8 py-12 text-white">
        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-sky-200">Hero</p>
        <h2 className="mt-3 text-4xl font-bold">{title}</h2>
        {subtitle && <p className="mt-3 max-w-2xl text-sm leading-7 text-sky-100">{subtitle}</p>}
      </section>
    )
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
      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">{section.type}</p>
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
