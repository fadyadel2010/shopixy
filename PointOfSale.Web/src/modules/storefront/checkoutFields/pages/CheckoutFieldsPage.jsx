import { useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { closestCenter, DndContext, PointerSensor, useSensor, useSensors } from '@dnd-kit/core'
import { arrayMove, SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { Archive, ArrowDown, ArrowUp, GripVertical, Loader2, Pencil, Plus } from 'lucide-react'

import { useToast } from '../../../../components/Toast'
import {
    AuthoringButton,
    AuthoringConfirmDialog,
    AuthoringDialog,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
} from '../../authoringPlatform'
import { useCheckoutFields } from '../hooks/useCheckoutFields'
import {
    buildUploadMimeTypes,
    getDefaultUploadMimeTypes,
    getKnownUploadMimeTypes,
    getUploadFormatOptions,
    getUploadFormatSelection,
    normalizeUploadMimeTypes,
} from '../uploadFormatOptions'

const CUSTOM_FIELD_TYPES = ['TEXT', 'TEXTAREA', 'NUMBER', 'EMAIL', 'PHONE', 'SELECT', 'RADIO', 'CHECKBOX', 'DATE', 'IMAGE', 'FILE', 'MULTI_IMAGE']
const UPLOAD_FIELD_TYPES = new Set(['IMAGE', 'FILE', 'MULTI_IMAGE'])
const OPTION_FIELD_TYPES = new Set(['SELECT', 'RADIO'])
const TEXT_CONFIG_TYPES = new Set(['TEXT', 'TEXTAREA', 'EMAIL', 'PHONE'])
const EMPTY_OPERATORS = new Set(['IS_EMPTY', 'IS_NOT_EMPTY'])

const emptyDraft = {
    id: null,
    fieldKind: 'CUSTOM',
    fieldType: 'TEXT',
    label: '',
    helpText: '',
    isEnabled: true,
    isRequired: false,
    configuration: { schemaVersion: 1 },
}

const normalizeType = (value) => String(value || 'TEXT').toUpperCase()
const isSystemField = (field) => String(field?.fieldKind || '').toUpperCase() === 'SYSTEM'

const normalizeOptionValue = (value) => String(value || '').trim()
const newRuleId = () => `rule_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`

const makeDraft = (field = null) => {
    if (!field) {
        return {
            ...emptyDraft,
            configuration: { schemaVersion: 1 },
        }
    }

    return {
        id: field.id,
        fieldKind: field.fieldKind,
        fieldType: normalizeType(field.fieldType),
        label: field.label || '',
        helpText: field.helpText || '',
        isEnabled: Boolean(field.isEnabled),
        isRequired: Boolean(field.isRequired),
        configuration: {
            schemaVersion: 1,
            ...(field.configuration || {}),
            options: (field.configuration?.options || []).map((option) => ({
                value: option.value || '',
                label: option.label || option.value || '',
            })),
            conditionalRules: (field.configuration?.conditionalRules || []).map((rule) => ({
                ruleId: rule.ruleId || newRuleId(),
                conditionMode: rule.conditionMode || 'ALL',
                conditions: (rule.conditions || []).map((condition) => ({
                    sourceFieldId: condition.sourceFieldId || '',
                    operator: condition.operator || 'EQUALS',
                    comparisonValue: condition.comparisonValue || '',
                })),
                actions: rule.actions || [],
            })),
            ...(Array.isArray(field.configuration?.allowedMimeTypes)
                ? { allowedMimeTypes: normalizeUploadMimeTypes(field.configuration.allowedMimeTypes) }
                : {}),
            maxFileSizeMb: field.configuration?.maxFileSize
                ? field.configuration.maxFileSize / 1024 / 1024
                : '',
        },
    }
}

const toNullableNumber = (value) => {
    if (value === '' || value == null) return null
    const next = Number(value)
    return Number.isFinite(next) ? next : null
}

const buildConfiguration = (draft) => {
    const fieldType = normalizeType(draft.fieldType)
    const source = draft.configuration || {}
    const configuration = { schemaVersion: 1 }

    if (TEXT_CONFIG_TYPES.has(fieldType)) {
        const minLength = toNullableNumber(source.minLength)
        const maxLength = toNullableNumber(source.maxLength)
        if (minLength != null) configuration.minLength = minLength
        if (maxLength != null) configuration.maxLength = maxLength
    }

    if (fieldType === 'NUMBER') {
        const min = toNullableNumber(source.min)
        const max = toNullableNumber(source.max)
        if (min != null) configuration.min = min
        if (max != null) configuration.max = max
    }

    if (OPTION_FIELD_TYPES.has(fieldType)) {
        configuration.options = (source.options || []).map((option) => ({
            value: normalizeOptionValue(option.value),
            label: String(option.label || '').trim(),
        }))
    }

    if (UPLOAD_FIELD_TYPES.has(fieldType)) {
        const hasConfiguredMimeTypes = Array.isArray(source.allowedMimeTypes)
        const configuredMimeTypes = normalizeUploadMimeTypes(source.allowedMimeTypes)
        const allowedMimeTypes = hasConfiguredMimeTypes
            ? configuredMimeTypes
            : getDefaultUploadMimeTypes(fieldType)
        const maxFileSizeMb = toNullableNumber(source.maxFileSizeMb)
        const maxFiles = toNullableNumber(source.maxFiles)
        if (allowedMimeTypes.length > 0) configuration.allowedMimeTypes = allowedMimeTypes
        if (maxFileSizeMb != null) configuration.maxFileSize = Math.round(maxFileSizeMb * 1024 * 1024)
        if (fieldType === 'MULTI_IMAGE' && maxFiles != null) configuration.maxFiles = maxFiles
        if (fieldType !== 'MULTI_IMAGE') configuration.maxFiles = 1
    }

    const conditionalRules = (source.conditionalRules || [])
        .map((rule) => ({
            ruleId: rule.ruleId || newRuleId(),
            conditionMode: rule.conditionMode || 'ALL',
            conditions: (rule.conditions || [])
                .map((condition) => ({
                    sourceFieldId: condition.sourceFieldId,
                    operator: String(condition.operator || '').toUpperCase(),
                    comparisonValue: EMPTY_OPERATORS.has(String(condition.operator || '').toUpperCase()) ? null : String(condition.comparisonValue || '').trim(),
                }))
                .filter((condition) => condition.sourceFieldId && condition.operator),
            actions: (rule.actions || []).map((action) => String(action || '').toUpperCase()).filter(Boolean),
        }))
        .filter((rule) => rule.conditions.length > 0 && rule.actions.length > 0)
    if (conditionalRules.length > 0) configuration.conditionalRules = conditionalRules

    return configuration
}

const buildPayload = (draft, { includeType }) => {
    const payload = {
        label: draft.label.trim(),
        helpText: draft.helpText.trim(),
        isEnabled: draft.isEnabled,
        isRequired: draft.isRequired,
        configuration: buildConfiguration(draft),
    }

    if (includeType) {
        payload.fieldType = normalizeType(draft.fieldType)
    }

    return payload
}

const validateDraft = (draft, t) => {
    const errors = []
    const fieldType = normalizeType(draft.fieldType)
    const configuration = buildConfiguration(draft)

    if (!draft.label.trim()) {
        errors.push(t('checkoutFields.validation.labelRequired', 'Label is required.'))
    }

    if (!draft.isEnabled && draft.isRequired) {
        errors.push(t('checkoutFields.validation.requiredMustBeEnabled', 'Required fields must be enabled.'))
    }

    if (TEXT_CONFIG_TYPES.has(fieldType)) {
        if ((configuration.minLength ?? 0) < 0 || (configuration.maxLength ?? 0) < 0) {
            errors.push(t('checkoutFields.validation.lengthPositive', 'Length values cannot be negative.'))
        }
        if (configuration.minLength != null && configuration.maxLength != null && configuration.minLength > configuration.maxLength) {
            errors.push(t('checkoutFields.validation.minLengthBeforeMax', 'Minimum length cannot exceed maximum length.'))
        }
    }

    if (fieldType === 'NUMBER' && configuration.min != null && configuration.max != null && configuration.min > configuration.max) {
        errors.push(t('checkoutFields.validation.minBeforeMax', 'Minimum value cannot exceed maximum value.'))
    }

    if (OPTION_FIELD_TYPES.has(fieldType)) {
        const values = configuration.options || []
        if (values.length === 0) {
            errors.push(t('checkoutFields.validation.optionsRequired', 'Select and radio fields require at least one option.'))
        }
        const normalized = values.map((option) => option.value.toLowerCase())
        if (values.some((option) => !option.value || !option.label)) {
            errors.push(t('checkoutFields.validation.optionRequired', 'Option value and label cannot be empty.'))
        }
        if (new Set(normalized).size !== normalized.length) {
            errors.push(t('checkoutFields.validation.optionDuplicate', 'Option values must be unique.'))
        }
    }

    if (UPLOAD_FIELD_TYPES.has(fieldType)) {
        const allowedMimeTypes = configuration.allowedMimeTypes || []
        const knownMimeTypes = getKnownUploadMimeTypes(fieldType)
        const knownSelectedCount = allowedMimeTypes.filter((mimeType) => knownMimeTypes.has(mimeType)).length
        if (knownSelectedCount === 0) {
            errors.push(fieldType === 'FILE'
                ? t('checkoutFields.validation.fileFormatRequired', 'Select at least one supported file type.')
                : t('checkoutFields.validation.imageFormatRequired', 'Select at least one supported image format.'))
        }
        if (new Set(allowedMimeTypes).size !== allowedMimeTypes.length) {
            errors.push(t('checkoutFields.validation.mimeTypeDuplicate', 'Upload formats must not contain duplicates.'))
        }
        if ((configuration.maxFileSize ?? 1) <= 0) {
            errors.push(t('checkoutFields.validation.fileSizePositive', 'Maximum file size must be greater than zero.'))
        }
        if (fieldType === 'MULTI_IMAGE' && ((configuration.maxFiles ?? 1) <= 0 || (configuration.maxFiles ?? 1) > 50)) {
            errors.push(t('checkoutFields.validation.maxFilesRange', 'Maximum files must be between 1 and 50.'))
        }
    }

    const rules = draft.configuration?.conditionalRules || []
    const targetActions = new Set(rules.flatMap((rule) => rule.actions || []))
    if (targetActions.has('SHOW') && targetActions.has('HIDE')) {
        errors.push(t('checkoutFields.validation.ruleVisibilityConflict', 'A field cannot mix show and hide rules.'))
    }
    if (targetActions.has('REQUIRE') && targetActions.has('OPTIONAL')) {
        errors.push(t('checkoutFields.validation.ruleRequiredConflict', 'A field cannot mix required and optional rules.'))
    }
    for (const rule of rules) {
        if (!rule.conditions?.length) {
            errors.push(t('checkoutFields.validation.ruleConditionRequired', 'Conditional rules require at least one condition.'))
        }
        if (!rule.actions?.length) {
            errors.push(t('checkoutFields.validation.ruleActionRequired', 'Conditional rules require at least one action.'))
        }
        if (rule.actions?.includes('SHOW') && rule.actions?.includes('HIDE')) {
            errors.push(t('checkoutFields.validation.ruleVisibilityConflict', 'A rule cannot both show and hide the same field.'))
        }
        if (rule.actions?.includes('REQUIRE') && rule.actions?.includes('OPTIONAL')) {
            errors.push(t('checkoutFields.validation.ruleRequiredConflict', 'A rule cannot both require and make optional the same field.'))
        }
        for (const condition of rule.conditions || []) {
            if (!condition.sourceFieldId) {
                errors.push(t('checkoutFields.validation.ruleSourceRequired', 'Select a source field for every condition.'))
            }
            if (!EMPTY_OPERATORS.has(condition.operator) && !String(condition.comparisonValue || '').trim()) {
                errors.push(t('checkoutFields.validation.ruleComparisonRequired', 'Enter a comparison value for every non-empty condition.'))
            }
            if (draft.id && condition.sourceFieldId === draft.id) {
                errors.push(t('checkoutFields.validation.ruleSelfReference', 'A field cannot depend on itself.'))
            }
        }
    }

    return errors
}

const operatorsForField = (field) => {
    const type = normalizeType(field?.fieldType)
    if (UPLOAD_FIELD_TYPES.has(type)) return ['IS_EMPTY', 'IS_NOT_EMPTY']
    if (type === 'NUMBER' || type === 'DATE') return ['EQUALS', 'NOT_EQUALS', 'GREATER_THAN', 'GREATER_THAN_OR_EQUAL', 'LESS_THAN', 'LESS_THAN_OR_EQUAL', 'IS_EMPTY', 'IS_NOT_EMPTY']
    if (type === 'CHECKBOX') return ['EQUALS', 'NOT_EQUALS', 'IS_EMPTY', 'IS_NOT_EMPTY']
    return ['EQUALS', 'NOT_EQUALS', 'CONTAINS', 'NOT_CONTAINS', 'IS_EMPTY', 'IS_NOT_EMPTY']
}

const ruleSummary = (rule, fields, targetLabel, t) => {
    const condition = rule.conditions?.[0]
    const source = fields.find((field) => field.id === condition?.sourceFieldId)
    const actions = (rule.actions || []).map((action) => t(`checkoutFields.rules.actions.${action}`, action.toLowerCase())).join(' + ')
    if (!condition || !source) return t('checkoutFields.rules.incompleteSummary', 'Incomplete rule for {{target}}', { target: targetLabel })
    const operator = t(`checkoutFields.rules.operators.${condition.operator}`, condition.operator.toLowerCase().replaceAll('_', ' '))
    const comparison = EMPTY_OPERATORS.has(condition.operator) ? '' : ` ${condition.comparisonValue || ''}`
    return t('checkoutFields.rules.summary', '{{actions}} {{target}} when {{source}} {{operator}}{{comparison}}.', {
        actions,
        target: targetLabel,
        source: source.label,
        operator,
        comparison,
    })
}

function FieldRulesConfiguration({ draft, fields, onChange }) {
    const { t } = useTranslation()
    const rules = draft.configuration?.conditionalRules || []
    const sourceFields = fields.filter((field) => field.id !== draft.id && field.isEnabled)

    const updateRules = (nextRules) => {
        onChange((current) => ({
            ...current,
            configuration: {
                ...(current.configuration || {}),
                conditionalRules: nextRules,
            },
        }))
    }

    const patchRule = (index, patch) => {
        updateRules(rules.map((rule, ruleIndex) => ruleIndex === index ? { ...rule, ...patch } : rule))
    }

    const patchCondition = (ruleIndex, conditionIndex, conditionPatch) => {
        const rule = rules[ruleIndex]
        const conditions = [...(rule.conditions || [])]
        const condition = conditions[conditionIndex] || { sourceFieldId: '', operator: 'EQUALS', comparisonValue: '' }
        conditions[conditionIndex] = { ...condition, ...conditionPatch }
        patchRule(ruleIndex, { conditions })
    }

    const toggleAction = (ruleIndex, action) => {
        const rule = rules[ruleIndex]
        const actions = new Set(rule.actions || [])
        if (actions.has(action)) actions.delete(action)
        else actions.add(action)
        patchRule(ruleIndex, { actions: Array.from(actions) })
    }

    const addRule = () => {
        updateRules([
            ...rules,
            {
                ruleId: newRuleId(),
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: '', operator: 'EQUALS', comparisonValue: '' }],
                actions: ['SHOW'],
            },
        ])
    }

    return (
        <fieldset className="rounded-xl border border-gray-200 p-4 dark:border-gray-700">
            <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">{t('checkoutFields.rules.title', 'Conditional Rules')}</legend>
            <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">{t('checkoutFields.rules.description', 'Show, hide, require, or make this field optional based on another checkout field.')}</p>

            <div className="mt-4 space-y-4">
                {rules.map((rule, index) => {
                    return (
                        <div key={rule.ruleId || index} className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-900/40">
                            <div className="flex flex-wrap items-center justify-between gap-2">
                                <p className="text-sm font-medium text-gray-800 dark:text-gray-100">{ruleSummary(rule, fields, draft.label || t('checkoutFields.thisField', 'this field'), t)}</p>
                                <button type="button" onClick={() => updateRules(rules.filter((_, ruleIndex) => ruleIndex !== index))} className="rounded-md border border-red-200 px-2 py-1 text-sm text-red-700 hover:bg-red-50 dark:border-red-800 dark:text-red-300">
                                    {t('checkoutFields.rules.delete', 'Delete rule')}
                                </button>
                            </div>

                            <div className="mt-3">
                                <label className="text-sm font-medium text-gray-700 dark:text-gray-200">
                                    {t('checkoutFields.rules.mode', 'Mode')}
                                    <select value={rule.conditionMode || 'ALL'} onChange={(event) => patchRule(index, { conditionMode: event.target.value })} className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100">
                                        <option value="ALL">{t('checkoutFields.rules.all', 'ALL')}</option>
                                        <option value="ANY">{t('checkoutFields.rules.any', 'ANY')}</option>
                                    </select>
                                </label>
                            </div>

                            <div className="mt-3 space-y-3">
                                {(rule.conditions || []).map((condition, conditionIndex) => {
                                    const source = sourceFields.find((field) => field.id === condition.sourceFieldId)
                                    const operators = operatorsForField(source)
                                    return (
                                        <div key={`${rule.ruleId}-condition-${conditionIndex}`} className="rounded-lg border border-gray-200 bg-white p-3 dark:border-gray-700 dark:bg-gray-800">
                                            <div className="grid gap-3 md:grid-cols-4">
                                                <label className="text-sm font-medium text-gray-700 dark:text-gray-200 md:col-span-2">
                                                    {t('checkoutFields.rules.sourceField', 'Source field')}
                                                    <select value={condition.sourceFieldId || ''} onChange={(event) => patchCondition(index, conditionIndex, { sourceFieldId: event.target.value, operator: 'EQUALS', comparisonValue: '' })} className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100">
                                                        <option value="">{t('checkoutFields.rules.selectSource', 'Select field')}</option>
                                                        {sourceFields.map((field) => <option key={field.id} value={field.id}>{field.label}</option>)}
                                                    </select>
                                                </label>
                                                <label className="text-sm font-medium text-gray-700 dark:text-gray-200">
                                                    {t('checkoutFields.rules.operator', 'Operator')}
                                                    <select value={condition.operator || 'EQUALS'} onChange={(event) => patchCondition(index, conditionIndex, { operator: event.target.value, comparisonValue: '' })} className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100">
                                                        {operators.map((operator) => <option key={operator} value={operator}>{t(`checkoutFields.rules.operators.${operator}`, operator)}</option>)}
                                                    </select>
                                                </label>
                                                <button type="button" disabled={(rule.conditions || []).length === 1} onClick={() => patchRule(index, { conditions: rule.conditions.filter((_, itemIndex) => itemIndex !== conditionIndex) })} className="self-end rounded-lg border border-red-200 px-3 py-2 text-sm text-red-700 disabled:opacity-50">
                                                    {t('checkoutFields.rules.deleteCondition', 'Delete condition')}
                                                </button>
                                            </div>
                                            {!EMPTY_OPERATORS.has(condition.operator) ? (
                                                <label className="mt-3 block text-sm font-medium text-gray-700 dark:text-gray-200">
                                                    {t('checkoutFields.rules.comparisonValue', 'Comparison value')}
                                                    {source && OPTION_FIELD_TYPES.has(normalizeType(source.fieldType)) ? (
                                                        <select value={condition.comparisonValue || ''} onChange={(event) => patchCondition(index, conditionIndex, { comparisonValue: event.target.value })} className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100">
                                                            <option value="">{t('checkoutFields.rules.selectValue', 'Select value')}</option>
                                                            {(source.configuration?.options || []).map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                                                        </select>
                                                    ) : source?.fieldType === 'CHECKBOX' ? (
                                                        <select value={condition.comparisonValue || ''} onChange={(event) => patchCondition(index, conditionIndex, { comparisonValue: event.target.value })} className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100">
                                                            <option value="">{t('checkoutFields.rules.selectValue', 'Select value')}</option>
                                                            <option value="true">true</option>
                                                            <option value="false">false</option>
                                                        </select>
                                                    ) : (
                                                        <input value={condition.comparisonValue || ''} onChange={(event) => patchCondition(index, conditionIndex, { comparisonValue: event.target.value })} className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100" />
                                                    )}
                                                </label>
                                            ) : null}
                                        </div>
                                    )
                                })}
                                <button type="button" disabled={(rule.conditions || []).length >= 5} onClick={() => patchRule(index, { conditions: [...(rule.conditions || []), { sourceFieldId: '', operator: 'EQUALS', comparisonValue: '' }] })} className="rounded-lg border border-indigo-200 px-3 py-2 text-sm font-medium text-indigo-700 disabled:opacity-50">
                                    {t('checkoutFields.rules.addCondition', 'Add condition')}
                                </button>
                            </div>

                            <div className="mt-3 flex flex-wrap gap-2">
                                {['SHOW', 'HIDE', 'REQUIRE', 'OPTIONAL'].map((action) => (
                                    <label key={action} className="inline-flex items-center gap-2 rounded-full border border-gray-300 px-3 py-1.5 text-sm text-gray-700 dark:border-gray-600 dark:text-gray-200">
                                        <input type="checkbox" checked={(rule.actions || []).includes(action)} onChange={() => toggleAction(index, action)} className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500" />
                                        {t(`checkoutFields.rules.actions.${action}`, action)}
                                    </label>
                                ))}
                            </div>
                        </div>
                    )
                })}
            </div>

            <button type="button" onClick={addRule} className="mt-4 inline-flex items-center gap-2 rounded-lg border border-indigo-200 px-3 py-2 text-sm font-medium text-indigo-700 hover:bg-indigo-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500">
                <Plus size={16} aria-hidden="true" />
                {t('checkoutFields.rules.add', 'Add rule')}
            </button>
        </fieldset>
    )
}

function SortableFieldRow({ field, index, total, isOrdering, onEdit, onDelete, onMove }) {
    const { t } = useTranslation()
    const {
        attributes,
        listeners,
        setNodeRef,
        transform,
        transition,
        isDragging,
    } = useSortable({ id: field.id, disabled: isOrdering })
    const style = { transform: CSS.Transform.toString(transform), transition }
    const system = isSystemField(field)
    const enabledLabel = field.isEnabled
        ? t('checkoutFields.shownInCheckout', 'Shown in Checkout')
        : t('checkoutFields.hiddenFromCheckout', 'Hidden from Checkout')
    const requiredLabel = field.isRequired ? t('checkoutFields.required', 'Required') : t('checkoutFields.optional', 'Optional')

    return (
        <li
            ref={setNodeRef}
            style={style}
            className={`rounded-xl border border-gray-200 bg-white p-4 shadow-sm transition dark:border-gray-700 dark:bg-gray-800 ${isDragging ? 'opacity-70 ring-2 ring-indigo-500' : ''}`}
        >
            <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div className="flex min-w-0 items-start gap-3">
                    <button
                        type="button"
                        className="mt-1 rounded-lg border border-gray-200 p-2 text-gray-500 hover:bg-gray-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-700"
                        aria-label={t('checkoutFields.dragHandle', 'Drag to reorder {{label}}', { label: field.label })}
                        disabled={isOrdering}
                        {...attributes}
                        {...listeners}
                    >
                        <GripVertical size={18} aria-hidden="true" />
                    </button>
                    <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                            <h2 className="truncate text-base font-semibold text-gray-900 dark:text-gray-100">{field.label}</h2>
                            <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${system ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/40 dark:text-blue-200' : 'bg-purple-50 text-purple-700 dark:bg-purple-900/40 dark:text-purple-200'}`}>
                                {system ? t('checkoutFields.system', 'System') : t('checkoutFields.custom', 'Custom')}
                            </span>
                        </div>
                        {field.helpText ? <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">{field.helpText}</p> : null}
                        <div className="mt-2 flex flex-wrap gap-2 text-xs">
                            <span className="rounded-full bg-gray-100 px-2 py-1 font-medium text-gray-700 dark:bg-gray-700 dark:text-gray-200">
                                {t(`checkoutFields.types.${normalizeType(field.fieldType)}`, normalizeType(field.fieldType))}
                            </span>
                            <span className="rounded-full bg-gray-100 px-2 py-1 font-medium text-gray-700 dark:bg-gray-700 dark:text-gray-200">{requiredLabel}</span>
                            <span className="rounded-full bg-gray-100 px-2 py-1 font-medium text-gray-700 dark:bg-gray-700 dark:text-gray-200">{enabledLabel}</span>
                        </div>
                    </div>
                </div>

                <div className="flex flex-wrap items-center gap-2 sm:justify-end">
                    <button
                        type="button"
                        onClick={() => onMove(field.id, -1)}
                        disabled={isOrdering || index === 0}
                        className="rounded-lg border border-gray-300 p-2 text-gray-600 hover:bg-gray-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-300 dark:hover:bg-gray-700"
                        aria-label={t('checkoutFields.moveUp', 'Move {{label}} up', { label: field.label })}
                    >
                        <ArrowUp size={16} aria-hidden="true" />
                    </button>
                    <button
                        type="button"
                        onClick={() => onMove(field.id, 1)}
                        disabled={isOrdering || index === total - 1}
                        className="rounded-lg border border-gray-300 p-2 text-gray-600 hover:bg-gray-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-300 dark:hover:bg-gray-700"
                        aria-label={t('checkoutFields.moveDown', 'Move {{label}} down', { label: field.label })}
                    >
                        <ArrowDown size={16} aria-hidden="true" />
                    </button>
                    <button
                        type="button"
                        onClick={() => onEdit(field)}
                        className="inline-flex items-center gap-2 rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 dark:border-gray-600 dark:text-gray-200 dark:hover:bg-gray-700"
                    >
                        <Pencil size={16} aria-hidden="true" />
                        {t('checkoutFields.edit', 'Edit')}
                    </button>
                    {!system ? (
                        <button
                            type="button"
                            onClick={() => onDelete(field)}
                            className="inline-flex items-center gap-2 rounded-lg border border-red-200 px-3 py-2 text-sm font-medium text-red-700 hover:bg-red-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-red-500 dark:border-red-800 dark:text-red-300 dark:hover:bg-red-900/30"
                        >
                            <Archive size={16} aria-hidden="true" />
                            {t('checkoutFields.archive', 'Archive')}
                        </button>
                    ) : null}
                </div>
            </div>
        </li>
    )
}

function FieldEditor({ draft, fields, errors, isSaving, onChange, onClose, onSave }) {
    const { t } = useTranslation()
    const initialFocusRef = useRef(null)
    const system = isSystemField(draft)
    const fieldType = normalizeType(draft.fieldType)
    const configuration = draft.configuration || {}

    const patchDraft = (patch) => {
        onChange((current) => ({ ...current, ...patch }))
    }

    const updateConfiguration = (key, value) => {
        onChange((current) => ({
            ...current,
            configuration: {
                ...(current.configuration || {}),
                [key]: value,
            },
        }))
    }

    const updateOption = (index, value) => {
        onChange((current) => {
            const currentConfiguration = current.configuration || {}
            const options = [...(currentConfiguration.options || [])]
            const previous = options[index] || { label: '', value: '' }
            options[index] = {
                ...previous,
                label: value,
                value,
            }
            return {
                ...current,
                configuration: {
                    ...currentConfiguration,
                    options,
                },
            }
        })
    }

    return (
        <AuthoringDialog
            open
            title={draft.id ? t('checkoutFields.editor.editTitle', 'Edit checkout field') : t('checkoutFields.editor.createTitle', 'Add custom field')}
            description={t('checkoutFields.editor.description', 'Configure how this field appears to customers during checkout.')}
            onClose={onClose}
            initialFocusRef={initialFocusRef}
            panelClassName="max-h-[90vh] max-w-3xl overflow-y-auto"
        >
            <form className="mt-5 space-y-5" onSubmit={(event) => { event.preventDefault(); onSave() }}>
                <fieldset disabled={isSaving} className="space-y-5">
                {errors.length > 0 ? (
                    <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">
                        <p className="font-medium">{t('checkoutFields.validation.title', 'Fix these issues before saving:')}</p>
                        <ul className="mt-2 list-disc space-y-1 pl-5">
                            {errors.map((error) => <li key={error}>{error}</li>)}
                        </ul>
                    </div>
                ) : null}

                <div className="grid gap-4 md:grid-cols-2">
                    <label htmlFor="checkout-field-label" className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                        {t('checkoutFields.label', 'Label')}
                        <input
                            id="checkout-field-label"
                            ref={initialFocusRef}
                            value={draft.label}
                            maxLength={120}
                            onChange={(event) => patchDraft({ label: event.target.value })}
                            className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100"
                            aria-invalid={errors.some((error) => error.includes(t('checkoutFields.label', 'Label')))}
                        />
                    </label>

                    <label htmlFor="checkout-field-type" className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                        {t('checkoutFields.fieldType', 'Field Type')}
                        <select
                            id="checkout-field-type"
                            value={fieldType}
                            disabled={system}
                            aria-describedby={system ? 'checkout-field-type-help' : undefined}
                            onChange={(event) => patchDraft({ fieldType: event.target.value, configuration: { schemaVersion: 1, options: OPTION_FIELD_TYPES.has(event.target.value) ? [{ value: '', label: '' }] : undefined } })}
                            className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100 disabled:cursor-not-allowed disabled:bg-gray-100 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100"
                        >
                            {CUSTOM_FIELD_TYPES.map((type) => (
                                <option key={type} value={type}>{t(`checkoutFields.types.${type}`, type)}</option>
                            ))}
                        </select>
                        {system ? (
                            <span id="checkout-field-type-help" className="mt-2 block text-xs font-normal text-gray-500 dark:text-gray-400">
                                {t('checkoutFields.systemTypeLockedHelp', 'System field type is locked because it maps to checkout address, customer, and order data.')}
                            </span>
                        ) : null}
                    </label>
                </div>

                <label htmlFor="checkout-field-help-text" className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                    {t('checkoutFields.helpText', 'Help Text')}
                    <textarea
                        id="checkout-field-help-text"
                        value={draft.helpText}
                        maxLength={500}
                        onChange={(event) => patchDraft({ helpText: event.target.value })}
                        rows={3}
                        className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-900 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100"
                    />
                </label>

                <div className="grid gap-3 md:grid-cols-2">
                    <label className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-3 text-sm font-medium text-gray-700 dark:border-gray-700 dark:text-gray-200">
                        <span>
                            {t('checkoutFields.showInCheckout', 'Show in Checkout')}
                            <span className="mt-1 block text-xs font-normal text-gray-500 dark:text-gray-400">
                                {t('checkoutFields.showInCheckoutHelp', 'When off, this field is not shown or required in storefront checkout.')}
                            </span>
                        </span>
                        <input
                            type="checkbox"
                            checked={draft.isEnabled}
                            onChange={(event) => onChange((current) => ({ ...current, isEnabled: event.target.checked, isRequired: event.target.checked ? current.isRequired : false }))}
                            className="h-5 w-5 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                        />
                    </label>
                    <label className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-3 text-sm font-medium text-gray-700 dark:border-gray-700 dark:text-gray-200">
                        {t('checkoutFields.requireAtCheckout', 'Require at Checkout')}
                        <input
                            type="checkbox"
                            checked={draft.isRequired}
                            disabled={!draft.isEnabled}
                            onChange={(event) => patchDraft({ isRequired: event.target.checked })}
                            className="h-5 w-5 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 disabled:cursor-not-allowed disabled:opacity-50"
                        />
                    </label>
                </div>

                <FieldTypeConfiguration
                    draft={draft}
                    configuration={configuration}
                    onChange={onChange}
                    updateConfiguration={updateConfiguration}
                    updateOption={updateOption}
                />

                <FieldRulesConfiguration
                    draft={draft}
                    fields={fields}
                    onChange={onChange}
                />

                <div className="flex flex-wrap justify-end gap-2 border-t border-gray-200 pt-5 dark:border-gray-700">
                    <button
                        type="button"
                        onClick={onClose}
                        disabled={isSaving}
                        className="min-h-10 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 disabled:opacity-50 dark:border-gray-600 dark:text-gray-200 dark:hover:bg-gray-700"
                    >
                        {t('checkoutFields.cancel', 'Cancel')}
                    </button>
                    <button
                        type="submit"
                        disabled={isSaving}
                        className="inline-flex min-h-10 items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 disabled:opacity-50"
                    >
                        {isSaving ? <Loader2 size={16} className="animate-spin" aria-hidden="true" /> : null}
                        {t('checkoutFields.save', 'Save')}
                    </button>
                </div>
                </fieldset>
            </form>
        </AuthoringDialog>
    )
}

function UploadFormatSelector({ fieldType, configuration, updateConfiguration }) {
    const { t } = useTranslation()
    const options = getUploadFormatOptions(fieldType)
    const { selectedIds, legacyMimeTypes } = getUploadFormatSelection(fieldType, configuration.allowedMimeTypes)
    const groupId = `checkout-upload-formats-${fieldType.toLowerCase()}`
    const descriptionId = `${groupId}-description`
    const legacyId = `${groupId}-legacy`
    const legendKey = fieldType === 'FILE' ? 'checkoutFields.allowedFileTypes' : 'checkoutFields.allowedImageFormats'
    const legendFallback = fieldType === 'FILE' ? 'Allowed file types' : 'Allowed image formats'
    const descriptionKey = fieldType === 'FILE' ? 'checkoutFields.allowedFileTypesHelp' : 'checkoutFields.allowedImageFormatsHelp'
    const descriptionFallback = fieldType === 'FILE'
        ? 'Choose which file types customers can upload.'
        : 'Choose which image formats customers can upload.'

    const persistSelection = (nextSelectedIds, nextLegacyMimeTypes = legacyMimeTypes) => {
        updateConfiguration('allowedMimeTypes', buildUploadMimeTypes(fieldType, nextSelectedIds, nextLegacyMimeTypes))
    }

    const toggleFormat = (formatId) => {
        const next = new Set(selectedIds)
        if (next.has(formatId)) next.delete(formatId)
        else next.add(formatId)
        persistSelection(next)
    }

    const removeLegacyFormats = () => {
        persistSelection(selectedIds, [])
    }

    return (
        <fieldset
            className="md:col-span-2"
            aria-describedby={legacyMimeTypes.length > 0 ? `${descriptionId} ${legacyId}` : descriptionId}
        >
            <legend className="text-sm font-medium text-gray-700 dark:text-gray-200">
                {t(legendKey, legendFallback)}
            </legend>
            <p id={descriptionId} className="mt-1 text-sm text-gray-500 dark:text-gray-400">
                {t(descriptionKey, descriptionFallback)}
            </p>
            <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {options.map((format) => {
                    const inputId = `${groupId}-${format.id}`
                    return (
                        <label
                            key={format.id}
                            htmlFor={inputId}
                            className="flex min-h-11 items-center gap-3 rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm text-gray-800 shadow-sm transition-colors hover:border-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100"
                        >
                            <input
                                id={inputId}
                                type="checkbox"
                                checked={selectedIds.has(format.id)}
                                onChange={() => toggleFormat(format.id)}
                                className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 dark:border-gray-600"
                            />
                            <span>{t(format.labelKey, format.defaultLabel)}</span>
                        </label>
                    )
                })}
            </div>
            {legacyMimeTypes.length > 0 ? (
                <div id={legacyId} className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800 dark:border-amber-800/70 dark:bg-amber-900/20 dark:text-amber-200">
                    <p className="font-medium">{t('checkoutFields.legacyUploadFormatsTitle', 'Legacy configured formats')}</p>
                    <p className="mt-1">
                        {t('checkoutFields.legacyUploadFormatsHelp', 'These formats are already saved on this field but are not part of the current safe preset list. They will be preserved until you remove them.')}
                    </p>
                    <ul className="mt-2 list-disc space-y-1 pl-5">
                        {legacyMimeTypes.map((mimeType) => <li key={mimeType}><code>{mimeType}</code></li>)}
                    </ul>
                    <button
                        type="button"
                        onClick={removeLegacyFormats}
                        className="mt-3 rounded-md border border-amber-300 bg-white px-3 py-1.5 text-sm font-medium text-amber-800 hover:bg-amber-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-amber-500 dark:border-amber-700 dark:bg-gray-900 dark:text-amber-200"
                    >
                        {t('checkoutFields.removeLegacyUploadFormats', 'Remove legacy formats')}
                    </button>
                </div>
            ) : null}
        </fieldset>
    )
}

function FieldTypeConfiguration({ draft, configuration, updateConfiguration, updateOption }) {
    const { t } = useTranslation()
    const fieldType = normalizeType(draft.fieldType)

    if (TEXT_CONFIG_TYPES.has(fieldType)) {
        return (
            <fieldset className="rounded-xl border border-gray-200 p-4 dark:border-gray-700">
                <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">{t('checkoutFields.configuration', 'Configuration')}</legend>
                <div className="mt-3 grid gap-4 md:grid-cols-2">
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                        {t('checkoutFields.minLength', 'Minimum Length')}
                        <input type="number" min="0" value={configuration.minLength ?? ''} onChange={(event) => updateConfiguration('minLength', event.target.value)} className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100" />
                    </label>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                        {t('checkoutFields.maxLength', 'Maximum Length')}
                        <input type="number" min="0" value={configuration.maxLength ?? ''} onChange={(event) => updateConfiguration('maxLength', event.target.value)} className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100" />
                    </label>
                </div>
            </fieldset>
        )
    }

    if (fieldType === 'NUMBER') {
        return (
            <fieldset className="rounded-xl border border-gray-200 p-4 dark:border-gray-700">
                <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">{t('checkoutFields.configuration', 'Configuration')}</legend>
                <div className="mt-3 grid gap-4 md:grid-cols-2">
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                        {t('checkoutFields.min', 'Minimum')}
                        <input type="number" value={configuration.min ?? ''} onChange={(event) => updateConfiguration('min', event.target.value)} className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100" />
                    </label>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                        {t('checkoutFields.max', 'Maximum')}
                        <input type="number" value={configuration.max ?? ''} onChange={(event) => updateConfiguration('max', event.target.value)} className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100" />
                    </label>
                </div>
            </fieldset>
        )
    }

    if (OPTION_FIELD_TYPES.has(fieldType)) {
        const options = configuration.options || []
        return (
            <fieldset className="rounded-xl border border-gray-200 p-4 dark:border-gray-700">
                <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">{t('checkoutFields.options', 'Options')}</legend>
                <div className="mt-3 space-y-3">
                    {options.map((option, index) => (
                        <div key={index} className="flex flex-col gap-2 sm:flex-row">
                            <label className="flex-1 text-sm font-medium text-gray-700 dark:text-gray-200">
                                <span className="sr-only">{t('checkoutFields.optionLabel', 'Option Label')}</span>
                                <input value={option.label} onChange={(event) => updateOption(index, event.target.value)} placeholder={t('checkoutFields.optionPlaceholder', 'Option value')} className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100" />
                            </label>
                            <button
                                type="button"
                                onClick={() => updateConfiguration('options', options.filter((_, optionIndex) => optionIndex !== index))}
                                className="rounded-lg border border-red-200 px-3 py-2 text-sm font-medium text-red-700 hover:bg-red-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-red-500 dark:border-red-800 dark:text-red-300"
                            >
                                {t('checkoutFields.delete', 'Delete')}
                            </button>
                        </div>
                    ))}
                    <button
                        type="button"
                        onClick={() => updateConfiguration('options', [...options, { value: '', label: '' }])}
                        className="inline-flex items-center gap-2 rounded-lg border border-indigo-200 px-3 py-2 text-sm font-medium text-indigo-700 hover:bg-indigo-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
                    >
                        <Plus size={16} aria-hidden="true" />
                        {t('checkoutFields.addOption', 'Add Option')}
                    </button>
                </div>
            </fieldset>
        )
    }

    if (UPLOAD_FIELD_TYPES.has(fieldType)) {
        return (
            <fieldset className="rounded-xl border border-gray-200 p-4 dark:border-gray-700">
                <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">{t('checkoutFields.uploadConfiguration', 'Upload Configuration')}</legend>
                <div className="mt-3 grid gap-4 md:grid-cols-2">
                    <UploadFormatSelector
                        fieldType={fieldType}
                        configuration={configuration}
                        updateConfiguration={updateConfiguration}
                    />
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                        {t('checkoutFields.maxFileSizeMb', 'Maximum File Size (MB)')}
                        <input
                            type="number"
                            min="1"
                            max="100"
                            value={configuration.maxFileSizeMb ?? (configuration.maxFileSize ? Math.round(configuration.maxFileSize / 1024 / 1024) : '')}
                            onChange={(event) => updateConfiguration('maxFileSizeMb', event.target.value)}
                            className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100"
                        />
                    </label>
                    {fieldType === 'MULTI_IMAGE' ? (
                        <label className="block text-sm font-medium text-gray-700 dark:text-gray-200">
                            {t('checkoutFields.maxFiles', 'Maximum Files')}
                            <input
                                type="number"
                                min="1"
                                max="50"
                                value={configuration.maxFiles ?? ''}
                                onChange={(event) => updateConfiguration('maxFiles', event.target.value)}
                                className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100"
                            />
                        </label>
                    ) : null}
                </div>
            </fieldset>
        )
    }

    return (
        <p className="rounded-lg bg-gray-50 px-4 py-3 text-sm text-gray-600 dark:bg-gray-700 dark:text-gray-200">
            {t('checkoutFields.noConfiguration', 'This field type has no additional configuration.')}
        </p>
    )
}

export function CheckoutFieldsPage() {
    const { t } = useTranslation()
    const { showSuccess, showError } = useToast()
    const [editorDraft, setEditorDraft] = useState(null)
    const [originalDraft, setOriginalDraft] = useState(null)
    const [validationErrors, setValidationErrors] = useState([])
    const [pendingDelete, setPendingDelete] = useState(null)
    const [confirmDiscard, setConfirmDiscard] = useState(false)
    const operationRef = useRef(false)

    const {
        fields,
        isLoading,
        isError,
        isForbidden,
        isEmpty,
        error,
        mutationError,
        isMutating,
        isOrdering,
        refetch,
        createField,
        updateField,
        deleteField,
        reorderFields,
    } = useCheckoutFields()

    const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 8 } }))
    const isDirty = editorDraft && JSON.stringify(editorDraft) !== JSON.stringify(originalDraft)

    const orderedFields = useMemo(() => fields, [fields])

    const openCreateEditor = () => {
        const nextDraft = makeDraft()
        setOriginalDraft(nextDraft)
        setEditorDraft(nextDraft)
        setValidationErrors([])
    }

    const openEditEditor = (field) => {
        const nextDraft = makeDraft(field)
        setOriginalDraft(nextDraft)
        setEditorDraft(nextDraft)
        setValidationErrors([])
    }

    const closeEditor = () => {
        if (operationRef.current) return
        if (isDirty) {
            setConfirmDiscard(true)
            return
        }
        setEditorDraft(null)
        setOriginalDraft(null)
        setValidationErrors([])
    }

    const discardEditor = () => {
        setConfirmDiscard(false)
        setEditorDraft(null)
        setOriginalDraft(null)
        setValidationErrors([])
    }

    const saveEditor = async () => {
        if (!editorDraft || operationRef.current || isOrdering) return
        const errors = validateDraft(editorDraft, t)
        setValidationErrors(errors)
        if (errors.length) return

        operationRef.current = true
        try {
            if (editorDraft.id) {
                const outcome = await updateField(editorDraft.id, buildPayload(editorDraft, { includeType: !isSystemField(editorDraft) }))
                if (!outcome.readbackFailed) showSuccess(t('checkoutFields.saved', 'Checkout field saved.'))
            } else {
                const outcome = await createField(buildPayload(editorDraft, { includeType: true }))
                if (!outcome.readbackFailed) showSuccess(t('checkoutFields.created', 'Custom field created.'))
            }
            setEditorDraft(null)
            setOriginalDraft(null)
            setValidationErrors([])
        } catch (err) {
            showError(err?.response?.data?.message || t('checkoutFields.saveFailed', 'Unable to save checkout field.'))
        } finally {
            operationRef.current = false
        }
    }

    const confirmDelete = async () => {
        if (!pendingDelete || operationRef.current || isOrdering) return
        operationRef.current = true
        try {
            const outcome = await deleteField(pendingDelete.id)
            if (!outcome.readbackFailed) showSuccess(t('checkoutFields.archived', 'Custom field archived.'))
            setPendingDelete(null)
        } catch (err) {
            showError(err?.response?.data?.message || t('checkoutFields.archiveFailed', 'Unable to archive checkout field.'))
        } finally {
            operationRef.current = false
        }
    }

    const persistOrder = async (nextFields) => {
        try {
            const result = await reorderFields(nextFields)
            if (result) showSuccess(t('checkoutFields.orderSaved', 'Checkout field order saved.'))
        } catch (err) {
            showError(err?.response?.data?.message || t('checkoutFields.orderFailed', 'Unable to save checkout field order.'))
        }
    }

    const handleDragEnd = ({ active, over }) => {
        if (isOrdering || !over || active.id === over.id) return
        const oldIndex = orderedFields.findIndex((field) => field.id === active.id)
        const newIndex = orderedFields.findIndex((field) => field.id === over.id)
        if (oldIndex < 0 || newIndex < 0 || oldIndex === newIndex) return
        persistOrder(arrayMove(orderedFields, oldIndex, newIndex))
    }

    const handleMove = (fieldId, direction) => {
        if (isOrdering) return
        const oldIndex = orderedFields.findIndex((field) => field.id === fieldId)
        const newIndex = oldIndex + direction
        if (oldIndex < 0 || newIndex < 0 || newIndex >= orderedFields.length) return
        persistOrder(arrayMove(orderedFields, oldIndex, newIndex))
    }

    return (
        <div className="space-y-4">
            <div className="flex flex-col gap-4 rounded-xl border border-gray-200 bg-white px-4 py-4 dark:border-gray-700 dark:bg-gray-800 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">{t('checkoutFields.title', 'Checkout Fields')}</h1>
                    <p className="mt-1 max-w-3xl text-sm text-gray-500 dark:text-gray-400">
                        {t('checkoutFields.description', 'Control the customer information collected during storefront checkout.')}
                    </p>
                </div>
                {!isForbidden ? (
                    <AuthoringButton
                        action={{ label: t('checkoutFields.addCustomField', 'Add Custom Field'), onClick: openCreateEditor, icon: Plus }}
                        defaultLabel={t('checkoutFields.addCustomField', 'Add Custom Field')}
                        tone="primary"
                    />
                ) : null}
            </div>

            {mutationError ? (
                <AuthoringFeedback kind="error" title={t('checkoutFields.actionFailed', 'Action failed')} message={mutationError} />
            ) : null}

            {isForbidden ? (
                <AuthoringFeedback
                    kind="error"
                    title={t('checkoutFields.forbiddenTitle', 'Permission required')}
                    message={t('checkoutFields.forbiddenDescription', 'Only POS owners and admins can manage checkout fields.')}
                />
            ) : null}

            {isError ? (
                <AuthoringFeedback
                    kind="error"
                    title={t('checkoutFields.loadErrorTitle', 'Unable to load checkout fields')}
                    message={error}
                    action={{ label: t('checkoutFields.retry', 'Retry'), onClick: () => refetch() }}
                />
            ) : null}

            {isLoading ? <AuthoringLoadingState label={t('checkoutFields.loading', 'Loading checkout fields...')} /> : null}

            {isEmpty ? (
                <AuthoringEmptyState
                    title={t('checkoutFields.emptyTitle', 'No checkout fields configured')}
                    description={t('checkoutFields.emptyDescription', 'System fields will initialize automatically when available. You can also add a custom field.')}
                    action={{ label: t('checkoutFields.addCustomField', 'Add Custom Field'), onClick: openCreateEditor }}
                />
            ) : null}

            {!isLoading && !isError && !isForbidden && orderedFields.length > 0 ? (
                <section className="rounded-xl border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-900/30" aria-labelledby="checkout-fields-list-heading">
                    <div className="mb-3 flex flex-wrap items-center justify-between gap-2 px-1">
                        <h2 id="checkout-fields-list-heading" className="text-sm font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400">
                            {t('checkoutFields.fieldList', 'Field List')}
                        </h2>
                        {isOrdering ? <span className="inline-flex items-center gap-2 text-sm text-gray-500"><Loader2 size={16} className="animate-spin" />{t('checkoutFields.savingOrder', 'Saving order...')}</span> : null}
                    </div>
                    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                        <SortableContext items={orderedFields.map((field) => field.id)} strategy={verticalListSortingStrategy}>
                            <ul className="space-y-3">
                                {orderedFields.map((field, index) => (
                                    <SortableFieldRow
                                        key={field.id}
                                        field={field}
                                        index={index}
                                        total={orderedFields.length}
                                        isOrdering={isOrdering}
                                        onEdit={openEditEditor}
                                        onDelete={setPendingDelete}
                                        onMove={handleMove}
                                    />
                                ))}
                            </ul>
                        </SortableContext>
                    </DndContext>
                </section>
            ) : null}

            {editorDraft ? (
                <FieldEditor
                    draft={editorDraft}
                    fields={orderedFields}
                    errors={validationErrors}
                    isSaving={isMutating}
                    onChange={setEditorDraft}
                    onClose={closeEditor}
                    onSave={saveEditor}
                />
            ) : null}

            <AuthoringConfirmDialog
                open={confirmDiscard}
                title={t('checkoutFields.discardTitle', 'Discard unsaved changes?')}
                description={t('checkoutFields.discardDescription', 'Closing the editor will discard changes that have not been saved.')}
                confirmLabel={t('checkoutFields.discard', 'Discard')}
                confirmTone="danger"
                busy={false}
                onConfirm={discardEditor}
                onClose={() => setConfirmDiscard(false)}
            />

            <AuthoringConfirmDialog
                open={Boolean(pendingDelete)}
                title={t('checkoutFields.archiveTitle', 'Archive custom field?')}
                description={t('checkoutFields.archiveDescription', 'This removes the custom field from current checkout while preserving historical order snapshots. System fields can be hidden with Show in Checkout.')}
                confirmLabel={t('checkoutFields.archive', 'Archive')}
                confirmTone="danger"
                busy={isMutating}
                onConfirm={confirmDelete}
                onClose={() => setPendingDelete(null)}
            />
        </div>
    )
}

export default CheckoutFieldsPage
