import { Loader2, RotateCcw, Save } from 'lucide-react'

export function SaveBar({
    saving,
    disabled,
    dirty,
    successMessage,
    onSave,
    onCancel,
}: {
    saving: boolean
    disabled: boolean
    dirty: boolean
    successMessage?: string | null
    onSave: () => void
    onCancel: () => void
}) {
    return (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white px-5 py-4 shadow-sm">
            <p className="text-sm text-slate-600">
                {successMessage ?? (dirty ? 'Unsaved changes are ready to save.' : 'No unsaved changes.')}
            </p>
            <div className="flex flex-wrap items-center gap-2">
                <button
                    type="button"
                    disabled={!dirty || saving}
                    onClick={onCancel}
                    className="inline-flex items-center gap-2 rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
                >
                    <RotateCcw className="h-4 w-4" />
                    Cancel Changes
                </button>
                <button
                    type="button"
                    disabled={disabled || saving || !dirty}
                    onClick={onSave}
                    className="inline-flex items-center gap-2 rounded-xl bg-slate-950 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
                >
                    {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                    Save
                </button>
            </div>
        </div>
    )
}
