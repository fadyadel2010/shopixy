import { useState } from 'react'
import { useTranslation } from 'react-i18next'

export default function VariantOptionsBuilder({
    optionGroups,
    onChange,
    maxOptions = 3,
    combinationCount = 0,
    risk = { level: 'safe', label: 'Safe', message: '' },
    duplicateValueMap = {},
    validationMessages = [],
    safeLimit = 100,
    infoLimit = 300,
    warningLimit = 1000
}) {
    const { t } = useTranslation()
    const [draftValues, setDraftValues] = useState({})
    const tx = (key, fallback) => {
        const value = t(key)
        return value === key ? fallback : value
    }

    const updateGroup = (groupId, patch) => {
        onChange(optionGroups.map((group) => (
            group.id === groupId ? { ...group, ...patch } : group
        )))
    }

    const removeGroup = (groupId) => {
        onChange(optionGroups.filter(group => group.id !== groupId))
        setDraftValues(prev => {
            const next = { ...prev }
            delete next[groupId]
            return next
        })
    }

    const addGroup = () => {
        if (optionGroups.length >= maxOptions) {
            return
        }

        onChange([
            ...optionGroups,
            {
                id: `option-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
                name: '',
                values: []
            }
        ])
    }

    const addValue = (groupId) => {
        const draftValue = draftValues[groupId] || ''
        const group = optionGroups.find(optionGroup => optionGroup.id === groupId)
        const candidateValues = [...(group?.values || []), draftValue.trim()].filter(Boolean)

        updateGroup(groupId, { values: candidateValues })
        setDraftValues(prev => ({ ...prev, [groupId]: '' }))
    }

    const removeValue = (groupId, valueIndexToRemove) => {
        onChange(optionGroups.map((group) => (
            group.id === groupId
                ? { ...group, values: group.values.filter((_, valueIndex) => valueIndex !== valueIndexToRemove) }
                : group
        )))
    }

    return (
        <div className="space-y-4">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <h4 className="text-sm font-semibold text-gray-900">
                        {tx('variants.optionBuilder', 'Variant Options')}
                    </h4>
                    <p className="text-sm text-gray-500">
                        {tx('variants.optionBuilderDescription', 'Define up to 3 option groups and generate combinations automatically.')}
                    </p>
                    <p className="mt-1 text-xs text-gray-500">
                        {tx('variants.optionBuilderHowTo', 'To use this builder: name an option group, type each value, then press Enter or Add.')}
                    </p>
                </div>

                <button
                    type="button"
                    onClick={addGroup}
                    disabled={optionGroups.length >= maxOptions}
                    className="rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                >
                    + {tx('variants.addOption', 'Add Option Group')}
                </button>
            </div>

            <div className="space-y-3">
                {optionGroups.map((group, index) => {
                    const duplicateValues = duplicateValueMap[group.id] || new Set()
                    const hasDuplicateValues = duplicateValues.size > 0
                    const hasEmptyValues = group.name?.trim() && (group.values || []).filter(value => value?.trim()).length === 0

                    return (
                    <div key={group.id} data-option-group={group.id} className={`rounded-2xl border bg-gray-50 p-4 ${hasDuplicateValues || hasEmptyValues ? 'border-red-300' : 'border-gray-200'}`}>
                        <div className="mb-3 flex items-center justify-between gap-3">
                            <div className="flex-1">
                                <label className="label">
                                    {tx('variants.optionName', 'Option Name')} {index + 1}
                                </label>
                                <input
                                    type="text"
                                    value={group.name}
                                    onChange={(e) => updateGroup(group.id, { name: e.target.value })}
                                    onKeyDown={(e) => {
                                        // WAVE 2 (Finding 9): this input lives inside the outer
                                        // product <form>, so an unhandled Enter triggers implicit
                                        // form submission (full save/page state loss). Enter here
                                        // commits the name and advances to the value input instead.
                                        if (e.key === 'Enter') {
                                            e.preventDefault()
                                            const groupElement = e.currentTarget.closest('[data-option-group]')
                                            const valueInput = groupElement?.querySelector('[data-option-value-input]')
                                            if (valueInput) {
                                                valueInput.focus()
                                            } else {
                                                e.currentTarget.blur()
                                            }
                                        }
                                    }}
                                    className="input"
                                    placeholder={tx('variants.optionNamePlaceholder', 'e.g. Size, Color, Material')}
                                />
                            </div>

                            <button
                                type="button"
                                onClick={() => removeGroup(group.id)}
                                className="mt-6 text-sm font-medium text-red-600 transition hover:text-red-700"
                            >
                                {tx('common.remove', 'Remove')}
                            </button>
                        </div>

                        <div>
                            <label className="label">{tx('variants.optionValues', 'Values')}</label>
                            <div className="flex flex-wrap gap-2 rounded-xl border border-gray-200 bg-white p-3">
                                {group.values.map((value, valueIndex) => {
                                    const isDuplicate = duplicateValues.has(value.trim().toLowerCase())

                                    return (
                                    <span
                                        key={`${value}-${valueIndex}`}
                                        className={`inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-medium ${
                                            isDuplicate
                                                ? 'bg-red-50 text-red-700 ring-1 ring-red-200'
                                                : 'bg-primary-50 text-primary-700'
                                        }`}
                                    >
                                        {value}
                                        <button
                                            type="button"
                                            onClick={() => removeValue(group.id, valueIndex)}
                                            className="text-primary-500 transition hover:text-primary-700"
                                        >
                                            x
                                        </button>
                                    </span>
                                    )
                                })}

                                <input
                                    type="text"
                                    data-option-value-input
                                    value={draftValues[group.id] || ''}
                                    onChange={(e) => setDraftValues(prev => ({ ...prev, [group.id]: e.target.value }))}
                                    onKeyDown={(e) => {
                                        if ((e.key === 'Enter' || e.key === ',') && (draftValues[group.id] || '').trim()) {
                                            e.preventDefault()
                                            addValue(group.id)
                                        }
                                    }}
                                    className="min-w-[12rem] flex-1 border-0 bg-transparent p-0 text-sm text-gray-700 focus:outline-none focus:ring-0"
                                    placeholder={tx('variants.optionValuePlaceholder', 'Type a value and press Enter')}
                                />

                                <button
                                    type="button"
                                    onClick={() => addValue(group.id)}
                                    disabled={!(draftValues[group.id] || '').trim()}
                                    className="rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                                >
                                    {tx('common.add', 'Add')}
                                </button>
                            </div>
                            {hasDuplicateValues && (
                                <p className="mt-2 text-xs font-medium text-red-700">
                                    {tx('variants.duplicateAttributeValues', 'Duplicate attribute values are not allowed.')}
                                </p>
                            )}
                            {hasEmptyValues && (
                                <p className="mt-2 text-xs font-medium text-red-700">
                                    {tx('variants.emptyAttributeValues', 'Add at least one value for this attribute.')}
                                </p>
                            )}
                        </div>
                    </div>
                    )
                })}
            </div>

            <div className={`rounded-xl border px-4 py-3 text-sm ${
                risk.level === 'confirm'
                    ? 'border-red-300 bg-red-50 text-red-800'
                    : risk.level === 'warning'
                        ? 'border-amber-300 bg-amber-50 text-amber-800'
                        : risk.level === 'info'
                            ? 'border-blue-300 bg-blue-50 text-blue-800'
                            : 'border-emerald-200 bg-emerald-50 text-emerald-700'
            }`}>
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                        <div className="text-xs font-semibold uppercase tracking-wide opacity-75">
                            {tx('variants.variantsToGenerate', 'Variants to Generate')}
                        </div>
                        <div className="mt-1 text-3xl font-semibold text-gray-900">
                            {combinationCount}
                        </div>
                    </div>
                    <div className="sm:max-w-md">
                        <div className="font-semibold">{risk.label}</div>
                        <div className="mt-1">{risk.message}</div>
                        <div className="mt-1 text-xs opacity-80">
                            {tx('variants.variantExplosionThresholds', 'Safe <= {{safe}}, info {{infoStart}}-{{info}}, warning {{warningStart}}-{{warning}}, confirmation above {{warning}}')
                                .replace('{{safe}}', safeLimit)
                                .replace('{{infoStart}}', safeLimit + 1)
                                .replace('{{info}}', infoLimit)
                                .replace('{{warningStart}}', infoLimit + 1)
                                .replace('{{warning}}', warningLimit)}
                        </div>
                    </div>
                </div>
                {validationMessages.length > 0 && (
                    <div className="mt-3 border-t border-current/20 pt-3">
                        {validationMessages.map((message) => (
                            <div key={message}>{message}</div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    )
}
