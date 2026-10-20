import { cloneElement, useId, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'
import SmartOfferPreview from './SmartOfferPreview'

const STATUS_OPTIONS = ['Inactive', 'Active']
const TRIGGER_OPTIONS = ['ExitIntent', 'TimeDelay', 'ScrollDepth', 'AddToCart', 'BeforeCheckout']
const PAGE_SCOPE_OPTIONS = ['All', 'Home', 'Product', 'Category', 'Cart', 'Checkout']
const COUPON_DISCOUNT_OPTIONS = ['Percentage', 'FixedAmount']
const controlClass = 'input min-w-0 bg-white text-gray-900 placeholder:text-gray-400 disabled:cursor-not-allowed disabled:bg-gray-100 disabled:text-gray-500'

const defaultOffer = {
    name: '', status: 'Inactive', priority: 0, triggerType: 'ExitIntent', pagesScope: 'All',
    delaySeconds: '', scrollPercent: '', showOncePerSession: true, maxDisplaysPerUser: '',
    popupTitle: '', popupDescription: '', buttonText: '', couponCode: '', couponDiscountType: 'Percentage',
    couponDiscountValue: '', couponMinOrderAmount: '', couponMaxDiscountAmount: '', imageUrl: '', backgroundColor: '#f97316',
}

function mapOfferToForm(offer) {
    const form = Object.fromEntries(Object.entries(defaultOffer).map(([field, fallback]) => [field, offer?.[field] ?? fallback]))
    // The admitted API contract supports both string and numeric enum representations.
    for (const [field, options] of Object.entries({ status: STATUS_OPTIONS, triggerType: TRIGGER_OPTIONS, pagesScope: PAGE_SCOPE_OPTIONS, couponDiscountType: COUPON_DISCOUNT_OPTIONS })) {
        if (typeof form[field] === 'number') form[field] = options[form[field]] ?? form[field]
    }
    return form
}

function optionalNumber(value) {
    return value === '' || value == null ? null : Number(value)
}

export default function SmartOfferEditor({ open, offer, ...props }) {
    // A draft belongs to one opening and one record. Refreshing the same DTO must
    // not rehydrate user input, reset focus, or turn off its preview.
    if (!open) return null
    return <SmartOfferEditorSession key={offer?.id ?? offer?.Id ?? 'create'} offer={offer} {...props} />
}

function SmartOfferEditorSession({ offer, saving, error, onClose, onSave }) {
    const { t } = useTranslation()
    const { dir } = useRTL()
    const [formData, setFormData] = useState(() => mapOfferToForm(offer))
    const [showPreview, setShowPreview] = useState(true)
    const nameRef = useRef(null)
    const returnFocus = useRef(document.activeElement)
    const formId = useId()
    const hasCoupon = Boolean(formData.couponCode.trim())
    const helperKey = { TimeDelay: 'timeDelay', ScrollDepth: 'scrollDepth', BeforeCheckout: 'beforeCheckout' }[formData.triggerType] || 'default'
    const change = (field, value) => setFormData(current => ({ ...current, [field]: value }))
    const label = field => t(`SmartOfferEditor.fields.${field}`)
    const placeholder = field => t(`SmartOfferEditor.placeholders.${field}`)

    const handleSubmit = event => {
        event.preventDefault()
        if (saving) return
        onSave({
            ...formData,
            name: formData.name.trim(),
            popupTitle: formData.popupTitle.trim(),
            buttonText: formData.buttonText.trim(),
            priority: formData.priority === '' ? 0 : Number(formData.priority),
            delaySeconds: optionalNumber(formData.delaySeconds),
            scrollPercent: optionalNumber(formData.scrollPercent),
            maxDisplaysPerUser: optionalNumber(formData.maxDisplaysPerUser),
            couponCode: hasCoupon ? formData.couponCode.trim().toUpperCase() : null,
            couponDiscountType: hasCoupon ? formData.couponDiscountType : null,
            couponDiscountValue: hasCoupon ? optionalNumber(formData.couponDiscountValue) : null,
            couponMinOrderAmount: hasCoupon ? optionalNumber(formData.couponMinOrderAmount) : null,
            couponMaxDiscountAmount: hasCoupon ? optionalNumber(formData.couponMaxDiscountAmount) : null,
            popupDescription: formData.popupDescription.trim() || null,
            imageUrl: formData.imageUrl.trim() || null,
            backgroundColor: formData.backgroundColor.trim() || null,
        })
    }

    const textField = (field, labelKey = field, extra = {}) => (
        <Field label={label(labelKey)}>
            <input name={field} value={formData[field]} onChange={event => change(field, event.target.value)}
                className={controlClass} placeholder={placeholder(labelKey)} {...extra} />
        </Field>
    )
    const numericField = (field, labelKey = field, extra = {}) => textField(field, labelKey, { type: 'number', dir: 'ltr', min: 0, step: 1, ...extra })
    const selectField = (field, options, optionKey, extra = {}) => (
        <Field label={label(field)}>
            <select name={field} value={formData[field]} onChange={event => change(field, event.target.value)} className={controlClass} {...extra}>
                {!options.includes(formData[field]) && <option value={formData[field]} disabled>{String(formData[field])}</option>}
                {options.map(option => <option key={option} value={option}>{t(`SmartOfferEditor.options.${optionKey}.${option}`)}</option>)}
            </select>
        </Field>
    )

    return (
        <Dialog.Root open onOpenChange={nextOpen => { if (!nextOpen && !saving) onClose() }}>
            <Dialog.Portal>
                <Dialog.Overlay className="fixed inset-0 z-[80] bg-black/50" />
                <Dialog.Content dir={dir} className="fixed left-1/2 top-1/2 z-[81] flex max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-6xl -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-xl bg-white text-gray-900 shadow-xl focus:outline-none"
                    onOpenAutoFocus={event => { event.preventDefault(); nameRef.current?.focus() }}
                    onCloseAutoFocus={event => { event.preventDefault(); if (returnFocus.current?.isConnected) returnFocus.current.focus() }}
                    onEscapeKeyDown={event => { if (saving) event.preventDefault() }}
                    onPointerDownOutside={event => { if (saving) event.preventDefault() }}>
                    <header className="flex shrink-0 items-start justify-between gap-3 border-b border-gray-200 px-4 py-4 sm:px-6">
                        <div className="min-w-0">
                            <Dialog.Title className="text-xl font-bold">{t(offer ? 'SmartOfferEditor.titles.edit' : 'SmartOfferEditor.titles.create')}</Dialog.Title>
                            <Dialog.Description className="mt-1 text-sm text-gray-500">{t('SmartOfferEditor.subtitle')}</Dialog.Description>
                        </div>
                        <div className="flex shrink-0 items-center gap-2">
                            <button type="button" onClick={() => setShowPreview(current => !current)} aria-expanded={showPreview}
                                aria-controls={`${formId}-preview`} className="btn-secondary text-sm">
                                {t(showPreview ? 'SmartOfferEditor.actions.hidePreview' : 'SmartOfferEditor.actions.preview')}
                            </button>
                            <button type="button" onClick={() => onClose()} disabled={saving} aria-label={t('SmartOfferEditor.actions.closeEditor')}
                                className="rounded-lg px-2 py-1 text-xl text-gray-500 hover:bg-gray-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-500 disabled:opacity-50">×</button>
                        </div>
                    </header>

                    <form id={formId} onSubmit={handleSubmit} className="flex min-h-0 flex-col">
                        <div className={`min-h-0 overflow-y-auto ${showPreview ? 'lg:grid lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]' : ''}`}>
                            <fieldset disabled={saving} className="min-w-0 space-y-6 p-4 sm:p-6">
                                <section className="space-y-4">
                                    <h3 className="text-base font-semibold">{t('SmartOfferEditor.sections.configuration')}</h3>
                                    <div className="grid gap-4 sm:grid-cols-2">
                                        {textField('name', 'name', { required: true, ref: nameRef })}
                                        {selectField('status', STATUS_OPTIONS, 'status')}
                                    </div>
                                    <div className="grid gap-4 sm:grid-cols-2">
                                        {selectField('triggerType', TRIGGER_OPTIONS, 'trigger')}
                                        {selectField('pagesScope', PAGE_SCOPE_OPTIONS, 'pagesScope')}
                                    </div>
                                    <p className="rounded-lg bg-gray-50 p-3 text-sm text-gray-600">{t(`SmartOfferEditor.helperText.${helperKey}`)}</p>
                                    <div className="grid gap-4 sm:grid-cols-2">
                                        {numericField('priority')}
                                        {numericField('maxDisplaysPerUser', 'maxDisplaysPerUser', { min: 1, 'aria-describedby': `${formId}-frequency` })}
                                        {numericField('delaySeconds', 'delaySeconds', { min: formData.triggerType === 'TimeDelay' ? 1 : 0, required: formData.triggerType === 'TimeDelay' })}
                                        {numericField('scrollPercent', 'scrollPercent', { min: 1, max: 100, required: formData.triggerType === 'ScrollDepth' })}
                                    </div>
                                    <p id={`${formId}-frequency`} className="text-xs text-gray-500">{t('SmartOfferEditor.notes.frequency')}</p>
                                    <label className="flex items-center gap-2 text-sm text-gray-700">
                                        <input name="showOncePerSession" type="checkbox" checked={formData.showOncePerSession}
                                            onChange={event => change('showOncePerSession', event.target.checked)} className="h-4 w-4 accent-primary-600" />
                                        {label('showOncePerSession')}
                                    </label>
                                </section>

                                <section className="space-y-4 border-t border-gray-200 pt-6">
                                    <h3 className="text-base font-semibold">{t('SmartOfferEditor.sections.popupContentTitle')}</h3>
                                    <div className="grid gap-4 sm:grid-cols-2">
                                        {textField('popupTitle', 'title', { required: true })}
                                        {textField('buttonText', 'buttonText', { required: true })}
                                    </div>
                                    <Field label={label('description')}>
                                        <textarea name="popupDescription" value={formData.popupDescription} onChange={event => change('popupDescription', event.target.value)}
                                            className={`${controlClass} min-h-24 resize-y`} rows={3} placeholder={placeholder('description')} />
                                    </Field>
                                    <div className="grid gap-4 sm:grid-cols-2">
                                        <Field label={label('backgroundColor')}>
                                            <input name="backgroundColor" dir="ltr" value={formData.backgroundColor} onChange={event => change('backgroundColor', event.target.value)} className={controlClass} placeholder={placeholder('backgroundColor')} />
                                        </Field>
                                        <label className="flex items-center gap-3 self-end text-sm text-gray-700">
                                            <input type="color" aria-label={t('SmartOfferEditor.fields.colorPicker')}
                                                value={/^#[0-9a-f]{6}$/i.test(formData.backgroundColor) ? formData.backgroundColor : '#f97316'}
                                                onChange={event => change('backgroundColor', event.target.value)} className="h-10 w-14 rounded-lg border border-gray-300 bg-white p-1" />
                                            {t('SmartOfferEditor.fields.colorPicker')}
                                        </label>
                                    </div>
                                    {textField('imageUrl', 'imageUrl', { dir: 'ltr' })}
                                </section>

                                <section className="space-y-4 border-t border-gray-200 pt-6">
                                    <h3 className="text-base font-semibold">{t('SmartOfferEditor.sections.coupon')}</h3>
                                    <div className="grid gap-4 sm:grid-cols-2">
                                        {textField('couponCode', 'couponCode', { dir: 'ltr' })}
                                        {selectField('couponDiscountType', COUPON_DISCOUNT_OPTIONS, 'couponDiscountType', { disabled: !hasCoupon })}
                                        {numericField('couponDiscountValue', 'discountValue', { min: 0.01, step: '0.01', max: formData.couponDiscountType === 'Percentage' ? 100 : undefined,
                                            required: hasCoupon, disabled: !hasCoupon, placeholder: placeholder(formData.couponDiscountType === 'Percentage' ? 'discountValuePercentage' : 'discountValueFixed') })}
                                        {numericField('couponMinOrderAmount', 'minOrderAmount', { step: '0.01', disabled: !hasCoupon })}
                                        {numericField('couponMaxDiscountAmount', 'maxDiscountAmount', { step: '0.01', disabled: !hasCoupon,
                                            min: formData.couponDiscountType === 'FixedAmount' ? formData.couponDiscountValue || 0 : 0 })}
                                    </div>
                                    <p className="rounded-lg bg-gray-50 p-3 text-sm text-gray-600">{t('SmartOfferEditor.notes.couponAutoCreate')}</p>
                                </section>
                            </fieldset>

                            {showPreview && <aside id={`${formId}-preview`} className="min-w-0 border-t border-gray-200 bg-gray-50 p-4 sm:p-6 lg:sticky lg:top-0 lg:self-start lg:border-s lg:border-t-0">
                                <h3 className="mb-3 text-base font-semibold">{t('SmartOfferEditor.sections.previewTitle')}</h3>
                                <SmartOfferPreview offer={formData} dir={dir} />
                                <p className="mt-3 text-xs leading-5 text-gray-500">{t('SmartOfferEditor.notes.preview')}</p>
                            </aside>}
                        </div>

                        <footer className="shrink-0 space-y-3 border-t border-gray-200 bg-white px-4 py-4 sm:px-6">
                            {error && <p role="alert" className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
                            <div className="flex flex-wrap justify-end gap-3">
                                <button type="button" onClick={() => onClose()} disabled={saving} className="btn-secondary disabled:opacity-50">{t('SmartOfferEditor.actions.cancel')}</button>
                                <button type="submit" disabled={saving} className="btn-primary disabled:cursor-not-allowed disabled:opacity-50">
                                    {t(saving ? 'SmartOfferEditor.actions.saving' : offer ? 'SmartOfferEditor.actions.saveChanges' : 'SmartOfferEditor.actions.createOffer')}
                                </button>
                            </div>
                        </footer>
                    </form>
                </Dialog.Content>
            </Dialog.Portal>
        </Dialog.Root>
    )
}

function Field({ label, children }) {
    const id = useId()
    return <div className="min-w-0">
        <label htmlFor={id} className="label">{label}</label>
        {cloneElement(children, { id })}
    </div>
}
