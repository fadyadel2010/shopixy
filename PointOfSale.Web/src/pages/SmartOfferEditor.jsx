import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import SmartOfferPreview from './SmartOfferPreview'

const STATUS_OPTIONS = ['Inactive', 'Active']
const TRIGGER_OPTIONS = ['ExitIntent', 'TimeDelay', 'ScrollDepth', 'AddToCart', 'BeforeCheckout']
const PAGE_SCOPE_OPTIONS = ['All', 'Home', 'Product', 'Category', 'Cart', 'Checkout']
const COUPON_DISCOUNT_OPTIONS = ['Percentage', 'FixedAmount']

const defaultOffer = {
    name: '',
    status: 'Inactive',
    priority: 0,
    triggerType: 'ExitIntent',
    pagesScope: 'All',
    delaySeconds: '',
    scrollPercent: '',
    showOncePerSession: true,
    maxDisplaysPerUser: '',
    popupTitle: '',
    popupDescription: '',
    buttonText: '',
    couponCode: '',
    couponDiscountType: 'Percentage',
    couponDiscountValue: '',
    couponMinOrderAmount: '',
    couponMaxDiscountAmount: '',
    imageUrl: '',
    backgroundColor: '#f97316',
}

function mapOfferToForm(offer) {
    if (!offer) {
        return defaultOffer
    }

    return {
        name: offer.name ?? '',
        status: offer.status ?? 'Inactive',
        priority: offer.priority ?? 0,
        triggerType: offer.triggerType ?? 'ExitIntent',
        pagesScope: offer.pagesScope ?? 'All',
        delaySeconds: offer.delaySeconds ?? '',
        scrollPercent: offer.scrollPercent ?? '',
        showOncePerSession: offer.showOncePerSession ?? true,
        maxDisplaysPerUser: offer.maxDisplaysPerUser ?? '',
        popupTitle: offer.popupTitle ?? '',
        popupDescription: offer.popupDescription ?? '',
        buttonText: offer.buttonText ?? '',
        couponCode: offer.couponCode ?? '',
        couponDiscountType: offer.couponDiscountType ?? 'Percentage',
        couponDiscountValue: offer.couponDiscountValue ?? '',
        couponMinOrderAmount: offer.couponMinOrderAmount ?? '',
        couponMaxDiscountAmount: offer.couponMaxDiscountAmount ?? '',
        imageUrl: offer.imageUrl ?? '',
        backgroundColor: offer.backgroundColor ?? '#f97316',
    }
}

function toOptionalInt(value) {
    if (value === '' || value === null || value === undefined) {
        return null
    }

    const parsed = Number.parseInt(value, 10)
    return Number.isNaN(parsed) ? null : parsed
}

function toOptionalDecimal(value) {
    if (value === '' || value === null || value === undefined) {
        return null
    }

    const parsed = Number.parseFloat(value)
    return Number.isNaN(parsed) ? null : parsed
}

export default function SmartOfferEditor({ open, offer, saving, error, onClose, onSave }) {
    const { t } = useTranslation()
    const [formData, setFormData] = useState(defaultOffer)
    const [showPreview, setShowPreview] = useState(false)

    useEffect(() => {
        if (open) {
            setFormData(mapOfferToForm(offer))
            setShowPreview(false)
        }
    }, [offer, open])

    const helperText = useMemo(() => {
        if (formData.triggerType === 'TimeDelay') {
            return t('SmartOfferEditor.helperText.timeDelay')
        }

        if (formData.triggerType === 'ScrollDepth') {
            return t('SmartOfferEditor.helperText.scrollDepth')
        }

        if (formData.triggerType === 'BeforeCheckout') {
            return t('SmartOfferEditor.helperText.beforeCheckout')
        }

        return t('SmartOfferEditor.helperText.default')
    }, [formData.triggerType, t])

    if (!open) {
        return null
    }

    const handleChange = (field, value) => {
        setFormData((current) => ({
            ...current,
            [field]: value,
        }))
    }

    const handleSubmit = (event) => {
        event.preventDefault()
        if (saving) return

        onSave({
            ...formData,
            priority: Number.parseInt(formData.priority, 10) || 0,
            delaySeconds: toOptionalInt(formData.delaySeconds),
            scrollPercent: toOptionalInt(formData.scrollPercent),
            maxDisplaysPerUser: toOptionalInt(formData.maxDisplaysPerUser),
            couponCode: formData.couponCode?.trim()?.toUpperCase() || null,
            couponDiscountType: formData.couponCode?.trim() ? formData.couponDiscountType : null,
            couponDiscountValue: formData.couponCode?.trim() ? toOptionalDecimal(formData.couponDiscountValue) : null,
            couponMinOrderAmount: formData.couponCode?.trim() ? toOptionalDecimal(formData.couponMinOrderAmount) : null,
            couponMaxDiscountAmount: formData.couponCode?.trim() ? toOptionalDecimal(formData.couponMaxDiscountAmount) : null,
            popupDescription: formData.popupDescription?.trim() || null,
            imageUrl: formData.imageUrl?.trim() || null,
            backgroundColor: formData.backgroundColor?.trim() || null,
        })
    }

    return (
        <div className="fixed inset-0 z-[80] overflow-y-auto bg-slate-950/60 px-4 py-8 backdrop-blur-sm">
            <div className="mx-auto max-w-6xl">
                <div className="rounded-[32px] border border-white/10 bg-slate-950 text-white shadow-2xl">
                    <div className="flex items-center justify-between border-b border-white/10 px-6 py-5">
                        <div>
                            <p className="text-xs uppercase tracking-[0.35em] text-orange-300">{t('SmartOfferEditor.badge')}</p>
                            <h2 className="mt-2 text-2xl font-semibold">
                                {offer
                                    ? t('SmartOfferEditor.titles.edit')
                                    : t('SmartOfferEditor.titles.create')}
                            </h2>
                            <p className="mt-1 text-sm text-slate-300">
                                {t('SmartOfferEditor.subtitle')}
                            </p>
                        </div>
                        <div className="flex items-center gap-3">
                            <button
                                type="button"
                                onClick={() => setShowPreview((current) => !current)}
                                className="rounded-2xl border border-white/20 px-4 py-2 text-sm font-medium text-white transition hover:bg-white/10"
                            >
                                {showPreview
                                    ? t('SmartOfferEditor.actions.hidePreview')
                                    : t('SmartOfferEditor.actions.preview')}
                            </button>
                            <button
                                type="button"
                                onClick={onClose}
                                className="rounded-full border border-white/15 p-2 text-xl leading-none text-slate-300 transition hover:bg-white/10"
                                aria-label={t('SmartOfferEditor.actions.closeEditor')}
                            >
                                ×
                            </button>
                        </div>
                    </div>

                    <div className="grid gap-0 lg:grid-cols-[1.2fr_0.8fr]">
                        <form onSubmit={handleSubmit} className="space-y-8 px-6 py-6">
                            <fieldset disabled={saving} className="space-y-8">
                            <section className="space-y-5">
                                <div className="grid gap-4 md:grid-cols-2">
                                    <Field label={t('SmartOfferEditor.fields.name')}>
                                        <input
                                            value={formData.name}
                                            onChange={(event) => handleChange('name', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.name')}
                                            required
                                        />
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.status')}>
                                        <select
                                            value={formData.status}
                                            onChange={(event) => handleChange('status', event.target.value)}
                                            className="input"
                                        >
                                            {STATUS_OPTIONS.map((option) => (
                                                <option key={option} value={option}>
                                                    {t(`SmartOfferEditor.options.status.${option}`)}
                                                </option>
                                            ))}
                                        </select>
                                    </Field>
                                </div>

                                <div className="grid gap-4 md:grid-cols-3">
                                    <Field label={t('SmartOfferEditor.fields.triggerType')}>
                                        <select
                                            value={formData.triggerType}
                                            onChange={(event) => handleChange('triggerType', event.target.value)}
                                            className="input"
                                        >
                                            {TRIGGER_OPTIONS.map((option) => (
                                                <option key={option} value={option}>
                                                    {t(`SmartOfferEditor.options.trigger.${option}`)}
                                                </option>
                                            ))}
                                        </select>
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.pagesScope')}>
                                        <select
                                            value={formData.pagesScope}
                                            onChange={(event) => handleChange('pagesScope', event.target.value)}
                                            className="input"
                                        >
                                            {PAGE_SCOPE_OPTIONS.map((option) => (
                                                <option key={option} value={option}>
                                                    {t(`SmartOfferEditor.options.pagesScope.${option}`)}
                                                </option>
                                            ))}
                                        </select>
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.priority')}>
                                        <input
                                            type="number"
                                            min="0"
                                            value={formData.priority}
                                            onChange={(event) => handleChange('priority', event.target.value)}
                                            className="input"
                                        />
                                    </Field>
                                </div>

                                <div className="rounded-3xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
                                    {helperText}
                                </div>

                                <div className="grid gap-4 md:grid-cols-3">
                                    <Field label={t('SmartOfferEditor.fields.delaySeconds')}>
                                        <input
                                            type="number"
                                            min="0"
                                            value={formData.delaySeconds}
                                            onChange={(event) => handleChange('delaySeconds', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.delaySeconds')}
                                        />
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.scrollPercent')}>
                                        <input
                                            type="number"
                                            min="0"
                                            max="100"
                                            value={formData.scrollPercent}
                                            onChange={(event) => handleChange('scrollPercent', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.scrollPercent')}
                                        />
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.maxDisplaysPerUser')}>
                                        <input
                                            type="number"
                                            min="1"
                                            value={formData.maxDisplaysPerUser}
                                            onChange={(event) => handleChange('maxDisplaysPerUser', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.maxDisplaysPerUser')}
                                        />
                                    </Field>
                                </div>

                                <label className="flex items-center gap-3 rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-sm text-slate-200">
                                    <input
                                        type="checkbox"
                                        checked={formData.showOncePerSession}
                                        onChange={(event) => handleChange('showOncePerSession', event.target.checked)}
                                        className="h-4 w-4 rounded border-white/20 bg-transparent text-orange-400 focus:ring-orange-400"
                                    />
                                    {t('SmartOfferEditor.fields.showOncePerSession')}
                                </label>
                            </section>

                            <section className="space-y-5">
                                <div>
                                    <p className="text-xs uppercase tracking-[0.35em] text-slate-400">{t('SmartOfferEditor.sections.popupContentBadge')}</p>
                                    <h3 className="mt-2 text-lg font-semibold text-white">{t('SmartOfferEditor.sections.popupContentTitle')}</h3>
                                </div>

                                <div className="grid gap-4 md:grid-cols-2">
                                    <Field label={t('SmartOfferEditor.fields.title')}>
                                        <input
                                            value={formData.popupTitle}
                                            onChange={(event) => handleChange('popupTitle', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.title')}
                                            required
                                        />
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.buttonText')}>
                                        <input
                                            value={formData.buttonText}
                                            onChange={(event) => handleChange('buttonText', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.buttonText')}
                                            required
                                        />
                                    </Field>
                                </div>

                                <Field label={t('SmartOfferEditor.fields.description')}>
                                    <textarea
                                        value={formData.popupDescription}
                                        onChange={(event) => handleChange('popupDescription', event.target.value)}
                                        className="input min-h-[120px] resize-y"
                                        placeholder={t('SmartOfferEditor.placeholders.description')}
                                    />
                                </Field>

                                <div className="grid gap-4 md:grid-cols-2">
                                    <Field label={t('SmartOfferEditor.fields.couponCode')}>
                                        <input
                                            value={formData.couponCode}
                                            onChange={(event) => handleChange('couponCode', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.couponCode')}
                                        />
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.couponDiscountType')}>
                                        <select
                                            value={formData.couponDiscountType}
                                            onChange={(event) => handleChange('couponDiscountType', event.target.value)}
                                            className="input"
                                            disabled={!formData.couponCode}
                                        >
                                            {COUPON_DISCOUNT_OPTIONS.map((option) => (
                                                <option key={option} value={option}>
                                                    {t(`SmartOfferEditor.options.couponDiscountType.${option}`)}
                                                </option>
                                            ))}
                                        </select>
                                    </Field>
                                </div>

                                <div className="grid gap-4 md:grid-cols-3">
                                    <Field label={t('SmartOfferEditor.fields.discountValue')}>
                                        <input
                                            type="number"
                                            min="0"
                                            step="0.01"
                                            value={formData.couponDiscountValue}
                                            onChange={(event) => handleChange('couponDiscountValue', event.target.value)}
                                            className="input"
                                            placeholder={
                                                formData.couponDiscountType === 'Percentage'
                                                    ? t('SmartOfferEditor.placeholders.discountValuePercentage')
                                                    : t('SmartOfferEditor.placeholders.discountValueFixed')
                                            }
                                            disabled={!formData.couponCode}
                                        />
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.minOrderAmount')}>
                                        <input
                                            type="number"
                                            min="0"
                                            step="0.01"
                                            value={formData.couponMinOrderAmount}
                                            onChange={(event) => handleChange('couponMinOrderAmount', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.minOrderAmount')}
                                            disabled={!formData.couponCode}
                                        />
                                    </Field>
                                    <Field label={t('SmartOfferEditor.fields.maxDiscountAmount')}>
                                        <input
                                            type="number"
                                            min="0"
                                            step="0.01"
                                            value={formData.couponMaxDiscountAmount}
                                            onChange={(event) => handleChange('couponMaxDiscountAmount', event.target.value)}
                                            className="input"
                                            placeholder={t('SmartOfferEditor.placeholders.maxDiscountAmount')}
                                            disabled={!formData.couponCode}
                                        />
                                    </Field>
                                </div>

                                <div className="rounded-3xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
                                    {t('SmartOfferEditor.notes.couponAutoCreate')}
                                </div>

                                <div className="grid gap-4 md:grid-cols-2">
                                    <Field label={t('SmartOfferEditor.fields.backgroundColor')}>
                                        <div className="flex items-center gap-3">
                                            <input
                                                type="color"
                                                value={formData.backgroundColor || '#f97316'}
                                                onChange={(event) => handleChange('backgroundColor', event.target.value)}
                                                className="h-12 w-16 rounded-2xl border border-white/15 bg-transparent"
                                            />
                                            <input
                                                value={formData.backgroundColor}
                                                onChange={(event) => handleChange('backgroundColor', event.target.value)}
                                                className="input"
                                                placeholder={t('SmartOfferEditor.placeholders.backgroundColor')}
                                            />
                                        </div>
                                    </Field>
                                </div>

                                <Field label={t('SmartOfferEditor.fields.imageUrl')}>
                                    <input
                                        value={formData.imageUrl}
                                        onChange={(event) => handleChange('imageUrl', event.target.value)}
                                        className="input"
                                        placeholder={t('SmartOfferEditor.placeholders.imageUrl')}
                                    />
                                </Field>
                            </section>

                            {error ? (
                                <div className="rounded-2xl border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-200">
                                    {error}
                                </div>
                            ) : null}

                            <div className="flex flex-wrap items-center justify-end gap-3 border-t border-white/10 pt-6">
                                <button
                                    type="button"
                                    onClick={onClose}
                                    className="rounded-2xl border border-white/15 px-5 py-3 text-sm font-medium text-slate-200 transition hover:bg-white/10"
                                >
                                    {t('SmartOfferEditor.actions.cancel')}
                                </button>
                                <button
                                    type="submit"
                                    disabled={saving}
                                    className="rounded-2xl bg-orange-500 px-5 py-3 text-sm font-semibold text-slate-950 transition hover:bg-orange-400 disabled:cursor-not-allowed disabled:opacity-60"
                                >
                                    {saving
                                        ? t('SmartOfferEditor.actions.saving')
                                        : offer
                                            ? t('SmartOfferEditor.actions.saveChanges')
                                            : t('SmartOfferEditor.actions.createOffer')}
                                </button>
                            </div>
                            </fieldset>
                        </form>

                        <aside className="border-t border-white/10 bg-white/[0.03] px-6 py-6 lg:border-l lg:border-t-0">
                            <div className="mb-5">
                                <p className="text-xs uppercase tracking-[0.35em] text-slate-400">{t('SmartOfferEditor.sections.previewBadge')}</p>
                                <h3 className="mt-2 text-lg font-semibold">{t('SmartOfferEditor.sections.previewTitle')}</h3>
                            </div>
                            {showPreview ? (
                                <SmartOfferPreview offer={formData} dir={document.documentElement.dir || 'ltr'} />
                            ) : (
                                <div className="rounded-3xl border border-dashed border-white/15 bg-white/[0.03] px-5 py-12 text-center text-sm text-slate-400">
                                    {t('SmartOfferEditor.previewEmpty')}
                                </div>
                            )}
                        </aside>
                    </div>
                </div>
            </div>

            <style>{`
                .input {
                    width: 100%;
                    border-radius: 1rem;
                    border: 1px solid rgba(255, 255, 255, 0.12);
                    background: rgba(15, 23, 42, 0.42);
                    padding: 0.85rem 1rem;
                    color: white;
                    outline: none;
                    transition: border-color 0.2s ease, box-shadow 0.2s ease;
                }
                .input:focus {
                    border-color: rgba(251, 146, 60, 0.8);
                    box-shadow: 0 0 0 3px rgba(251, 146, 60, 0.18);
                }
                .input::placeholder {
                    color: rgba(203, 213, 225, 0.55);
                }
            `}</style>
        </div>
    )
}

function Field({ label, children }) {
    return (
        <label className="block space-y-2">
            <span className="text-sm font-medium text-slate-200">{label}</span>
            {children}
        </label>
    )
}
