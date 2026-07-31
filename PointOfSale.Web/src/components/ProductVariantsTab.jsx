import { forwardRef, useEffect, useImperativeHandle, useMemo, useState } from 'react'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useTranslation } from 'react-i18next'
import VariantOptionsBuilder from './VariantOptionsBuilder'
import VariantBulkActions from './VariantBulkActions'
import VariantMatrixTable from './VariantMatrixTable'
import VariantMediaPicker from './VariantMediaPicker'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'

const SAFE_COMBINATION_LIMIT = 100
const INFO_COMBINATION_LIMIT = 300
const WARNING_COMBINATION_LIMIT = 1000
const ATTRIBUTE_PRESETS = ['Color', 'Size', 'Material', 'Capacity', 'Flavor']
const VARIANT_TEMPLATES = [
    { id: 't-shirt', name: 'T-Shirt', attributes: ['Color', 'Size'] },
    { id: 'shoes', name: 'Shoes', attributes: ['Color', 'Size'] },
    { id: 'electronics', name: 'Electronics', attributes: ['Color', 'Storage'] },
    { id: 'food', name: 'Food', attributes: ['Flavor', 'Weight'] },
    { id: 'furniture', name: 'Furniture', attributes: ['Color', 'Material'] }
]

function normalizeText(value) {
    return (value || '').trim()
}

function normalizeKeyPart(value) {
    return normalizeText(value).toLowerCase()
}

function normalizeSkuPart(value) {
    return normalizeText(value)
        .replace(/[^a-z0-9]+/gi, '-')
        .replace(/^-+|-+$/g, '')
}

function normalizeToken(value) {
    return normalizeText(value)
        .replace(/[^a-z0-9]+/gi, '_')
        .replace(/^_+|_+$/g, '')
        .toUpperCase()
}

function createVariantAuthoringIdempotencyKey(productId, scope = 'matrix') {
    const randomPart = globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`
    return `variant-authoring:${productId}:${scope}:${randomPart}`
}

function createEmptyVariantForm(sortOrder = 0) {
    return {
        sku: '',
        barcode: '',
        price: '',
        onlinePrice: '',
        costPrice: '',
        inventory: '',
        weight: '',
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
            values: group.values.map(value => normalizeText(value))
        }))
    )
}

function exclusionRulesToSignature(excludedRules) {
    return JSON.stringify(
        excludedRules.map((rule) => ({
            conditions: (rule.conditions || [])
                .map(condition => ({
                    name: normalizeText(condition.name),
                    value: normalizeText(condition.value)
                }))
                .filter(condition => condition.name && condition.value)
                .sort((a, b) => normalizeKeyPart(a.name).localeCompare(normalizeKeyPart(b.name)))
        }))
    )
}

function normalizeOptionGroups(optionGroups) {
    return optionGroups
        .map((group, groupIndex) => ({
            ...group,
            name: normalizeText(group.name),
            values: (group.values || []).map(value => normalizeText(value)).filter(Boolean),
            sortOrder: groupIndex
        }))
}

function getOptionGroupValidation(optionGroups) {
    const groups = normalizeOptionGroups(optionGroups)
    const messages = []
    const duplicateValueMap = {}
    const normalizedNames = groups.map(group => normalizeKeyPart(group.name)).filter(Boolean)
    const hasDuplicateNames = normalizedNames.some((name, index) => normalizedNames.indexOf(name) !== index)

    groups.forEach((group) => {
        if (!group.name && group.values.length > 0) {
            messages.push('Every option group needs a name before variants can be generated.')
        }

        if (group.name && group.values.length === 0) {
            messages.push(`${group.name} needs at least one value before variants can be generated.`)
        }

        const seenValues = new Set()
        const duplicates = new Set()
        group.values.forEach((value) => {
            const normalizedValue = normalizeKeyPart(value)
            if (seenValues.has(normalizedValue)) {
                duplicates.add(normalizedValue)
            }
            seenValues.add(normalizedValue)
        })

        if (duplicates.size > 0) {
            duplicateValueMap[group.id] = duplicates
            messages.push(`${group.name || 'Option'} has duplicate values.`)
        }
    })

    if (hasDuplicateNames) {
        messages.push('Duplicate attribute names are not allowed.')
    }

    return {
        messages: [...new Set(messages)],
        duplicateValueMap,
        hasBlockingErrors: messages.length > 0
    }
}

function getCompletedOptionGroups(optionGroups) {
    return normalizeOptionGroups(optionGroups).filter(group => group.name && group.values.length > 0)
}

function ruleMatchesAttributes(rule, attributes) {
    const conditions = (rule.conditions || []).filter(condition => normalizeText(condition.name) && normalizeText(condition.value))

    if (conditions.length === 0) {
        return false
    }

    return conditions.every((condition) => (
        attributes.some(attribute =>
            normalizeKeyPart(attribute.name) === normalizeKeyPart(condition.name)
            && normalizeKeyPart(attribute.value) === normalizeKeyPart(condition.value)
        )
    ))
}

function isExcludedCombination(attributes, excludedRules) {
    return excludedRules.some(rule => ruleMatchesAttributes(rule, attributes))
}

function forEachCombination(optionGroups, excludedRules, visitor) {
    const completedGroups = getCompletedOptionGroups(optionGroups)

    if (!completedGroups.length) {
        return
    }

    const visit = (groupIndex, attributes) => {
        if (groupIndex >= completedGroups.length) {
            if (!isExcludedCombination(attributes, excludedRules)) {
                visitor(attributes)
            }
            return
        }

        const group = completedGroups[groupIndex]
        group.values.forEach((value, valueIndex) => {
            visit(groupIndex + 1, [
                ...attributes,
                {
                    name: group.name,
                    value,
                    sortOrder: groupIndex,
                    valueSortOrder: valueIndex
                }
            ])
        })
    }

    visit(0, [])
}

function countCombinations(optionGroups, excludedRules = []) {
    let count = 0
    forEachCombination(optionGroups, excludedRules, () => {
        count += 1
    })
    return count
}

function getCombinationRisk(count) {
    if (count > WARNING_COMBINATION_LIMIT) {
        return {
            level: 'confirm',
            label: 'Confirmation required',
            message: 'Large variant sets may impact editing performance. Confirm before generating this matrix.'
        }
    }

    if (count > INFO_COMBINATION_LIMIT) {
        return {
            level: 'warning',
            label: 'Large matrix',
            message: 'This many variants may impact editing performance.'
        }
    }

    if (count > SAFE_COMBINATION_LIMIT) {
        return {
            level: 'info',
            label: 'Moderate matrix',
            message: 'This variant set is larger than usual. Editing may feel slower on low-powered devices.'
        }
    }

    return {
        level: 'safe',
        label: 'Safe',
        message: 'This variant count is safe for interactive editing.'
    }
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

function generateCombinations(optionGroups, excludedRules = []) {
    const combinations = []

    forEachCombination(optionGroups, excludedRules, (attributes) => {
        combinations.push(attributes.map((attribute, index) => ({
            name: attribute.name,
            value: attribute.value,
            sortOrder: index
        })))
    })

    return combinations
}

function buildVariantPayload(row, sortOrder) {
    return {
        sku: normalizeText(row.sku) || null,
        barcode: normalizeText(row.barcode) || null,
        price: parseFloat(row.price) || 0,
        onlinePrice: row.onlinePrice !== '' ? parseFloat(row.onlinePrice) : null,
        costPrice: parseFloat(row.costPrice) || 0,
        weight: row.weight !== '' ? parseFloat(row.weight) : null,
        isActive: !!row.isActive,
        mediaId: row.mediaId || null,
        imageUrl: row.mediaId ? null : normalizeText(row.imageUrl) || null,
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
        weight: variant.weight == null ? null : Number(variant.weight),
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

function toAuthoringVariant(row, sortOrder) {
    return {
        variantId: row.id || null,
        sku: normalizeText(row.sku) || null,
        barcode: normalizeText(row.barcode) || null,
        price: parseFloat(row.price) || 0,
        onlinePrice: row.onlinePrice !== '' ? parseFloat(row.onlinePrice) : null,
        costPrice: parseFloat(row.costPrice) || 0,
        weight: row.weight !== '' ? parseFloat(row.weight) : null,
        mediaId: row.mediaId || null,
        imageUrl: row.mediaId ? null : normalizeText(row.imageUrl) || null,
        isActive: !!row.isActive,
        isDefault: !!row.isDefault,
        attributes: (row.attributes || []).reduce((acc, attribute) => {
            const name = normalizeText(attribute.name)
            if (name) {
                acc[name] = normalizeText(attribute.value)
            }
            return acc
        }, {}),
        sortOrder
    }
}

function productUnitsToAuthoringPayload(product) {
    return (product.units || product.productUnits || []).map((unit) => ({
        productUnitId: unit.id || unit.productUnitId || null,
        unitId: unit.unitId,
        conversionFactor: Number(unit.conversionFactor) || 1,
        purchasePrice: unit.purchasePrice == null ? null : Number(unit.purchasePrice),
        sellingPrice: unit.sellingPrice == null ? null : Number(unit.sellingPrice),
        barcode: unit.barcode || null,
        isDefault: !!unit.isDefault,
        isBaseUnit: !!unit.isBaseUnit
    })).filter(unit => unit.unitId)
}

function normalizePublishStatus(value) {
    if (typeof value === 'number' && Number.isFinite(value)) {
        return value
    }

    const normalizedValue = typeof value === 'string' ? value.trim().toLowerCase() : ''
    if (normalizedValue === 'draft') return 0
    if (normalizedValue === 'published') return 1
    if (normalizedValue === 'archived') return 2

    const parsedValue = Number.parseInt(value, 10)
    return Number.isFinite(parsedValue) ? parsedValue : 1
}

function buildProductAuthoringPayload(product, variants) {
    return {
        name: product.name,
        nameAr: product.nameAr || null,
        barcode: null,
        sku: null,
        description: product.description || null,
        descriptionAr: product.descriptionAr || null,
        categoryId: product.categoryId,
        purchasePrice: Number(product.purchasePrice) || 0,
        salePrice: Number(product.salePrice) || 0,
        onlinePrice: product.onlinePrice == null ? null : Number(product.onlinePrice),
        priceIncludesTax: !!product.priceIncludesTax,
        taxRate: Number(product.taxRate) || 0,
        unit: product.unit || 'Piece',
        minimumStockLevel: Number(product.minimumStockLevel) || 0,
        images: null,
        isActive: product.isActive !== false,
        trackStock: false,
        hasVariants: true,
        slug: product.slug || null,
        metaTitle: product.metaTitle || null,
        metaDescription: product.metaDescription || null,
        metaKeywords: product.metaKeywords || null,
        weight: product.weight == null ? null : Number(product.weight),
        length: product.length == null ? null : Number(product.length),
        width: product.width == null ? null : Number(product.width),
        height: product.height == null ? null : Number(product.height),
        weightUnit: product.weightUnit || 'kg',
        dimensionUnit: product.dimensionUnit || 'cm',
        publishStatus: normalizePublishStatus(product.publishStatus),
        isOnlineVisible: product.isOnlineVisible ?? true,
        isSearchable: product.isSearchable ?? true,
        isFeatured: product.isFeatured ?? false,
        lowStockThreshold: product.lowStockThreshold ?? null,
        allowBackorder: product.allowBackorder ?? false,
        brand: product.brand || null,
        manufacturer: product.manufacturer || null,
        tags: product.tags || null,
        features: product.features || [],
        faqs: product.faqs || [],
        aiGenerated: !!product.aiGenerated,
        aiMetadata: product.aiMetadata || null,
        videoUrl: product.videoUrl || null,
        productTemplate: product.productTemplate || null,
        productUnits: null,
        variants
    }
}

function getWarehouseQuantity(row, warehouseId) {
    if (!warehouseId) {
        return row.totalStock || 0
    }

    const stock = (row.stocks || []).find(item => item.warehouseId === warehouseId)
    return stock?.quantity ?? 0
}

function buildMatrixRow({ key, variant, attributes, draft, isDefault, index, selectedWarehouseId, isLegacy = false }) {
    const base = variant
        ? {
            id: variant.id,
            productId: variant.productId,
            sku: variant.sku || '',
            barcode: variant.barcode || '',
            price: variant.price?.toString() || '',
            onlinePrice: variant.onlinePrice?.toString() || '',
            costPrice: variant.costPrice?.toString() || '',
            weight: variant.weight?.toString() || '',
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
            weight: '',
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
        inventory: merged.inventory ?? getWarehouseQuantity(merged, selectedWarehouseId).toString(),
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
    const [parentProduct, setParentProduct] = useState(null)
    const [warehouses, setWarehouses] = useState([])
    const [selectedWarehouseId, setSelectedWarehouseId] = useState('')
    const [optionGroups, setOptionGroups] = useState([])
    const [rowDrafts, setRowDrafts] = useState({})
    const [deletedKeys, setDeletedKeys] = useState(new Set())
    const [selectedKeys, setSelectedKeys] = useState(new Set())
    const [defaultKey, setDefaultKey] = useState(null)
    const [error, setError] = useState('')
    const [success, setSuccess] = useState('')
    const [showLegacyRows, setShowLegacyRows] = useState(false)
    const [skuBuilder, setSkuBuilder] = useState({
        prefix: '',
        separator: '-',
        casing: 'upper',
        pattern: ''
    })
    const [excludedRules, setExcludedRules] = useState([])
    const [draftExclusionRule, setDraftExclusionRule] = useState({})
    const [confirmedGenerationSignature, setConfirmedGenerationSignature] = useState('')

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
            const [variantsResponse, productResponse, warehousesResponse] = await Promise.all([
                api.get(`/products/${productId}/variants`),
                api.get(`/products/${productId}`),
                api.get('/warehouses')
            ])
            const nextVariants = (variantsResponse.data || []).map(normalizeVariantForMatrix)
            const derivedGroups = reconstructOptionGroupsFromVariants(nextVariants)
            const currentDefault = nextVariants.find(variant => variant.isDefault)
            const nextWarehouses = Array.isArray(warehousesResponse.data)
                ? warehousesResponse.data
                : warehousesResponse.data?.items || []
            const nextProduct = productResponse.data || null

            setVariants(nextVariants)
            setParentProduct(nextProduct)
            setWarehouses(nextWarehouses)
            setSelectedWarehouseId(prev => prev || nextWarehouses.find(warehouse => warehouse.isDefault)?.id || nextWarehouses[0]?.id || '')
            setSkuBuilder(prev => ({
                ...prev,
                prefix: prev.prefix || normalizeSkuPart(nextProduct?.sku || nextProduct?.name || '')
            }))
            setOptionGroups(derivedGroups)
            setExcludedRules([])
            setDraftExclusionRule({})
            setConfirmedGenerationSignature('')
            setRowDrafts({})
            setDeletedKeys(new Set())
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
    const optionGroupValidation = useMemo(() => getOptionGroupValidation(optionGroups), [optionGroups])
    const generationSignature = useMemo(
        () => `${optionGroupsToSignature(optionGroups)}|${exclusionRulesToSignature(excludedRules)}`,
        [excludedRules, optionGroups]
    )
    const combinationCount = useMemo(
        () => optionGroupValidation.hasBlockingErrors ? 0 : countCombinations(optionGroups, excludedRules),
        [excludedRules, optionGroupValidation.hasBlockingErrors, optionGroups]
    )
    const combinationRisk = useMemo(() => getCombinationRisk(combinationCount), [combinationCount])
    const generationRequiresConfirmation = combinationCount > WARNING_COMBINATION_LIMIT
    const largeGenerationConfirmed = !generationRequiresConfirmation || confirmedGenerationSignature === generationSignature
    const canGenerateMatrixRows = !optionGroupValidation.hasBlockingErrors && largeGenerationConfirmed
    const generatedCombinations = useMemo(
        () => canGenerateMatrixRows ? generateCombinations(optionGroups, excludedRules) : [],
        [canGenerateMatrixRows, excludedRules, optionGroups]
    )
    const generationBlocked = optionGroupValidation.hasBlockingErrors || !largeGenerationConfirmed
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
                if (deletedKeys.has(key)) {
                    return
                }

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
                    index,
                    selectedWarehouseId
                }))
            })
        }

        variants
            .filter(variant => !matchedVariantIds.has(variant.id))
            .forEach((variant, index) => {
                const fallbackKey = getCombinationKeyFromVariant(variant)
                if (deletedKeys.has(fallbackKey)) {
                    return
                }

                rows.push(buildMatrixRow({
                    key: fallbackKey,
                    variant,
                    attributes: variant.attributes || [],
                    draft: rowDrafts[fallbackKey],
                    isDefault: defaultKey ? defaultKey === fallbackKey : variant.isDefault,
                    index: rows.length + index,
                    selectedWarehouseId,
                    isLegacy: generatedCombinations.length > 0
                }))
            })

        return rows
    }, [defaultKey, deletedKeys, generatedCombinations, generationBlocked, rowDrafts, selectedWarehouseId, variants])

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
            || deletedKeys.size > 0
            || optionGroupsToSignature(optionGroups) !== optionGroupsToSignature(originalOptionGroups)
            || exclusionRulesToSignature(excludedRules) !== '[]'
            || !!(defaultKey && matrixRows.find(row => row.original?.isDefault)?.key !== defaultKey)
    }, [defaultKey, deletedKeys, excludedRules, matrixRows, optionGroups, originalOptionGroups, rowDrafts])

    useEffect(() => {
        if (!hasUnsavedChanges) {
            return
        }

        const message = tx('variants.unsavedLeavePrompt', 'Unsaved changes detected. Leave anyway?')

        const handleBeforeUnload = (event) => {
            event.preventDefault()
            event.returnValue = message
            return message
        }

        const handleDocumentClick = (event) => {
            const anchor = event.target?.closest?.('a[href]')
            if (!anchor || anchor.target === '_blank' || anchor.hasAttribute('download')) {
                return
            }

            const href = anchor.getAttribute('href') || ''
            if (!href || href.startsWith('#') || href.startsWith('javascript:')) {
                return
            }

            if (!window.confirm(message)) {
                event.preventDefault()
                event.stopPropagation()
            }
        }

        window.addEventListener('beforeunload', handleBeforeUnload)
        document.addEventListener('click', handleDocumentClick, true)

        return () => {
            window.removeEventListener('beforeunload', handleBeforeUnload)
            document.removeEventListener('click', handleDocumentClick, true)
        }
    }, [hasUnsavedChanges, tx])

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

    const handleTogglePresetAttribute = (attributeName, checked) => {
        if (checked) {
            if (optionGroups.some(group => normalizeKeyPart(group.name) === normalizeKeyPart(attributeName))) {
                return
            }

            setOptionGroups(prev => [
                ...prev,
                {
                    id: `preset-${normalizeKeyPart(attributeName)}-${Date.now()}`,
                    name: attributeName,
                    values: []
                }
            ].slice(0, 3))
            return
        }

        setOptionGroups(prev => prev.filter(group => normalizeKeyPart(group.name) !== normalizeKeyPart(attributeName)))
    }

    const handleApplyTemplate = (template) => {
        setOptionGroups(template.attributes.map((attributeName, index) => ({
            id: `template-${template.id}-${normalizeKeyPart(attributeName)}-${index}`,
            name: attributeName,
            values: []
        })))
        setExcludedRules([])
        setDraftExclusionRule({})
        setConfirmedGenerationSignature('')
        setDeletedKeys(new Set())
        setSelectedKeys(new Set())
    }

    const updateDraftExclusionRule = (attributeName, value) => {
        setDraftExclusionRule(prev => ({
            ...prev,
            [attributeName]: value
        }))
    }

    const handleAddExclusionRule = () => {
        const conditions = getCompletedOptionGroups(optionGroups)
            .map(group => ({
                name: group.name,
                value: normalizeText(draftExclusionRule[group.name])
            }))
            .filter(condition => condition.name && condition.value)

        if (conditions.length < 2) {
            setError(tx('variants.exclusionNeedsTwoValues', 'Choose at least two attribute values to exclude a combination.'))
            setTimeout(() => setError(''), 4000)
            return
        }

        const nextRuleSignature = exclusionRulesToSignature([{ conditions }])
        if (excludedRules.some(rule => exclusionRulesToSignature([rule]) === nextRuleSignature)) {
            setDraftExclusionRule({})
            return
        }

        setExcludedRules(prev => [
            ...prev,
            {
                id: `exclude-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
                conditions
            }
        ])
        setDraftExclusionRule({})
        setConfirmedGenerationSignature('')
    }

    const handleRemoveExclusionRule = (ruleId) => {
        setExcludedRules(prev => prev.filter(rule => rule.id !== ruleId))
        setConfirmedGenerationSignature('')
    }

    const handleGenerateVariants = async () => {
        setError('')

        if (optionGroupValidation.hasBlockingErrors) {
            setError(optionGroupValidation.messages[0])
            return
        }

        if (generationRequiresConfirmation && confirmedGenerationSignature !== generationSignature) {
            const confirmed = await confirm({
                confirmLabel: tx('common.confirm', 'Confirm'),
                description: tx('variants.largeGenerationConfirm', 'A large number of variants may impact editing performance. Generate this matrix anyway?'),
                title: tx('variants.largeVariantMatrix', 'Large Variant Matrix'),
                variant: 'warning',
            })

            if (!confirmed) {
                return
            }
        }

        setConfirmedGenerationSignature(generationSignature)
    }

    const buildSkuForRow = (row, index) => {
        const separator = skuBuilder.separator || '-'
        const prefix = normalizeSkuPart(skuBuilder.prefix || parentProduct?.sku || parentProduct?.name || 'VARIANT')
        const attributes = row.attributes || []
        const tokenValues = attributes.reduce((acc, attribute) => {
            acc[normalizeToken(attribute.name)] = normalizeSkuPart(attribute.value)
            return acc
        }, {
            PREFIX: prefix,
            INDEX: `${index + 1}`
        })

        let sku = ''
        if (normalizeText(skuBuilder.pattern)) {
            sku = skuBuilder.pattern.replace(/\{([^}]+)\}/g, (_, token) => tokenValues[normalizeToken(token)] || '')
                .replace(/[-_.]{2,}/g, separator)
                .replace(/^[-_.]+|[-_.]+$/g, '')
        } else {
            const suffixParts = attributes
            .map(attribute => normalizeSkuPart(attribute.value))
            .filter(Boolean)
            sku = [prefix, ...suffixParts].filter(Boolean).join(separator) || `VARIANT${separator}${index + 1}`
        }

        if (skuBuilder.casing === 'lower') {
            sku = sku.toLowerCase()
        } else if (skuBuilder.casing === 'upper') {
            sku = sku.toUpperCase()
        }

        return sku
    }

    const rowValidationSummary = useMemo(() => {
        const summary = {
            generatedVariants: matrixRows.length,
            active: matrixRows.filter(row => row.isActive).length,
            inactive: matrixRows.filter(row => !row.isActive).length,
            missingPurchasePrice: matrixRows.filter(row => row.costPrice === '' || Number.isNaN(parseFloat(row.costPrice))).length,
            missingSellingPrice: matrixRows.filter(row => row.price === '' || Number.isNaN(parseFloat(row.price)) || parseFloat(row.price) <= 0).length,
            missingSku: matrixRows.filter(row => !normalizeText(row.sku)).length,
            duplicateSku: 0,
            duplicateBarcode: 0,
            duplicateCombinations: 0,
            negativeInventory: matrixRows.filter(row => row.inventory !== '' && !Number.isNaN(parseFloat(row.inventory)) && parseFloat(row.inventory) < 0).length,
            negativeWeight: matrixRows.filter(row => row.weight !== '' && !Number.isNaN(parseFloat(row.weight)) && parseFloat(row.weight) < 0).length
        }
        const issues = {}
        const findDuplicates = (rows, getValue) => {
            const counts = new Map()
            rows.forEach((row) => {
                const value = getValue(row)
                if (!value) {
                    return
                }
                counts.set(value, (counts.get(value) || 0) + 1)
            })
            return counts
        }
        const skuCounts = findDuplicates(matrixRows, row => normalizeText(row.sku).toLowerCase())
        const barcodeCounts = findDuplicates(matrixRows, row => normalizeText(row.barcode).toLowerCase())
        const combinationCounts = findDuplicates(matrixRows, row => attributesToKey(row.attributes))

        matrixRows.forEach((row) => {
            const rowIssues = {}
            const sku = normalizeText(row.sku).toLowerCase()
            const barcode = normalizeText(row.barcode).toLowerCase()
            const combinationKey = attributesToKey(row.attributes)

            if (sku && skuCounts.get(sku) > 1) {
                rowIssues.sku = true
            }
            if (barcode && barcodeCounts.get(barcode) > 1) {
                rowIssues.barcode = true
            }
            if (combinationKey && combinationCounts.get(combinationKey) > 1) {
                rowIssues.combination = true
            }
            if (row.inventory !== '' && !Number.isNaN(parseFloat(row.inventory)) && parseFloat(row.inventory) < 0) {
                rowIssues.inventory = true
            }
            if (row.weight !== '' && !Number.isNaN(parseFloat(row.weight)) && parseFloat(row.weight) < 0) {
                rowIssues.weight = true
            }

            if (Object.keys(rowIssues).length > 0) {
                issues[row.key] = rowIssues
            }
        })

        summary.duplicateSku = [...skuCounts.values()].filter(count => count > 1).reduce((total, count) => total + count, 0)
        summary.duplicateBarcode = [...barcodeCounts.values()].filter(count => count > 1).reduce((total, count) => total + count, 0)
        summary.duplicateCombinations = [...combinationCounts.values()].filter(count => count > 1).reduce((total, count) => total + count, 0)

        return { summary, issues }
    }, [matrixRows])

    const skuPreviewRows = useMemo(
        () => matrixRows.slice(0, 2).map((row, index) => buildSkuForRow(row, index)),
        [matrixRows, skuBuilder]
    )

    const validateMatrixRows = (rows) => {
        if (optionGroupValidation.hasBlockingErrors) {
            return optionGroupValidation.messages[0]
        }

        if (generationRequiresConfirmation && confirmedGenerationSignature !== generationSignature) {
            return tx('variants.largeGenerationRequiresConfirmation', 'Confirm the large variant generation before saving.')
        }

        if (!rows.length) {
            return tx('variants.noVariants', 'No variants to save.')
        }

        const duplicateCombination = rows.some((row, index) => (
            rows.findIndex(candidate => candidate.key === row.key) !== index
        ))
        if (duplicateCombination) {
            return tx('variants.duplicateCombination', 'Duplicate variant combinations are not allowed.')
        }

        const duplicateSku = rows
            .map(row => normalizeText(row.sku).toLowerCase())
            .filter(Boolean)
            .some((sku, index, skus) => skus.indexOf(sku) !== index)
        if (duplicateSku) {
            return tx('variants.duplicateSku', 'Duplicate SKU values are not allowed.')
        }

        const duplicateBarcode = rows
            .map(row => normalizeText(row.barcode).toLowerCase())
            .filter(Boolean)
            .some((barcode, index, barcodes) => barcodes.indexOf(barcode) !== index)
        if (duplicateBarcode) {
            return tx('variants.duplicateBarcode', 'Duplicate barcode values are not allowed.')
        }

        if (rows.some(row => row.costPrice === '' || Number.isNaN(parseFloat(row.costPrice)))) {
            return tx('variants.purchasePriceRequired', 'Each variant requires a purchase price.')
        }

        if (rows.some(row => row.price === '' || Number.isNaN(parseFloat(row.price)) || parseFloat(row.price) <= 0)) {
            return tx('variants.priceRequired', 'Each variant requires a valid selling price.')
        }

        if (rows.some(row => row.inventory !== '' && (Number.isNaN(parseFloat(row.inventory)) || parseFloat(row.inventory) < 0))) {
            return tx('variants.invalidStockQuantity', 'Inventory cannot be negative.')
        }

        if (rows.some(row => row.weight !== '' && (Number.isNaN(parseFloat(row.weight)) || parseFloat(row.weight) < 0))) {
            return tx('variants.invalidWeight', 'Weight cannot be negative.')
        }

        return null
    }

    const handleApplyBulkAction = async ({ type, value, warehouseId, reason }) => {
        setError('')

        const selectedRows = matrixRows.filter(row => selectedKeys.has(row.key))
        if (!selectedRows.length) {
            return
        }

        if (type === 'set-price' || type === 'set-cost-price' || type === 'set-inventory' || type === 'set-weight') {
            const numericValue = parseFloat(value)
            if (Number.isNaN(numericValue) || numericValue < 0) {
                setError(tx('variants.invalidBulkValue', 'Enter a non-negative value.'))
                return
            }

            const field = {
                'set-price': 'price',
                'set-cost-price': 'costPrice',
                'set-inventory': 'inventory',
                'set-weight': 'weight'
            }[type]

            setRowDrafts(prev => {
                const next = { ...prev }
                selectedRows.forEach((row) => {
                    next[row.key] = { ...(next[row.key] || {}), [field]: value }
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

        if (type === 'set-image') {
            setRowDrafts(prev => {
                const next = { ...prev }
                selectedRows.forEach((row) => {
                    next[row.key] = {
                        ...(next[row.key] || {}),
                        mediaId: value || null,
                        imageUrl: ''
                    }
                })
                return next
            })
            return
        }

        if (type === 'generate-sku') {
            setRowDrafts(prev => {
                const next = { ...prev }
                selectedRows.forEach((row, index) => {
                    next[row.key] = { ...(next[row.key] || {}), sku: buildSkuForRow(row, index) }
                })
                return next
            })
            return
        }

        if (type === 'generate-barcode') {
            try {
                const generated = []
                for (const row of selectedRows) {
                    const response = await api.post('/products/generate-barcode?type=EAN13')
                    generated.push([row.key, response.data?.barcode || ''])
                }

                setRowDrafts(prev => {
                    const next = { ...prev }
                    generated.forEach(([key, barcode]) => {
                        if (barcode) {
                            next[key] = { ...(next[key] || {}), barcode }
                        }
                    })
                    return next
                })
            } catch (err) {
                console.error('Failed to generate barcodes', err)
                setError(err.response?.data?.message || tx('products.failedToGenerateBarcode', 'Failed to generate barcode'))
            }
            return
        }

        if (type === 'delete-selected') {
            setDeletedKeys(prev => new Set([...prev, ...selectedRows.map(row => row.key)]))
            setSelectedKeys(new Set())
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
                    }, {
                        headers: {
                            'Idempotency-Key': createVariantAuthoringIdempotencyKey(productId, `stock:${row.id}`)
                        }
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

        const validationError = validateMatrixRows(matrixRows)
        if (validationError) {
            setError(validationError)
            return false
        }

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
            const productResponse = parentProduct ? { data: parentProduct } : await api.get(`/products/${productId}`)
            const product = productResponse.data
            const authoringVariants = matrixRows.map((row, index) => toAuthoringVariant({
                ...row,
                isDefault: (defaultKey || matrixRows[0]?.key) === row.key
            }, index))
            const productData = buildProductAuthoringPayload(product, authoringVariants)
            const rowVersion = product.rowVersion || product.RowVersion || null
            const requestOptions = {
                headers: {
                    'Idempotency-Key': createVariantAuthoringIdempotencyKey(productId)
                }
            }

            if (rowVersion) {
                requestOptions.headers['If-Match'] = `"${rowVersion}"`
            }

            const savedProductResponse = await api.put(`/products/${productId}`, productData, requestOptions)
            setParentProduct(savedProductResponse.data || product)

            const inventoryRows = matrixRows.filter(row => Object.prototype.hasOwnProperty.call(rowDrafts[row.key] || {}, 'inventory'))
            if (selectedWarehouseId && inventoryRows.length > 0) {
                const latestVariantsResponse = await api.get(`/products/${productId}/variants`)
                const latestByKey = new Map(
                    (latestVariantsResponse.data || [])
                        .map(normalizeVariantForMatrix)
                        .map(variant => [getCombinationKeyFromVariant(variant), variant])
                )

                for (const row of inventoryRows) {
                    const quantity = parseFloat(row.inventory)
                    if (Number.isNaN(quantity) || quantity < 0) {
                        continue
                    }

                    const latestVariant = row.id
                        ? (latestVariantsResponse.data || []).find(variant => variant.id === row.id)
                        : latestByKey.get(row.key)

                    if (!latestVariant?.id) {
                        continue
                    }

                    await api.put(`/products/${productId}/variants/${latestVariant.id}/stock/${selectedWarehouseId}`, {
                        quantity,
                        reason: 'Variant authoring inventory update'
                    }, {
                        headers: {
                            'Idempotency-Key': createVariantAuthoringIdempotencyKey(productId, `stock:${latestVariant.id}`)
                        }
                    })
                }
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
        hasPendingChanges() {
            return isMatrixMode && hasUnsavedChanges
        },
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
                    <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
                        <div className="mb-3">
                            <h4 className="text-sm font-semibold text-gray-900">
                                {tx('variants.variantTemplates', 'Variant Templates')}
                            </h4>
                            <p className="text-sm text-gray-500">
                                {tx('variants.variantTemplatesHint', 'Start from a common attribute structure, then edit values freely.')}
                            </p>
                        </div>
                        <div className="flex flex-wrap gap-2">
                            {VARIANT_TEMPLATES.map((template) => (
                                <button
                                    key={template.id}
                                    type="button"
                                    onClick={() => handleApplyTemplate(template)}
                                    className="rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-primary-300 hover:bg-primary-50 hover:text-primary-700"
                                >
                                    {template.name}
                                </button>
                            ))}
                        </div>
                    </div>

                    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
                        <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
                            <div className="mb-3">
                                <h4 className="text-sm font-semibold text-gray-900">
                                    {tx('variants.chooseAttributes', 'Choose Attributes')}
                                </h4>
                                <p className="text-sm text-gray-500">
                                    {tx('variants.chooseAttributesHint', 'Select common attributes, then add values in the builder below.')}
                                </p>
                            </div>
                            <div className="flex flex-wrap gap-2">
                                {ATTRIBUTE_PRESETS.map((attributeName) => {
                                    const checked = optionGroups.some(group => normalizeKeyPart(group.name) === normalizeKeyPart(attributeName))

                                    return (
                                        <label
                                            key={attributeName}
                                            className={`inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm font-medium transition ${
                                                checked
                                                    ? 'border-primary-300 bg-primary-50 text-primary-700'
                                                    : 'border-gray-200 bg-white text-gray-700 hover:border-gray-300'
                                            }`}
                                        >
                                            <input
                                                type="checkbox"
                                                checked={checked}
                                                onChange={(e) => handleTogglePresetAttribute(attributeName, e.target.checked)}
                                                className="rounded"
                                            />
                                            {attributeName}
                                        </label>
                                    )
                                })}
                            </div>
                        </div>

                        <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
                            <div className="mb-3">
                                <h4 className="text-sm font-semibold text-gray-900">
                                    {tx('variants.skuBuilder', 'SKU Builder')}
                                </h4>
                                <p className="text-sm text-gray-500">
                                    {tx('variants.skuBuilderHint', 'Generate selected row SKUs from the parent prefix and attributes.')}
                                </p>
                            </div>
                            <div className="grid gap-3">
                                <input
                                    type="text"
                                    value={skuBuilder.prefix}
                                    onChange={(e) => setSkuBuilder(prev => ({ ...prev, prefix: e.target.value }))}
                                    className="input"
                                    placeholder={tx('variants.skuPrefix', 'Prefix')}
                                />
                                <div className="grid grid-cols-2 gap-2">
                                    <select
                                        value={skuBuilder.separator}
                                        onChange={(e) => setSkuBuilder(prev => ({ ...prev, separator: e.target.value }))}
                                        className="input"
                                    >
                                        <option value="-">-</option>
                                        <option value="_">_</option>
                                        <option value=".">.</option>
                                    </select>
                                    <select
                                        value={skuBuilder.casing}
                                        onChange={(e) => setSkuBuilder(prev => ({ ...prev, casing: e.target.value }))}
                                        className="input"
                                    >
                                        <option value="upper">{tx('variants.uppercase', 'Uppercase')}</option>
                                        <option value="lower">{tx('variants.lowercase', 'Lowercase')}</option>
                                        <option value="preserve">{tx('variants.preserveCase', 'Preserve')}</option>
                                    </select>
                                </div>
                                <input
                                    type="text"
                                    value={skuBuilder.pattern}
                                    onChange={(e) => setSkuBuilder(prev => ({ ...prev, pattern: e.target.value }))}
                                    className="input"
                                    placeholder={tx('variants.skuPatternPlaceholder', 'Pattern, e.g. TSHIRT-{COLOR}-{SIZE}')}
                                />
                                <div className="rounded-xl border border-gray-200 bg-gray-50 px-3 py-2">
                                    <div className="text-xs font-semibold uppercase tracking-wide text-gray-500">
                                        {tx('variants.skuPreview', 'SKU Preview')}
                                    </div>
                                    <div className="mt-1 space-y-1 font-mono text-sm text-gray-700">
                                        {skuPreviewRows.length > 0
                                            ? skuPreviewRows.map((preview, index) => (
                                                <div key={`${preview}-${index}`}>{preview}</div>
                                            ))
                                            : <div>{tx('variants.skuPreviewEmpty', 'Add attributes to preview SKUs.')}</div>}
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>

                    <VariantOptionsBuilder
                        optionGroups={optionGroups}
                        onChange={setOptionGroups}
                        combinationCount={combinationCount}
                        risk={combinationRisk}
                        duplicateValueMap={optionGroupValidation.duplicateValueMap}
                        validationMessages={optionGroupValidation.messages}
                        safeLimit={SAFE_COMBINATION_LIMIT}
                        infoLimit={INFO_COMBINATION_LIMIT}
                        warningLimit={WARNING_COMBINATION_LIMIT}
                    />

                    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
                        <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
                            <div className="mb-3 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                                <div>
                                    <h4 className="text-sm font-semibold text-gray-900">
                                        {tx('variants.combinationRules', 'Combination Rules')}
                                    </h4>
                                    <p className="text-sm text-gray-500">
                                        {tx('variants.combinationRulesHint', 'Exclude combinations before they appear in the matrix.')}
                                    </p>
                                </div>
                                <button
                                    type="button"
                                    onClick={handleGenerateVariants}
                                    disabled={combinationCount === 0 || optionGroupValidation.hasBlockingErrors}
                                    className="rounded-lg bg-primary-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-50"
                                >
                                    {tx('variants.generateVariantCount', 'Generate {{count}} Variants').replace('{{count}}', combinationCount)}
                                </button>
                            </div>

                            <div className="grid gap-3 md:grid-cols-3">
                                {getCompletedOptionGroups(optionGroups).map((group) => (
                                    <label key={group.id} className="space-y-1">
                                        <span className="label">{group.name}</span>
                                        <select
                                            value={draftExclusionRule[group.name] || ''}
                                            onChange={(e) => updateDraftExclusionRule(group.name, e.target.value)}
                                            className="input"
                                        >
                                            <option value="">{tx('variants.anyValue', 'Any value')}</option>
                                            {group.values.map((value, index) => (
                                                <option key={`${group.id}-${value}-${index}`} value={value}>
                                                    {value}
                                                </option>
                                            ))}
                                        </select>
                                    </label>
                                ))}
                            </div>
                            <div className="mt-3 flex flex-wrap items-center gap-2">
                                <button
                                    type="button"
                                    onClick={handleAddExclusionRule}
                                    disabled={getCompletedOptionGroups(optionGroups).length < 2}
                                    className="rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                                >
                                    {tx('variants.addExclusionRule', 'Add Exclusion')}
                                </button>
                                {excludedRules.map((rule) => (
                                    <span key={rule.id} className="inline-flex items-center gap-2 rounded-full bg-amber-50 px-3 py-1 text-sm font-medium text-amber-800">
                                        {rule.conditions.map(condition => `${condition.name}: ${condition.value}`).join(' x ')}
                                        <button
                                            type="button"
                                            onClick={() => handleRemoveExclusionRule(rule.id)}
                                            className="text-amber-700 transition hover:text-amber-900"
                                        >
                                            x
                                        </button>
                                    </span>
                                ))}
                            </div>
                        </div>

                        <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
                            <h4 className="text-sm font-semibold text-gray-900">
                                {tx('variants.workspaceStatistics', 'Workspace Statistics')}
                            </h4>
                            <div className="mt-3 grid grid-cols-2 gap-3 text-sm">
                                {[
                                    [tx('variants.generatedVariants', 'Generated Variants'), rowValidationSummary.summary.generatedVariants],
                                    [tx('common.active', 'Active'), rowValidationSummary.summary.active],
                                    [tx('common.inactive', 'Inactive'), rowValidationSummary.summary.inactive],
                                    [tx('variants.missingPurchasePrice', 'Missing Purchase Price'), rowValidationSummary.summary.missingPurchasePrice],
                                    [tx('variants.missingSellingPrice', 'Missing Selling Price'), rowValidationSummary.summary.missingSellingPrice],
                                    [tx('variants.missingSku', 'Missing SKU'), rowValidationSummary.summary.missingSku],
                                    [tx('variants.duplicateSkuShort', 'Duplicate SKU'), rowValidationSummary.summary.duplicateSku],
                                    [tx('variants.duplicateBarcodeShort', 'Duplicate Barcode'), rowValidationSummary.summary.duplicateBarcode],
                                    [tx('variants.duplicateCombinationsShort', 'Duplicate Combinations'), rowValidationSummary.summary.duplicateCombinations]
                                ].map(([label, value]) => (
                                    <div key={label} className="rounded-xl border border-gray-200 bg-gray-50 px-3 py-2">
                                        <div className="text-xs text-gray-500">{label}</div>
                                        <div className="text-lg font-semibold text-gray-900">{value}</div>
                                    </div>
                                ))}
                            </div>
                            {(optionGroupValidation.messages.length > 0 || Object.keys(rowValidationSummary.issues).length > 0) && (
                                <div className="mt-3 rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                                    <div className="font-semibold">{tx('variants.liveValidation', 'Live Validation')}</div>
                                    {optionGroupValidation.messages.slice(0, 3).map((message) => (
                                        <div key={message} className="mt-1">{message}</div>
                                    ))}
                                    {rowValidationSummary.summary.duplicateSku > 0 && <div className="mt-1">{tx('variants.duplicateSku', 'Duplicate SKU values are not allowed.')}</div>}
                                    {rowValidationSummary.summary.duplicateBarcode > 0 && <div className="mt-1">{tx('variants.duplicateBarcode', 'Duplicate barcode values are not allowed.')}</div>}
                                    {rowValidationSummary.summary.negativeInventory > 0 && <div className="mt-1">{tx('variants.invalidStockQuantity', 'Inventory cannot be negative.')}</div>}
                                    {rowValidationSummary.summary.negativeWeight > 0 && <div className="mt-1">{tx('variants.invalidWeight', 'Weight cannot be negative.')}</div>}
                                </div>
                            )}
                        </div>
                    </div>

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
                                    rowIssues={rowValidationSummary.issues}
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
                        productId={productId}
                        selectedCount={selectedKeys.size}
                        onApply={handleApplyBulkAction}
                        generationBlocked={generationBlocked}
                        warehouses={warehouses}
                        selectedWarehouseId={selectedWarehouseId}
                        onWarehouseChange={setSelectedWarehouseId}
                        skuBuilder={skuBuilder}
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
