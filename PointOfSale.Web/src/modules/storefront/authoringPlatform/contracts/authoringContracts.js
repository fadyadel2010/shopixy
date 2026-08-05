export const AUTHORING_ACTION_ORDER = Object.freeze([
    'back',
    'title',
    'status',
    'preview',
    'history',
    'discard',
    'save',
    'publish',
])

export const AUTHORING_STATUS_LABELS = Object.freeze({
    dirty: 'Unsaved changes',
    draft: 'Draft',
    published: 'Published',
    live: 'Live',
    saving: 'Saving...',
    publishing: 'Publishing...',
    error: 'Action required',
    disabled: 'Disabled',
})

export const AUTHORING_BUTTON_TONES = Object.freeze({
    secondary: 'border border-gray-300 bg-white text-gray-700 hover:bg-gray-50',
    primary: 'border border-indigo-600 bg-indigo-600 text-white hover:bg-indigo-700',
    publish: 'border border-emerald-600 bg-emerald-600 text-white hover:bg-emerald-700',
    danger: 'border border-red-300 bg-white text-red-700 hover:bg-red-50',
})
