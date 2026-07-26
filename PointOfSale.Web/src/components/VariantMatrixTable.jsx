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
    onEditFallback,
    onDeleteFallback,
    showFallbackActions = false
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
                                <th className="px-4 py-3 text-left">
                                    <input
                                        type="checkbox"
                                        checked={allSelected}
                                        onChange={(e) => onToggleSelectAll(e.target.checked)}
                                        className="rounded"
                                    />
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.image', 'Image')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.variantName', 'Variant Name')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.attributes', 'Attributes')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.sku', 'SKU')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.barcode', 'Barcode')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.price', 'POS Price')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.onlinePrice', 'Online Price')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.costPrice', 'Cost Price')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.availability', 'Stock Status')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.default', 'Default')}
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                    {tx('variants.status', 'Active')}
                                </th>
                                {showFallbackActions && (
                                    <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
                                        {tx('common.actions', 'Actions')}
                                    </th>
                                )}
                            </tr>
                        </thead>

                        <tbody className="divide-y divide-gray-200 bg-white">
                            {rows.map((row) => {
                                const thumbnailUrl = getResolvedThumbnail(row, mediaById)
                                const usesGalleryImage = !!row.mediaId && mediaById.has(row.mediaId)
                                const usesLegacyImage = !usesGalleryImage && !!row.imageUrl
                                const canManageLegacyRow = showFallbackActions && !!row.id && row.isLegacy

                                return (
                                    <Fragment key={row.key}>
                                        <tr className={row.isDirty ? 'bg-amber-50/50' : ''}>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="checkbox"
                                                    checked={selectedKeys.has(row.key)}
                                                    onChange={(e) => onToggleSelect(row.key, e.target.checked)}
                                                    className="rounded"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <button
                                                    type="button"
                                                    onClick={() => setPickerRowKey(row.key)}
                                                    className="flex min-w-[8rem] items-center gap-3 rounded-xl border border-gray-200 px-3 py-2 text-left transition hover:border-primary-300 hover:bg-primary-50"
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
                                                    className="input min-w-[10rem]"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="text"
                                                    value={row.barcode}
                                                    onChange={(e) => onRowChange(row.key, { barcode: e.target.value })}
                                                    className="input min-w-[10rem]"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="number"
                                                    step="0.01"
                                                    value={row.price}
                                                    onChange={(e) => onRowChange(row.key, { price: e.target.value })}
                                                    className="input min-w-[8rem]"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="number"
                                                    step="0.01"
                                                    value={row.onlinePrice}
                                                    onChange={(e) => onRowChange(row.key, { onlinePrice: e.target.value })}
                                                    className="input min-w-[8rem]"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="number"
                                                    step="0.01"
                                                    value={row.costPrice}
                                                    onChange={(e) => onRowChange(row.key, { costPrice: e.target.value })}
                                                    className="input min-w-[8rem]"
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <div className={`inline-flex rounded-full px-3 py-1 text-xs font-semibold ${row.totalStock > 0 ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'}`}>
                                                    {row.totalStock > 0
                                                        ? `${tx('variants.inStock', 'In Stock')} (${row.totalStock})`
                                                        : tx('variants.outOfStock', 'Out of Stock')}
                                                </div>
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <input
                                                    type="radio"
                                                    name="default-variant"
                                                    checked={row.isDefault}
                                                    onChange={() => onSetDefault(row.key)}
                                                />
                                            </td>
                                            <td className="px-4 py-4 align-top">
                                                <label className="inline-flex items-center gap-2 text-sm text-gray-700">
                                                    <input
                                                        type="checkbox"
                                                        checked={row.isActive}
                                                        onChange={(e) => onRowChange(row.key, { isActive: e.target.checked })}
                                                        className="rounded"
                                                    />
                                                    {row.isActive ? tx('common.active', 'Active') : tx('common.inactive', 'Inactive')}
                                                </label>
                                            </td>
                                            {showFallbackActions && (
                                                <td className="px-4 py-4 align-top">
                                                    <div className="flex min-h-[2rem] flex-col gap-2">
                                                        {canManageLegacyRow && (
                                                            <button
                                                                type="button"
                                                                onClick={() => onEditFallback(row)}
                                                                className="text-left text-sm font-medium text-primary-600 transition hover:text-primary-700"
                                                            >
                                                                {tx('variants.advancedEdit', 'Advanced Edit')}
                                                            </button>
                                                        )}
                                                        {canManageLegacyRow && !row.isDefault && (
                                                            <button
                                                                type="button"
                                                                onClick={() => onDeleteFallback(row)}
                                                                className="text-left text-sm font-medium text-red-600 transition hover:text-red-700"
                                                            >
                                                                {tx('common.delete', 'Delete')}
                                                            </button>
                                                        )}
                                                    </div>
                                                </td>
                                            )}
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
