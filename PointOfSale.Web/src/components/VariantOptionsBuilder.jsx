import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'

function uniqueValues(values) {
    const seen = new Set()

    return values.filter((value) => {
        const normalized = value.trim().toLowerCase()
        if (!normalized || seen.has(normalized)) {
            return false
        }

        seen.add(normalized)
        return true
    })
}

export default function VariantOptionsBuilder({
    optionGroups,
    onChange,
    maxOptions = 3,
    combinationCount = 0,
    maxCombinations = 100
}) {
    const { t } = useTranslation()
    const [draftValues, setDraftValues] = useState({})
    const tx = (key, fallback) => {
        const value = t(key)
        return value === key ? fallback : value
    }

    const hasLimitWarning = combinationCount > maxCombinations
    const helperText = useMemo(() => {
        if (hasLimitWarning) {
            return tx('variants.combinationLimitWarning', `Maximum ${maxCombinations} combinations allowed.`)
        }

        return tx('variants.combinationCount', 'Generated combinations')
    }, [hasLimitWarning, maxCombinations, t])

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
        const candidateValues = uniqueValues([...(group?.values || []), draftValue.trim()])

        updateGroup(groupId, { values: candidateValues })
        setDraftValues(prev => ({ ...prev, [groupId]: '' }))
    }

    const removeValue = (groupId, valueToRemove) => {
        onChange(optionGroups.map((group) => (
            group.id === groupId
                ? { ...group, values: group.values.filter(value => value !== valueToRemove) }
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
                {optionGroups.map((group, index) => (
                    <div key={group.id} className="rounded-2xl border border-gray-200 bg-gray-50 p-4">
                        <div className="mb-3 flex items-center justify-between gap-3">
                            <div className="flex-1">
                                <label className="label">
                                    {tx('variants.optionName', 'Option Name')} {index + 1}
                                </label>
                                <input
                                    type="text"
                                    value={group.name}
                                    onChange={(e) => updateGroup(group.id, { name: e.target.value })}
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
                                {group.values.map((value) => (
                                    <span key={value} className="inline-flex items-center gap-2 rounded-full bg-primary-50 px-3 py-1 text-sm font-medium text-primary-700">
                                        {value}
                                        <button
                                            type="button"
                                            onClick={() => removeValue(group.id, value)}
                                            className="text-primary-500 transition hover:text-primary-700"
                                        >
                                            x
                                        </button>
                                    </span>
                                ))}

                                <input
                                    type="text"
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
                        </div>
                    </div>
                ))}
            </div>

            <div className={`rounded-xl border px-4 py-3 text-sm ${hasLimitWarning ? 'border-amber-300 bg-amber-50 text-amber-800' : 'border-blue-200 bg-blue-50 text-blue-700'}`}>
                <div className="font-medium">{helperText}</div>
                <div className="mt-1">
                    {combinationCount} / {maxCombinations}
                </div>
            </div>
        </div>
    )
}
