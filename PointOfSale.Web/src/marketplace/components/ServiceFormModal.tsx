import React, { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { CreateServiceRequest } from '../types'

interface ServiceFormModalProps {
    isRTL: boolean
    serviceId?: string
    initialData?: Partial<CreateServiceRequest>
    onSubmit: (data: CreateServiceRequest) => Promise<void>
    onClose: () => void
}

const CATEGORY_VALUES = [
    'Design',
    'Development',
    'Marketing',
    'Writing',
    'Video',
    'Music',
    'Business',
    'Consulting',
    'Other',
] as const

const PRICING_TYPES = ['Fixed', 'Hourly', 'Package'] as const
const CURRENCIES = ['EGP', 'USD', 'EUR', 'GBP'] as const

const parseStartingPrice = (value: string): number | undefined => {
    if (!value) {
        return undefined
    }

    const parsedValue = Number.parseFloat(value)
    return Number.isFinite(parsedValue) ? parsedValue : undefined
}

type ServiceFormData = Omit<CreateServiceRequest, 'startingPrice'> & {
    startingPrice: string
}

export function ServiceFormModal({ serviceId, initialData, onSubmit, onClose, isRTL }: ServiceFormModalProps) {
    const { t } = useTranslation()
    const [imageError, setImageError] = useState<string | null>(null)
    const [formData, setFormData] = useState<ServiceFormData>({
        title: initialData?.title || '',
        description: initialData?.description || '',
        category: initialData?.category || '',
        startingPrice: initialData?.startingPrice == null ? '' : String(initialData.startingPrice),
        pricingType: initialData?.pricingType || 'Fixed',
        currency: initialData?.currency || 'EGP',
        serviceImages: initialData?.serviceImages ?? undefined,
        packages: initialData?.packages ?? undefined,
    })
    const [isSubmitting, setIsSubmitting] = useState(false)
    const [imagePreview, setImagePreview] = useState<string | null>(null)

    const handleClose = () => {
        if (!isSubmitting) {
            onClose()
        }
    }

    React.useEffect(() => {
        if (formData.serviceImages) {
            try {
                const images = JSON.parse(formData.serviceImages)
                if (Array.isArray(images) && images.length > 0) {
                    setImagePreview(images[0])
                }
            } catch {
                // Keep silent when existing stored image JSON is invalid.
            }
        }
    }, [formData.serviceImages])

    const handleImageUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0]
        if (file) {
            setImageError(null)
            const reader = new FileReader()
            reader.onloadend = () => {
                const dataUrl = reader.result as string
                setImagePreview(dataUrl)
                setFormData((prev) => ({ ...prev, serviceImages: JSON.stringify([dataUrl]) }))
            }
            reader.onerror = () => {
                console.error('Failed to read image file')
                setImageError(t('servicesMarketplace.serviceForm.imageUploadFailed', { defaultValue: 'Failed to upload image. Please try again.' }))
                setImagePreview(null)
            }
            reader.readAsDataURL(file)
        }
    }

    const handleSubmit = async (event: React.FormEvent) => {
        event.preventDefault()
        setIsSubmitting(true)

        try {
            const parsedStartingPrice = parseStartingPrice(formData.startingPrice)

            if (formData.startingPrice.trim() && parsedStartingPrice == null) {
                window.alert(t('servicesMarketplace.serviceForm.invalidStartingPrice'))
                return
            }

            const submissionData: CreateServiceRequest = {
                ...formData,
                startingPrice: parsedStartingPrice,
            }

            await onSubmit(submissionData)
            onClose()
        } catch (error) {
            console.error('Failed to save service:', error)
            window.alert(t('servicesMarketplace.serviceForm.saveFailed'))
        } finally {
            setIsSubmitting(false)
        }
    }

    return (
        <div
            className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4"
            onClick={handleClose}
            dir={isRTL ? 'rtl' : 'ltr'}
        >
            <div
                className="bg-white rounded-lg shadow-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto"
                onClick={(event) => event.stopPropagation()}
            >
                <div className="p-6">
                    <h3 className="text-2xl font-bold text-gray-900 mb-6">
                        {serviceId
                            ? t('servicesMarketplace.serviceForm.editTitle')
                            : t('servicesMarketplace.serviceForm.createTitle')}
                    </h3>

                    <form onSubmit={handleSubmit} className="space-y-4">
                        <div>
                            <label className="label">
                                {t('servicesMarketplace.serviceForm.fields.title')}
                            </label>
                            <input
                                type="text"
                                required
                                className="input"
                                value={formData.title}
                                onChange={(event) => setFormData({ ...formData, title: event.target.value })}
                                placeholder={t('servicesMarketplace.serviceForm.placeholders.title')}
                            />
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.serviceForm.fields.description')}
                            </label>
                            <textarea
                                required
                                rows={4}
                                className="input"
                                value={formData.description}
                                onChange={(event) => setFormData({ ...formData, description: event.target.value })}
                                placeholder={t('servicesMarketplace.serviceForm.placeholders.description')}
                            />
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.serviceForm.fields.category')}
                            </label>
                            <select
                                required
                                className="input"
                                value={formData.category}
                                onChange={(event) => setFormData({ ...formData, category: event.target.value })}
                            >
                                <option value="">
                                    {t('servicesMarketplace.serviceForm.placeholders.category')}
                                </option>
                                {CATEGORY_VALUES.map((category) => (
                                    <option key={category} value={category}>
                                        {t(`servicesMarketplace.categories.${category}`)}
                                    </option>
                                ))}
                            </select>
                        </div>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="label">
                                    {t('servicesMarketplace.serviceForm.fields.startingPrice')}
                                </label>
                                <input
                                    type="number"
                                    step="0.01"
                                    className="input"
                                    value={formData.startingPrice}
                                    onChange={(event) =>
                                        setFormData({
                                            ...formData,
                                            startingPrice: event.target.value,
                                        })
                                    }
                                    placeholder={t('servicesMarketplace.serviceForm.placeholders.startingPrice')}
                                />
                            </div>
                            <div>
                                <label className="label">
                                    {t('servicesMarketplace.serviceForm.fields.pricingType')}
                                </label>
                                <select
                                    className="input"
                                    value={formData.pricingType}
                                    onChange={(event) => {
                                        const pricingType = PRICING_TYPES.find(value => value === event.target.value)
                                        if (pricingType) setFormData({ ...formData, pricingType })
                                    }}
                                >
                                    {PRICING_TYPES.map((pricingType) => (
                                        <option key={pricingType} value={pricingType}>
                                            {t(`servicesMarketplace.pricingType.${pricingType}`)}
                                        </option>
                                    ))}
                                </select>
                            </div>
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.serviceForm.fields.currency')}
                            </label>
                            <select
                                className="input"
                                value={formData.currency}
                                onChange={(event) => setFormData({ ...formData, currency: event.target.value })}
                            >
                                {CURRENCIES.map((currency) => (
                                    <option key={currency} value={currency}>
                                        {t(`servicesMarketplace.currency.${currency}`)}
                                    </option>
                                ))}
                            </select>
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.serviceForm.fields.image')}
                            </label>
                            <input
                                type="file"
                                accept="image/*"
                                className="input"
                                onChange={handleImageUpload}
                            />
                            {imagePreview ? (
                                <div className="mt-2">
                                    <img
                                        src={imagePreview}
                                        alt={t('servicesMarketplace.chat.previewAlt')}
                                        className="w-full h-48 object-cover rounded-lg"
                                    />
                                </div>
                            ) : null}
                            {imageError && (
                                <p className="mt-2 text-sm text-red-600">{imageError}</p>
                            )}
                        </div>

                        <div className="flex gap-3 pt-4">
                            <button
                                type="submit"
                                disabled={isSubmitting}
                                className="btn-primary flex-1 disabled:opacity-50"
                            >
                                {isSubmitting
                                    ? t('common.saving')
                                    : serviceId
                                        ? t('servicesMarketplace.serviceForm.update')
                                        : t('servicesMarketplace.serviceForm.create')}
                            </button>
                            <button
                                type="button"
                                onClick={handleClose}
                                disabled={isSubmitting}
                                className="btn-secondary disabled:opacity-50"
                            >
                                {t('common.cancel')}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </div>
    )
}
