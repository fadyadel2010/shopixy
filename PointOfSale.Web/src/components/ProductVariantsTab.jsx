import { forwardRef, useEffect, useImperativeHandle, useMemo, useState } from 'react'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useTranslation } from 'react-i18next'
import VariantOptionsBuilder from './VariantOptionsBuilder'
import VariantBulkActions from './VariantBulkActions'
import VariantMatrixTable from './VariantMatrixTable'
import VariantMediaPicker from './VariantMediaPicker'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'

const MAX_COMBINATIONS = 100

function normalizeText(value) {
    return (value || '').trim()
}

function normalizeKeyPart(value) {
    return normalizeText(value).toLowerCase()
}

function createEmptyVariantForm(sortOrder = 0) {
    return {
        sku: '',
        barcode: '',
        price: '',
        onlinePrice: '',
        costPrice: '',
        isActive: true,
        mediaId: null,
        imageUrl: '',
        sortOrder,
        attributes: []
    }
}

function attributesToKey(attributes) {
    if (!attributes?.length) {
        return ''
    }

    return [...attributes]
        .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
        .map(attribute => `${normalizeKeyPart(attribute.name)}=${normalizeKeyPart(attribute.value)}`)
        .join('|')
}

function optionGroupsToSignature(optionGroups) {
    return JSON.stringify(
        optionGroups.map((group) => ({
            name: normalizeText(group.name),
            values: [...new Set(group.values.map(value => normalizeText(value)).filter(Boolean))]
        }))
    )
}

function normalizeVariantAttributeList(attributes) {
    if (Array.isArray(attributes)) {
        return [...attributes]
            .map((attribute, index) => ({
                ...attribute,
                name: normalizeText(attribute?.name),
                value: normalizeText(attribute?.value),
                sortOrder: attribute?.sortOrder ?? index
            }))
            .filter(attribute => attribute.name && attribute.value)
            .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
    }

    if (attributes && typeof attributes === 'object') {
        return Object.entries(attributes)
            .map(([name, value], index) => ({
                name: normalizeText(name),
                value: normalizeText(value),
                sortOrder: index
            }))
            .filter(attribute => attribute.name && attribute.value)
    }

    return []
}

function parseVariantNameAttributes(variantName) {
    const normalizedName = normalizeText(variantName)
    if (!normalizedName) {
        return []
    }

    const separators = [' / ', '/', ' | ', '|', ' - ']
    const selectedSeparator = separators.find(separator => normalizedName.includes(separator))
    if (!selectedSeparator) {
        return []
    }

    const parts = normalizedName
        .split(selectedSeparator)
        .map(part => normalizeText(part))
        .filter(Boolean)

    if (parts.length < 2 || parts.length > 3) {
        return []
    }

    return parts.map((value, index) => ({
        name: `Option ${index + 1}`,
        value,
        sortOrder: index
    }))
}

function normalizeVariantAttributes(variant) {
    const normalizedAttributes = normalizeVariantAttributeList(variant?.attributes)
    if (normalizedAttributes.length > 0) {
        return normalizedAttributes
    }

    return parseVariantNameAttributes(
        variant?.name
        || variant?.variantName
        || variant?.displayName
        || variant?.title
        || ''
    )
}

function normalizeVariantForMatrix(variant) {
    return {
        ...variant,
        attributes: normalizeVariantAttributes(variant)
    }
}

function reconstructOptionGroupsFromVariants(variants) {
    const groups = []
    const groupMap = new Map()

    variants.forEach((variant) => {
        const attributes = normalizeVariantAttributes(variant)

        attributes.forEach((attribute) => {
            const name = normalizeText(attribute.name)
            const value = normalizeText(attribute.value)
            const normalizedName = normalizeKeyPart(name)

            if (!name || !value) {
                return
            }

            if (!groupMap.has(normalizedName) && groups.length < 3) {
                const nextGroup = {
                    id: `derived-${normalizedName}`,
                    name,
                    values: []
                }

                groupMap.set(normalizedName, nextGroup)
                groups.push(nextGroup)
            }

            const group = groupMap.get(normalizedName)
            if (!group) {
                return
            }

            if (!group.values.some(existingValue => normalizeKeyPart(existingValue) === normalizeKeyPart(value))) {
                group.values.push(value)
            }
        })
    })

    return groups
}

function deriveOptionGroupsFromVariants(variants) {
    return reconstructOptionGroupsFromVariants(variants)
}

function getCombinationKeyFromVariant(variant) {
    const attributeKey = attributesToKey(normalizeVariantAttributes(variant))
    return attributeKey || `variant:${variant.id}`
}

function getRowName(attributes, fallbackName) {
    if (!attributes?.length) {
        return fallbackName || 'Default Variant'
    }

    return attributes.map(attribute => attribute.value).join(' / ')
}

function canReuseLegacyDefaultVariant(variant) {
    return !!variant?.isDefault && normalizeVariantAttributes(variant).length === 0
}

function generateCombinations(optionGroups) {
    const completedGroups = optionGroups
        .map((group) => ({
            ...group,
            name: normalizeText(group.name),
            values: [...new Set(group.values.map(value => normalizeText(value)).filter(Boolean))]
        }))
        .filter(group => group.name && group.values.length > 0)

    if (!completedGroups.length) {
        return []
    }

    return completedGroups.reduce((combinations, group) => {
        if (!combinations.length) {
            return group.values.map((value, index) => ([{
                name: group.name,
                value,
                sortOrder: index
            }]))
        }

        return combinations.flatMap((combination) => (
            group.values.map((value, index) => ([
                ...combination,
                {
                    name: group.name,
                    value,
                    sortOrder: index
                }
            ]))
        ))
    }, [])
}

function buildVariantPayload(row, sortOrder) {
    return {
        sku: normalizeText(row.sku) || null,
        barcode: normalizeText(row.barcode) || null,
        price: parseFloat(row.price) || 0,
        onlinePrice: row.onlinePrice !== '' ? parseFloat(row.onlinePrice) : null,
        costPrice: parseFloat(row.costPrice) || 0,
        isActive: !!row.isActive,
        mediaId: row.mediaId || null,
        imageUrl: normalizeText(row.imageUrl) || null,
        sortOrder,
        attributes: (row.attributes || []).map((attribute, index) => ({
            name: attribute.name,
            value: attribute.value,
            sortOrder: index
        }))
    }
}

function rowMatchesVariant(row, variant, sortOrder) {
    const payload = buildVariantPayload(row, sortOrder)
    const original = {
        sku: normalizeText(variant.sku),
        barcode: normalizeText(variant.barcode),
        price: Number(variant.price || 0),
        onlinePrice: variant.onlinePrice == null ? null : Number(variant.onlinePrice),
        costPrice: Number(variant.costPrice || 0),
        isActive: !!variant.isActive,
        mediaId: variant.mediaId || null,
        imageUrl: normalizeText(variant.imageUrl),
        sortOrder: variant.sortOrder ?? 0,
        attributes: (variant.attributes || []).map((attribute, index) => ({
            name: attribute.name,
            value: attribute.value,
            sortOrder: attribute.sortOrder ?? index
        }))
    }

    return JSON.stringify(payload) === JSON.stringify(original)
}

function buildMatrixRow({ key, variant, attributes, draft, isDefault, index, isLegacy = false }) {
    const base = variant
        ? {
            id: variant.id,
            productId: variant.productId,
            sku: variant.sku || '',
            barcode: variant.barcode || '',
            price: variant.price?.toString() || '',
            onlinePrice: variant.onlinePrice?.toString() || '',
            costPrice: variant.costPrice?.toString() || '',
            isActive: variant.isActive,
            mediaId: variant.mediaId || null,
            imageUrl: variant.imageUrl || '',
            totalStock: variant.totalStock || 0,
            stocks: variant.stocks || [],
            original: variant
        }
        : {
            id: null,
            productId: null,
            sku: '',
            barcode: '',
            price: '',
            onlinePrice: '',
            costPrice: '',
            isActive: true,
            mediaId: null,
            imageUrl: '',
            totalStock: 0,
            stocks: [],
            original: null
        }

    const merged = {
        ...base,
        ...(draft || {})
    }

    return {
        ...merged,
        key,
        attributes,
        name: getRowName(attributes, variant ? getRowName(variant.attributes, variant.sku || variant.barcode) : 'Default Variant'),
        isDefault,
        isLegacy,
        isNew: !variant,
        sortOrder: index,
        isDirty: !!draft
    }
}

const ProductVariantsTab = forwardRef(function ProductVariantsTab({ productId, forceMatrixMode = false }, ref) {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const tx = (key, fallback) => {
        const value = t(key)
        return value === key ? fallback : value
    }
    const [loading, setLoading] = useState(true)
    const [savingMatrix, setSavingMatrix] = useState(false)
    const [applyingStock, setApplyingStock] = useState(false)
    const [variants, setVariants] = useState([])
    const [optionGroups, setOptionGroups] = useState([])
    const [rowDrafts, setRowDrafts] = useState({})
    const [selectedKeys, setSelectedKeys] = useState(new Set())
    const [defaultKey, setDefaultKey] = useState(null)
    const [error, setError] = useState('')
    const [success, setSuccess] = useState('')
    const [showLegacyRows, setShowLegacyRows] = useState(false)

    const [showModal, setShowModal] = useState(false)
    const [showManualMediaPicker, setShowManualMediaPicker] = useState(false)
    const [editingVariant, setEditingVariant] = useState(null)
    const [confirmDelete, setConfirmDelete] = useState(null)
    const [formData, setFormData] = useState(createEmptyVariantForm())
    const [newAttribute, setNewAttribute] = useState({ name: '', value: '' })

    useEffect(() => {
        if (productId) {
            fetchVariants()
        }
    }, [productId])

    const fetchVariants = async () => {
        try {
            setLoading(true)
            const response = await api.get(`/products/${productId}/variants`)
            const nextVariants = (response.data || []).map(normalizeVariantForMatrix)
            const derivedGroups = reconstructOptionGroupsFromVariants(nextVariants)
            const currentDefault = nextVariants.find(variant => variant.isDefault)

            setVariants(nextVariants)
            setOptionGroups(derivedGroups)
            setRowDrafts({})
            setSelectedKeys(new Set())
            setDefaultKey(currentDefault ? getCombinationKeyFromVariant(currentDefault) : null)
        } catch (err) {
            console.error('Failed to load variants', err)
            setError(tx('variants.failedToLoad', 'Failed to load variants'))
        } finally {
            setLoading(false)
        }
    }

    const originalOptionGroups = useMemo(() => deriveOptionGroupsFromVariants(variants), [variants])
    const generatedCombinations = useMemo(() => generateCombinations(optionGroups), [optionGroups])
    const generationBlocked = generatedCombinations.length > MAX_COMBINATIONS
    const hasMultipleVariants = variants && variants.length > 1
    const isMatrixMode = forceMatrixMode || (optionGroups?.length || 0) > 0 || hasMultipleVariants

    useEffect(() => {
        if (!variants?.length) {
            return
        }

        if (variants.length > 1 && optionGroups.length === 0) {
            const reconstructed = reconstructOptionGroupsFromVariants(variants)

            if (reconstructed?.length > 0) {
                setOptionGroups(reconstructed)
            }
        }
    }, [optionGroups.length, variants])

    const matrixRows = useMemo(() => {
        const rows = []
        const matchedVariantIds = new Set()
        const existingByKey = new Map(
            variants.map(variant => [attributesToKey(variant.attributes || []), variant])
        )
        const reusableLegacyDefaultVariant = variants.find(canReuseLegacyDefaultVariant) || null

        if (generatedCombinations.length > 0 && !generationBlocked) {
            generatedCombinations.forEach((attributes, index) => {
                const key = attributesToKey(attributes)
                let existingVariant = existingByKey.get(key)
                if (!existingVariant && index === 0 && reusableLegacyDefaultVariant && !matchedVariantIds.has(reusableLegacyDefaultVariant.id)) {
                    existingVariant = reusableLegacyDefaultVariant
                }

                if (existingVariant) {
                    matchedVariantIds.add(existingVariant.id)
                }

                rows.push(buildMatrixRow({
                    key,
                    variant: existingVariant,
                    attributes,
                    draft: rowDrafts[key],
                    isDefault: defaultKey ? defaultKey === key : existingVariant?.isDefault,
                    index
                }))
            })
        }

        variants
            .filter(variant => !matchedVariantIds.has(variant.id))
            .forEach((variant, index) => {
                const fallbackKey = getCombinationKeyFromVariant(variant)
                rows.push(buildMatrixRow({
                    key: fallbackKey,
                    variant,
                    attributes: variant.attributes || [],
                    draft: rowDrafts[fallbackKey],
                    isDefault: defaultKey ? defaultKey === fallbackKey : variant.isDefault,
                    index: rows.length + index,
                    isLegacy: generatedCombinations.length > 0
                }))
            })

        return rows
    }, [defaultKey, generatedCombinations, generationBlocked, rowDrafts, variants])

    useEffect(() => {
        if (matrixRows.length === 0) {
            if (defaultKey !== null) {
                setDefaultKey(null)
            }
            return
        }

        if (!defaultKey || !matrixRows.some(row => row.key === defaultKey)) {
            const persistedDefault = matrixRows.find(row => row.original?.isDefault)
            setDefaultKey(persistedDefault?.key || matrixRows[0].key)
        }
    }, [defaultKey, matrixRows])

    useEffect(() => {
        setSelectedKeys((prev) => {
            const validKeys = new Set(matrixRows.map(row => row.key))
            return new Set([...prev].filter(key => validKeys.has(key)))
        })
    }, [matrixRows])

    const legacyRows = useMemo(
        () => matrixRows.filter(row => row.isLegacy),
        [matrixRows]
    )
    const visibleMatrixRows = useMemo(
        () => showLegacyRows ? matrixRows : matrixRows.filter(row => !row.isLegacy),
        [matrixRows, showLegacyRows]
    )

    useEffect(() => {
        if (isMatrixMode && showModal) {
            setShowModal(false)
        }
    }, [isMatrixMode, showModal])

    useEffect(() => {
        if (showLegacyRows || legacyRows.length === 0) {
            return
        }

        const legacyRowKeys = new Set(legacyRows.map(row => row.key))
        setSelectedKeys((prev) => new Set([...prev].filter(key => !legacyRowKeys.has(key))))
    }, [legacyRows, showLegacyRows])

    const hasUnsavedChanges = useMemo(() => {
        return Object.keys(rowDrafts).length > 0
            || optionGroupsToSignature(optionGroups) !== optionGroupsToSignature(originalOptionGroups)
            || !!(defaultKey && matrixRows.find(row => row.original?.isDefault)?.key !== defaultKey)
    }, [defaultKey, matrixRows, optionGroups, originalOptionGroups, rowDrafts])

    const handleAddVariant = () => {
        setEditingVariant(null)
        setFormData(createEmptyVariantForm(matrixRows.length))
        setNewAttribute({ name: '', value: '' })
        setShowManualMediaPicker(false)
        setShowModal(true)
    }

    const handleEditVariant = (variant) => {
        setEditingVariant(variant)
        setFormData({
            sku: variant.sku || '',
            barcode: variant.barcode || '',
            price: variant.price?.toString() || '',
            onlinePrice: variant.onlinePrice?.toString() || '',
            costPrice: variant.costPrice?.toString() || '',
            isActive: variant.isActive,
            mediaId: variant.mediaId || null,
            imageUrl: variant.imageUrl || '',
            sortOrder: variant.sortOrder ?? 0,
            attributes: variant.attributes || []
        })
        setNewAttribute({ name: '', value: '' })
        setShowManualMediaPicker(false)
        setShowModal(true)
    }

    const handleSelectManualMedia = (mediaItem) => {
        setFormData((prev) => ({
            ...prev,
            mediaId: mediaItem?.id || null,
            imageUrl: mediaItem ? '' : prev.imageUrl
        }))
        setShowManualMediaPicker(false)
    }

    const handleSaveVariant = async () => {
        setError('')

        try {
            const payload = buildVariantPayload(formData, formData.sortOrder)

            if (editingVariant) {
                await api.put(`/products/${productId}/variants/${editingVariant.id}`, payload)
                setSuccess(tx('variants.variantUpdated', 'Variant updated successfully'))
            } else {
                await api.post(`/products/${productId}/variants`, payload)
                setSuccess(tx('variants.variantCreated', 'Variant created successfully'))
            }

            invalidatePosRuntimeCache({ reason: 'product-updated' })
            setShowModal(false)
            await fetchVariants()
            setTimeout(() => setSuccess(''), 3000)
        } catch (err) {
            console.error('Failed to save variant', err)
            setError(err.response?.data?.message || tx('variants.failedToSave', 'Failed to save variant'))
        }
    }

    const handleDeleteVariant = (variant) => {
        if (variant.isDefault) {
            setError(tx('variants.cannotDeleteDefault', 'Cannot delete the default variant. Set another variant as default first.'))
            setTimeout(() => setError(''), 5000)
            return
        }

        setConfirmDelete(variant)
    }

    const confirmDeleteVariant = async () => {
        if (!confirmDelete) return

        try {
            const headers = {}
            if (confirmDelete.totalStock > 0) {
                headers['X-Confirm-Delete'] = 'true'
            }

            await api.delete(`/products/${productId}/variants/${confirmDelete.id}`, { headers })
            setSuccess(tx('variants.variantDeleted', 'Variant deleted successfully'))
            setConfirmDelete(null)
            invalidatePosRuntimeCache({ reason: 'product-updated' })
            await fetchVariants()
            setTimeout(() => setSuccess(''), 3000)
        } catch (err) {
            console.error('Failed to delete variant', err)
            setError(err.response?.data?.message || tx('variants.failedToDelete', 'Failed to delete variant'))
            setConfirmDelete(null)
        }
    }

    const handleAddAttribute = () => {
        if (!newAttribute.name || !newAttribute.value) return

        setFormData(prev => ({
            ...prev,
            attributes: [...prev.attributes, {
                name: newAttribute.name,
                value: newAttribute.value,
                sortOrder: prev.attributes.length
            }]
        }))
        setNewAttribute({ name: '', value: '' })
    }

    const handleRemoveAttribute = (index) => {
        setFormData(prev => ({
            ...prev,
            attributes: prev.attributes.filter((_, i) => i !== index)
        }))
    }

    const handleRowChange = (key, patch) => {
        setRowDrafts(prev => ({
            ...prev,
            [key]: {
                ...(prev[key] || {}),
                ...patch
            }
        }))
    }

    const handleToggleSelect = (key, checked) => {
        setSelectedKeys(prev => {
            const next = new Set(prev)
            if (checked) {
                next.add(key)
            } else {
                next.delete(key)
            }
            return next
        })
    }

    const handleToggleSelectAll = (checked) => {
        if (!checked) {
            setSelectedKeys(new Set())
            return
        }

        setSelectedKeys(new Set(visibleMatrixRows.map(row => row.key)))
    }

    const handleApplyBulkAction = async ({ type, value, warehouseId, reason }) => {
        setError('')

        const selectedRows = matrixRows.filter(row => selectedKeys.has(row.key))
        if (!selectedRows.length) {
            return
        }

        if (type === 'set-price') {
            setRowDrafts(prev => {
                const next = { ...prev }
                selectedRows.forEach((row) => {
                    next[row.key] = { ...(next[row.key] || {}), price: value }
                })
                return next
            })
            return
        }

        if (type === 'increase-percent') {
            const percent = parseFloat(value)
            if (Number.isNaN(percent)) {
                setError(t('variants.invalidPercentage') || 'Enter a valid percentage')
                return
            }

            setRowDrafts(prev => {
                const next = { ...prev }
                selectedRows.forEach((row) => {
                    const currentPrice = parseFloat(row.price) || 0
                    const currentOnlinePrice = row.onlinePrice !== '' ? (parseFloat(row.onlinePrice) || 0) : null
                    next[row.key] = {
                        ...(next[row.key] || {}),
                        price: (currentPrice * (1 + percent / 100)).toFixed(2),
                        onlinePrice: currentOnlinePrice == null ? '' : (currentOnlinePrice * (1 + percent / 100)).toFixed(2)
                    }
                })
                return next
            })
            return
        }

        if (type === 'set-active') {
            setRowDrafts(prev => {
                const next = { ...prev }
                selectedRows.forEach((row) => {
                    next[row.key] = { ...(next[row.key] || {}), isActive: !!value }
                })
                return next
            })
            return
        }

        if (type === 'set-stock') {
            const quantity = parseFloat(value)
            if (Number.isNaN(quantity) || quantity < 0) {
                setError(tx('variants.invalidStockQuantity', 'Enter a valid stock quantity'))
                return
            }

            const persistedRows = selectedRows.filter(row => row.id)
            if (!persistedRows.length) {
                setError(tx('variants.saveBeforeStock', 'Save new variants before applying stock changes.'))
                return
            }

            try {
                setApplyingStock(true)
                for (const row of persistedRows) {
                    await api.put(`/products/${productId}/variants/${row.id}/stock/${warehouseId}`, {
                        quantity,
                        reason: normalizeText(reason) || 'Variant matrix bulk update'
                    })
                }

                setSuccess(tx('inventoryManagement.stockAdjusted', 'Stock updated'))
                invalidatePosRuntimeCache({ reason: 'stock-adjusted' })
                await fetchVariants()
                setTimeout(() => setSuccess(''), 3000)
            } catch (err) {
                console.error('Failed to update stock', err)
                setError(err.response?.data?.message || tx('inventoryManagement.failedToAdjust', 'Failed to adjust stock'))
            } finally {
                setApplyingStock(false)
            }
        }
    }

    const saveMatrixChanges = async () => {
        setError('')

        if (generationBlocked) {
            setError(tx('variants.combinationLimitWarning', `Maximum ${MAX_COMBINATIONS} combinations allowed.`))
            return false
        }

        if (!matrixRows.length) {
            setError(tx('variants.noVariants', 'No variants to save.'))
            return false
        }

        if (matrixRows.some(row => !row.price || parseFloat(row.price) <= 0)) {
            setError(tx('variants.priceRequired', 'Each variant requires a valid POS price.'))
            return false
        }

        const rowsToCreate = matrixRows.filter(row => !row.id)
        const rowsToUpdate = matrixRows.filter((row, index) => row.id && !rowMatchesVariant(row, row.original, index))
        const desiredExistingIds = new Set(matrixRows.filter(row => row.id).map(row => row.id))
        const rowsToDelete = variants.filter(variant => !desiredExistingIds.has(variant.id))

        if (rowsToDelete.some(row => row.totalStock > 0)) {
            const confirmed = await confirm({
                confirmLabel: tx('common.confirm', 'Confirm'),
                description: tx('variants.deleteWithStockConfirm', 'Some removed combinations still have stock. Continue and delete them anyway?'),
                title: tx('common.warning', 'Warning'),
                variant: 'warning',
            })

            if (!confirmed) {
                return false
            }
        }

        try {
            setSavingMatrix(true)
            const keyToId = new Map(matrixRows.filter(row => row.id).map(row => [row.key, row.id]))

            for (const row of rowsToCreate) {
                const absoluteIndex = matrixRows.findIndex(matrixRow => matrixRow.key === row.key)
                const response = await api.post(`/products/${productId}/variants`, buildVariantPayload(row, absoluteIndex))
                keyToId.set(row.key, response.data.id)
            }

            for (const row of rowsToUpdate) {
                const absoluteIndex = matrixRows.findIndex(matrixRow => matrixRow.key === row.key)
                await api.put(`/products/${productId}/variants/${row.id}`, buildVariantPayload(row, absoluteIndex))
            }

            const desiredDefaultKey = defaultKey || matrixRows[0]?.key
            const desiredDefaultId = keyToId.get(desiredDefaultKey)
            const currentDefaultId = variants.find(variant => variant.isDefault)?.id

            if (desiredDefaultId && desiredDefaultId !== currentDefaultId) {
                await api.put(`/products/${productId}/variants/${desiredDefaultId}/set-default`)
            }

            for (const row of rowsToDelete) {
                const headers = {}
                if (row.totalStock > 0) {
                    headers['X-Confirm-Delete'] = 'true'
                }

                await api.delete(`/products/${productId}/variants/${row.id}`, { headers })
            }

            setSuccess(tx('variants.variantMatrixSaved', 'Variant matrix saved successfully'))
            invalidatePosRuntimeCache({ reason: 'product-updated' })
            await fetchVariants()
            setTimeout(() => setSuccess(''), 3000)
            return true
        } catch (err) {
            console.error('Failed to save variant matrix', err)
            setError(
                err.response?.status === 401
                    ? 'Your session expired or this account is not allowed to manage variants. Please sign in again.'
                    :
                err.response?.data?.message
                || err.response?.data?.error
                || tx('variants.failedToSave', 'Failed to save variant matrix')
            )
            return false
        } finally {
            setSavingMatrix(false)
        }
    }

    const handleSaveMatrix = async () => {
        await saveMatrixChanges()
    }

    useImperativeHandle(ref, () => ({
        async savePendingChanges() {
            if (!isMatrixMode || !hasUnsavedChanges) {
                return true
            }

            return await saveMatrixChanges()
        }
    }), [hasUnsavedChanges, isMatrixMode, saveMatrixChanges])

    const hasDraftSelection = matrixRows.some(row => selectedKeys.has(row.key) && !row.id)

    if (!productId) {
        return (
            <div className="p-6 text-center text-gray-500">
                <p>{tx('variants.saveProductFirst', 'Please save the product first to manage variants.')}</p>
            </div>
        )
    }

    if (loading) {
        return (
            <div className="flex items-center justify-center py-12">
                <div className="animate-spin h-8 w-8 border-4 border-primary-600 border-t-transparent rounded-full"></div>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            <div>
                <h3 className="text-lg font-semibold">{tx('variants.productVariants', 'Product Variants')}</h3>
                <p className="text-sm text-gray-500">
                    {isMatrixMode
                        ? tx('variants.matrixDescription', 'Build option groups, generate combinations, and keep variant data aligned with the existing API.')
                        : tx('variants.manualDescription', 'Manage simple products with the legacy single-variant workflow.')}
                </p>
            </div>

            {error && (
                <div className="p-3 bg-red-100 border border-red-400 text-red-700 rounded">
                    {error}
                </div>
            )}
            {success && (
                <div className="p-3 bg-green-100 border border-green-400 text-green-700 rounded">
                    {success}
                </div>
            )}

            {isMatrixMode ? (
                <>
                    <VariantOptionsBuilder
                        optionGroups={optionGroups}
                        onChange={setOptionGroups}
                        combinationCount={generatedCombinations.length}
                        maxCombinations={MAX_COMBINATIONS}
                    />

                    {matrixRows.length > 0 ? (
                        <>
                            {legacyRows.length > 0 && (
                                <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                                        <p>
                                            {showLegacyRows
                                                ? (legacyRows.length === 1
                                                    ? tx('variants.legacyRowVisibleSingle', 'One legacy variant is visible for cleanup. It does not match the current option groups and may not appear correctly on the storefront.')
                                                    : tx('variants.legacyRowVisibleMultiple', 'Legacy variants are visible for cleanup. They do not match the current option groups and may not appear correctly on the storefront.'))
                                                : (legacyRows.length === 1
                                                    ? tx('variants.legacyRowHiddenSingle', 'One legacy variant is hidden to keep this matrix clean. Show it only if you want to edit or delete it.')
                                                    : tx('variants.legacyRowHiddenMultiple', 'Legacy variants are hidden to keep this matrix clean. Show them only if you want to edit or delete them.'))}
                                        </p>
                                        <button
                                            type="button"
                                            onClick={() => setShowLegacyRows(prev => !prev)}
                                            className="inline-flex items-center justify-center rounded-lg border border-amber-300 bg-white px-3 py-2 text-sm font-semibold text-amber-900 transition hover:bg-amber-100"
                                        >
                                            {showLegacyRows
                                                ? tx('variants.hideLegacyRows', 'Hide legacy variants')
                                                : tx('variants.showLegacyRows', 'Show legacy variants')}
                                        </button>
                                    </div>
                                </div>
                            )}

                            {visibleMatrixRows.length > 0 && (
                                <VariantMatrixTable
                                    productId={productId}
                                    rows={visibleMatrixRows}
                                    selectedKeys={selectedKeys}
                                    onToggleSelect={handleToggleSelect}
                                    onToggleSelectAll={handleToggleSelectAll}
                                    onRowChange={handleRowChange}
                                    onSetDefault={setDefaultKey}
                                    onEditFallback={handleEditVariant}
                                    onDeleteFallback={handleDeleteVariant}
                                    showFallbackActions={legacyRows.length > 0}
                                />
                            )}

                            {visibleMatrixRows.length === 0 && legacyRows.length > 0 && !showLegacyRows && (
                                <div className="rounded-2xl border border-dashed border-gray-300 bg-gray-50 px-4 py-6 text-center text-sm text-gray-500">
                                    {tx('variants.allLegacyRowsHidden', 'Only legacy variants exist right now. Use "Show legacy variants" if you want to review or delete them.')}
                                </div>
                            )}
                        </>
                    ) : (
                        <div className="text-center py-12 bg-gray-50 rounded-lg">
                            <p className="text-gray-500 mb-4">
                                {tx('variants.noVariants', 'No variants yet. Start by adding option groups or use the fallback modal.')}
                            </p>
                        </div>
                    )}

                    <VariantBulkActions
                        selectedCount={selectedKeys.size}
                        onApply={handleApplyBulkAction}
                        generationBlocked={generationBlocked}
                    />

                    {hasUnsavedChanges && (
                        <div className="sticky bottom-4 z-10">
                            <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-lg">
                                <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm font-medium text-amber-800">
                                    {tx('variants.unsavedMatrixBanner', 'Variant changes are not saved yet.')}
                                </div>
                                <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                                    <p className="text-sm text-gray-500">
                                        {tx('variants.saveMatrixHint', 'Save the matrix to persist option, price, image, and default variant updates.')}
                                    </p>
                                    <button
                                        type="button"
                                        onClick={handleSaveMatrix}
                                        disabled={savingMatrix || generationBlocked}
                                        className="inline-flex items-center justify-center rounded-lg bg-primary-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-50"
                                    >
                                        {savingMatrix ? tx('products.saving', 'Saving') : tx('variants.saveMatrix', 'Save Matrix')}
                                    </button>
                                </div>
                            </div>
                        </div>
                    )}
                </>
            ) : (
                <>
                    {variants.length > 0 ? (
                        <div className="space-y-3">
                            {variants.map((variant) => (
                                <div
                                    key={variant.id}
                                    className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm"
                                >
                                    <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                                        <div className="space-y-3">
                                            <div className="flex flex-wrap items-center gap-2">
                                                <h4 className="text-base font-semibold text-gray-900">
                                                    {getRowName(variant.attributes, variant.sku || variant.barcode || (t('variants.defaultVariant') || 'Default Variant'))}
                                                </h4>
                                                {variant.isDefault && (
                                                    <span className="rounded-full bg-primary-50 px-3 py-1 text-xs font-semibold text-primary-700">
                                                        {t('variants.default') || 'Default'}
                                                    </span>
                                                )}
                                                <span className={`rounded-full px-3 py-1 text-xs font-semibold ${variant.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-gray-100 text-gray-600'}`}>
                                                    {variant.isActive ? (t('common.active') || 'Active') : (t('common.inactive') || 'Inactive')}
                                                </span>
                                                <span className={`rounded-full px-3 py-1 text-xs font-semibold ${variant.totalStock > 0 ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'}`}>
                                                    {variant.totalStock > 0
                                                        ? `${t('variants.inStock') || 'In Stock'} (${variant.totalStock})`
                                                        : (t('variants.outOfStock') || 'Out of Stock')}
                                                </span>
                                            </div>

                                            {variant.attributes?.length > 0 && (
                                                <p className="text-sm text-gray-600">
                                                    {variant.attributes.map(attr => `${attr.name}: ${attr.value}`).join(', ')}
                                                </p>
                                            )}

                                            <div className="grid gap-3 text-sm text-gray-600 sm:grid-cols-2 xl:grid-cols-5">
                                                <div>
                                                    <span className="font-medium text-gray-900">{t('variants.sku') || 'SKU'}:</span> {variant.sku || '-'}
                                                </div>
                                                <div>
                                                    <span className="font-medium text-gray-900">{t('variants.barcode') || 'Barcode'}:</span> {variant.barcode || '-'}
                                                </div>
                                                <div>
                                                    <span className="font-medium text-gray-900">{t('variants.price') || 'POS Price'}:</span> {variant.price}
                                                </div>
                                                <div>
                                                    <span className="font-medium text-gray-900">{t('variants.onlinePrice') || 'Online Price'}:</span> {variant.onlinePrice ?? '-'}
                                                </div>
                                                <div>
                                                    <span className="font-medium text-gray-900">{t('variants.costPrice') || 'Cost Price'}:</span> {variant.costPrice}
                                                </div>
                                            </div>
                                        </div>

                                        <div className="flex gap-3">
                                            <button
                                                type="button"
                                                onClick={() => handleEditVariant(variant)}
                                                className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-gray-50"
                                            >
                                                {t('common.edit') || 'Edit'}
                                            </button>
                                            {!variant.isDefault && (
                                                <button
                                                    type="button"
                                                    onClick={() => handleDeleteVariant(variant)}
                                                    className="rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 transition hover:bg-red-50"
                                                >
                                                    {t('common.delete') || 'Delete'}
                                                </button>
                                            )}
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    ) : (
                        <div className="text-center py-12 bg-gray-50 rounded-lg">
                            <p className="text-gray-500">
                                {tx('variants.noVariants', 'No variants yet. Use the legacy workflow to add one.')}
                            </p>
                        </div>
                    )}

                    <div className="flex justify-start">
                        <button
                            type="button"
                            onClick={handleAddVariant}
                            className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-gray-50"
                        >
                            + {t('variants.addVariant') || 'Add Variant'}
                        </button>
                    </div>
                </>
            )}

            {showModal && !isMatrixMode && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl w-full max-w-2xl max-h-[90vh] overflow-y-auto m-4">
                        <div className="p-6">
                            <div className="flex justify-between items-center mb-6">
                                <h3 className="text-lg font-semibold">
                                    {editingVariant
                                        ? (t('variants.editVariant') || 'Edit Variant')
                                        : (t('variants.addVariant') || 'Add Variant')}
                                </h3>
                                <button
                                    type="button"
                                    onClick={() => setShowModal(false)}
                                    className="text-gray-400 hover:text-gray-600"
                                >
                                    x
                                </button>
                            </div>

                            <div className="space-y-6">
                                <div className="grid grid-cols-2 gap-4">
                                    <div>
                                        <label className="label">{t('variants.sku') || 'SKU'}</label>
                                        <input
                                            type="text"
                                            value={formData.sku}
                                            onChange={(e) => setFormData({ ...formData, sku: e.target.value })}
                                            className="input"
                                        />
                                    </div>
                                    <div>
                                        <label className="label">{t('variants.barcode') || 'Barcode'}</label>
                                        <input
                                            type="text"
                                            value={formData.barcode}
                                            onChange={(e) => setFormData({ ...formData, barcode: e.target.value })}
                                            className="input"
                                        />
                                    </div>
                                </div>

                                <div className="grid grid-cols-3 gap-4">
                                    <div>
                                        <label className="label">{t('variants.price') || 'POS Price'} *</label>
                                        <input
                                            type="number"
                                            step="0.01"
                                            value={formData.price}
                                            onChange={(e) => setFormData({ ...formData, price: e.target.value })}
                                            className="input"
                                        />
                                    </div>
                                    <div>
                                        <label className="label">{t('variants.onlinePrice') || 'Online Price'}</label>
                                        <input
                                            type="number"
                                            step="0.01"
                                            value={formData.onlinePrice}
                                            onChange={(e) => setFormData({ ...formData, onlinePrice: e.target.value })}
                                            className="input"
                                        />
                                    </div>
                                    <div>
                                        <label className="label">{t('variants.costPrice') || 'Cost Price'}</label>
                                        <input
                                            type="number"
                                            step="0.01"
                                            value={formData.costPrice}
                                            onChange={(e) => setFormData({ ...formData, costPrice: e.target.value })}
                                            className="input"
                                        />
                                    </div>
                                </div>

                                <div>
                                    <h4 className="font-medium mb-3">{t('variants.attributes') || 'Attributes'}</h4>
                                    {formData.attributes.length > 0 && (
                                        <div className="mb-4 space-y-2">
                                            {formData.attributes.map((attr, index) => (
                                                <div key={`${attr.name}-${attr.value}-${index}`} className="flex items-center gap-2 bg-gray-50 p-2 rounded">
                                                    <span className="font-medium text-sm">{attr.name}:</span>
                                                    <span className="text-sm text-gray-600">{attr.value}</span>
                                                    <button
                                                        type="button"
                                                        onClick={() => handleRemoveAttribute(index)}
                                                        className="ml-auto text-red-500 hover:text-red-700"
                                                    >
                                                        x
                                                    </button>
                                                </div>
                                            ))}
                                        </div>
                                    )}

                                    <div className="flex gap-2">
                                        <input
                                            type="text"
                                            placeholder={t('variants.attributeName') || 'Name'}
                                            value={newAttribute.name}
                                            onChange={(e) => setNewAttribute({ ...newAttribute, name: e.target.value })}
                                            className="input flex-1"
                                        />
                                        <input
                                            type="text"
                                            placeholder={t('variants.attributeValue') || 'Value'}
                                            value={newAttribute.value}
                                            onChange={(e) => setNewAttribute({ ...newAttribute, value: e.target.value })}
                                            className="input flex-1"
                                        />
                                        <button
                                            type="button"
                                            onClick={handleAddAttribute}
                                            className="btn-primary px-4"
                                            disabled={!newAttribute.name || !newAttribute.value}
                                        >
                                            {t('common.add') || 'Add'}
                                        </button>
                                    </div>
                                </div>

                                <div className="space-y-3">
                                    <label className="label">{tx('variants.variantImage', 'Variant Image')}</label>
                                    <div className="rounded-2xl border border-gray-200 bg-gray-50 p-4">
                                        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                                            <div>
                                                <p className="text-sm font-medium text-gray-900">
                                                    {formData.mediaId
                                                        ? tx('variants.galleryImageSelected', 'Gallery image selected')
                                                        : formData.imageUrl
                                                            ? tx('variants.legacyImagePreserved', 'Legacy image will be preserved')
                                                            : tx('variants.noImage', 'No variant image selected')}
                                                </p>
                                                <p className="mt-1 text-xs text-gray-500">
                                                    {tx('variants.variantImageHelp', 'Choose one image from the product media gallery.')}
                                                </p>
                                            </div>

                                            <div className="flex gap-2">
                                                <button
                                                    type="button"
                                                    onClick={() => setShowManualMediaPicker(true)}
                                                    className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-white"
                                                >
                                                    {formData.mediaId
                                                        ? tx('variants.changeImage', 'Change Image')
                                                        : tx('variants.selectImage', 'Select Image')}
                                                </button>
                                                {(formData.mediaId || formData.imageUrl) && (
                                                    <button
                                                        type="button"
                                                        onClick={() => setFormData((prev) => ({ ...prev, mediaId: null, imageUrl: '' }))}
                                                        className="rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 transition hover:bg-red-50"
                                                    >
                                                        {tx('common.clear', 'Clear')}
                                                    </button>
                                                )}
                                            </div>
                                        </div>
                                    </div>
                                </div>

                                <div>
                                    <label className="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            checked={formData.isActive}
                                            onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                            className="rounded"
                                        />
                                        <span className="text-sm font-medium text-gray-700">
                                            {t('variants.variantActive') || 'Variant is active'}
                                        </span>
                                    </label>
                                </div>

                                <div className="flex justify-end gap-3 pt-4 border-t">
                                    <button
                                        type="button"
                                        onClick={() => setShowModal(false)}
                                        className="btn-secondary"
                                    >
                                        {t('common.cancel') || 'Cancel'}
                                    </button>
                                    <button
                                        type="button"
                                        onClick={handleSaveVariant}
                                        className="btn-primary"
                                    >
                                        {editingVariant
                                            ? (t('common.save') || 'Save')
                                            : (t('variants.createVariant') || 'Create Variant')}
                                    </button>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            <VariantMediaPicker
                open={showManualMediaPicker}
                productId={productId}
                selectedMediaId={formData.mediaId ?? null}
                onClose={() => setShowManualMediaPicker(false)}
                onSelect={handleSelectManualMedia}
            />

            {confirmDelete && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl w-full max-w-md m-4 p-6">
                        <h3 className="text-lg font-semibold mb-4">
                            {t('variants.confirmDelete') || 'Confirm Delete'}
                        </h3>
                        <p className="text-gray-600 mb-4">
                            {t('variants.deleteWarning') || 'Are you sure you want to delete this variant?'}
                        </p>
                        {confirmDelete.totalStock > 0 && (
                            <div className="p-3 bg-yellow-50 border border-yellow-200 rounded mb-4">
                                <p className="text-yellow-800 text-sm">
                                    {t('variants.hasStock') || 'This variant has'} {confirmDelete.totalStock} {t('variants.unitsInStock') || 'units in stock.'}
                                </p>
                            </div>
                        )}
                        <div className="flex justify-end gap-3">
                            <button
                                type="button"
                                onClick={() => setConfirmDelete(null)}
                                className="btn-secondary"
                            >
                                {t('common.cancel') || 'Cancel'}
                            </button>
                            <button
                                type="button"
                                onClick={confirmDeleteVariant}
                                className="btn-danger"
                            >
                                {t('common.delete') || 'Delete'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
})

export default ProductVariantsTab
