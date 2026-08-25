import { Fragment, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import VariantMediaPicker from './VariantMediaPicker'

function formatAttributes(attributes) {
    if (!attributes?.length) {
        return '-'
    }

    return attributes.map(attr => `${attr.name}: ${attr.value}`).join(', ')
}

function sortMedia(items) {
    return [...items].sort((left, right) => {
        if (left.position !== right.position) {
            return left.position - right.position
        }

        return new Date(left.createdAtUtc || 0).getTime() - new Date(right.createdAtUtc || 0).getTime()
    })
}

function getResolvedThumbnail(row, mediaById) {
    if (row.mediaId && mediaById.has(row.mediaId)) {
        return mediaById.get(row.mediaId)?.url || null
    }

    if (row.imageUrl) {
        return row.imageUrl
    }

    return null
}

export default function VariantMatrixTable({
    productId,
    rows,
    selectedKeys,
    onToggleSelect,
    onToggleSelectAll,
    onRowChange,
    onSetDefault,
    rowIssues = {}
}) {
    const { t } = useTranslation()
    const [pickerRowKey, setPickerRowKey] = useState(null)
    const [productMedia, setProductMedia] = useState([])
    const tx = (key, fallback) => {
        const value = t(key)
        return value === key ? fallback : value
    }

    useEffect(() => {
        if (!productId) {
            setProductMedia([])
            return
        }

        let isMounted = true

        const fetchProductMedia = async () => {
            try {
                const response = await api.get(`/admin/products/${productId}/media`)

                if (isMounted) {
                    setProductMedia(sortMedia((response.data || []).filter(item => item.mediaType === 'image')))
                }
            } catch (err) {
                if (isMounted) {
                    setProductMedia([])
                }
            }
        }

        fetchProductMedia()

        return () => {
            isMounted = false
        }
    }, [productId])

    const allSelected = rows.length > 0 && rows.every(row => selectedKeys.has(row.key))
    const mediaById = useMemo(() => new Map(productMedia.map(item => [item.id, item])), [productMedia])
    const activePickerRow = pickerRowKey ? rows.find(row => row.key === pickerRowKey) ?? null : null

    const handleSelectMedia = (mediaItem) => {
        if (!activePickerRow) {
            return
        }

        if (mediaItem) {
            onRowChange(activePickerRow.key, {
                mediaId: mediaItem.id,
                imageUrl: ''
            })
        } else {
            onRowChange(activePickerRow.key, {
                mediaId: null
            })
        }

        setPickerRowKey(null)
    }

    return (
        <>
            <div className="overflow-hidden rounded-2xl border border-gray-200 bg-white shadow-sm">
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th className="px-4 py-3 text-start">
                                    <input
                                        type="checkbox"
                                        checked={allSelected}
                                        onChange={(e) => onToggleSelectAll(e.target.checked)}
                                        className="rounded"
                                        aria-label={tx('variants.selectAllVariants', 'Select all variants')}
                                    />
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.image', 'Image')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.variantName', 'Variant Name')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.attributes', 'Attributes')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.sku', 'SKU')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.barcode', 'Barcode')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.costPrice', 'Purchase Price')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.price', 'Selling Price')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.onlinePrice', 'Online Price')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.availableStock', 'Available Stock')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.weight', 'Weight')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.default', 'Default')}
                                </th>
                                <th className="px-4 py-3 text-start text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.status', 'Status')}
                                </th>
                            </tr>
                        </thead>

                        <tbody className="divide-y divide-gray-200 bg-white">
                            {rows.map((row) => {
                                const thumbnailUrl = getResolvedThumbnail(row, mediaById)
                                const usesGalleryImage = !!row.mediaId && mediaById.has(row.mediaId)
                                const usesLegacyImage = !usesGalleryImage && !!row.imageUrl
                                const issues = rowIssues[row.key] || {}
                                const issueInputClass = 'input min-w-[10rem] border-red-300 bg-red-50 text-red-800 focus:border-red-500 focus:ring-red-200'
                                const numberIssueInputClass = 'input min-w-[8rem] border-red-300 bg-red-50 text-red-800 focus:border-red-500 focus:ring-red-200'

                                return (
                                    <Fragment key={row.key}>
                                        <tr className={issues.combination ? 'bg-red-50/60' : row.isDirty ? 'bg-amber-50/50' : ''}>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="checkbox"
                                                    checked={selectedKeys.has(row.key)}
                                                    onChange={(e) => onToggleSelect(row.key, e.target.checked)}
                                                    className="rounded"
                                                    aria-label={`${tx('variants.selectVariant', 'Select variant')}: ${row.name}`}
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <button
                                                    type="button"
                                                    onClick={() => setPickerRowKey(row.key)}
                                                    className="flex min-w-[8rem] items-center gap-3 rounded-xl border border-gray-200 px-3 py-2 text-start transition hover:border-primary-300 hover:bg-primary-50"
                                                >
                                                    {thumbnailUrl ? (
                                                        <img
                                                            src={thumbnailUrl}
                                                            alt={row.name}
                                                            className="h-12 w-12 rounded-lg object-cover"
                                                        />
                                                    ) : (
                                                        <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-gray-100 text-xs text-gray-400">
                                                            IMG
                                                        </div>
                                                    )}

                                                    <div className="min-w-0">
                                                        <div className="truncate text-sm font-semibold text-gray-900">
                                                            {thumbnailUrl ? tx('variants.changeImage', 'Change Image') : tx('variants.selectImage', 'Select Image')}
                                                        </div>
                                                        <div className="truncate text-xs text-gray-500">
                                                            {usesGalleryImage
                                                                ? tx('variants.galleryImage', 'Gallery image')
                                                                : usesLegacyImage
                                                                    ? tx('variants.legacyImage', 'Legacy image')
                                                                    : tx('variants.noImage', 'No variant image')}
                                                        </div>
                                                    </div>
                                                </button>
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <div className="font-medium text-gray-900">{row.name}</div>
                                                {row.isNew && (
                                                    <div className="mt-1 text-xs font-medium text-emerald-700">
                                                        {tx('variants.newVariant', 'New')}
                                                    </div>
                                                )}
                                                {row.isLegacy && (
                                                    <div className="mt-1 text-xs font-medium text-amber-700">
                                                        {tx('variants.legacyVariant', 'Legacy variant')}
                                                    </div>
                                                )}
                                            </td>
                                            <td className="px-4 py-4 align-top text-sm text-gray-600">
                                                {formatAttributes(row.attributes)}
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="text"
                                                    value={row.sku}
                                                    onChange={(e) => onRowChange(row.key, { sku: e.target.value })}
                                                    className={issues.sku ? issueInputClass : 'input min-w-[10rem]'}
                                                    aria-invalid={issues.sku ? 'true' : 'false'}
                                                    aria-label={`${row.name}: ${tx('variants.sku', 'SKU')}`}
                                                    maxLength={100}
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="text"
                                                    value={row.barcode}
                                                    onChange={(e) => onRowChange(row.key, { barcode: e.target.value })}
                                                    className={issues.barcode ? issueInputClass : 'input min-w-[10rem]'}
                                                    aria-invalid={issues.barcode ? 'true' : 'false'}
                                                    aria-label={`${row.name}: ${tx('variants.barcode', 'Barcode')}`}
                                                    maxLength={100}
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="number"
                                                    step="0.01"
                                                    min="0"
                                                    value={row.costPrice}
                                                    onChange={(e) => onRowChange(row.key, { costPrice: e.target.value })}
                                                    className={issues.costPrice ? numberIssueInputClass : 'input min-w-[8rem] text-end'}
                                                    aria-invalid={issues.costPrice ? 'true' : 'false'}
                                                    aria-label={`${row.name}: ${tx('variants.costPrice', 'Cost Price')}`}
                                                    dir="ltr"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="number"
                                                    step="0.01"
                                                    min="0"
                                                    value={row.price}
                                                    onChange={(e) => onRowChange(row.key, { price: e.target.value })}
                                                    className={issues.price ? numberIssueInputClass : 'input min-w-[8rem] text-end'}
                                                    aria-invalid={issues.price ? 'true' : 'false'}
                                                    aria-label={`${row.name}: ${tx('variants.price', 'POS Price')}`}
                                                    dir="ltr"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="number"
                                                    step="0.01"
                                                    min="0"
                                                    value={row.onlinePrice}
                                                    onChange={(e) => onRowChange(row.key, { onlinePrice: e.target.value })}
                                                    className={issues.onlinePrice ? numberIssueInputClass : 'input min-w-[8rem] text-end'}
                                                    aria-invalid={issues.onlinePrice ? 'true' : 'false'}
                                                    aria-label={`${row.name}: ${tx('variants.onlinePrice', 'Online Price')}`}
                                                    placeholder={tx('variants.usesPosPrice', 'Uses POS price')}
                                                    dir="ltr"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <div className="min-w-[8rem] text-sm text-gray-700">
                                                    {row.totalStock ?? 0}
                                                </div>
                                                <div className="mt-1 text-xs text-gray-500">
                                                    {tx('variants.stockManagedOutsideAuthoring', 'Managed by inventory workflows')}
                                                </div>
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="number"
                                                    step="0.001"
                                                    min="0"
                                                    value={row.weight}
                                                    onChange={(e) => onRowChange(row.key, { weight: e.target.value })}
                                                    className={issues.weight ? numberIssueInputClass : 'input min-w-[8rem] text-end'}
                                                    aria-invalid={issues.weight ? 'true' : 'false'}
                                                    aria-label={`${row.name}: ${tx('variants.weight', 'Weight')}`}
                                                    dir="ltr"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="radio"
                                                    name="default-variant"
                                                    checked={row.isDefault}
                                                    onChange={() => onSetDefault(row.key)}
                                                    aria-label={`${tx('variants.default', 'Default')}: ${row.name}`}
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <label className="inline-flex items-center gap-2 text-sm text-gray-700">
                                                    <input
                                                        type="checkbox"
                                                        checked={row.isActive}
                                                        onChange={(e) => onRowChange(row.key, { isActive: e.target.checked })}
                                                        className="rounded"
                                                        aria-label={`${row.name}: ${tx('variants.status', 'Status')} — ${row.isActive ? tx('common.active', 'Active') : tx('common.inactive', 'Inactive')}`}
                                                    />
                                                    <span className="whitespace-nowrap">
                                                        {tx('variants.status', 'Status')}: {row.isActive ? tx('common.active', 'Active') : tx('common.inactive', 'Inactive')}
                                                    </span>
                                                </label>
                                            </td>
                                        </tr>
                                    </Fragment>
                                )
                            })}
                        </tbody>
                    </table>
                </div>
            </div>

            <VariantMediaPicker
                open={!!activePickerRow}
                productId={productId}
                selectedMediaId={activePickerRow?.mediaId ?? null}
                onClose={() => setPickerRowKey(null)}
                onSelect={handleSelectMedia}
            />
        </>
    )
}
