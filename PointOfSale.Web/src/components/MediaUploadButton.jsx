import { useRef } from 'react'

export default function MediaUploadButton({
    label,
    description,
    accept,
    multiple = true,
    disabled = false,
    busy = false,
    onFilesSelected
}) {
    const inputRef = useRef(null)

    const handleButtonClick = () => {
        if (!disabled && !busy) {
            inputRef.current?.click()
        }
    }

    const handleChange = async (event) => {
        const files = Array.from(event.target.files || [])

        if (files.length > 0) {
            await onFilesSelected(files)
        }

        event.target.value = ''
    }

    return (
        <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
            <input
                ref={inputRef}
                type="file"
                accept={accept}
                multiple={multiple}
                onChange={handleChange}
                className="hidden"
                disabled={disabled || busy}
            />

            <button
                type="button"
                onClick={handleButtonClick}
                disabled={disabled || busy}
                className="flex w-full items-center justify-between gap-4 rounded-xl border border-dashed border-gray-300 px-4 py-4 text-left transition hover:border-primary-400 hover:bg-primary-50 disabled:cursor-not-allowed disabled:opacity-60"
            >
                <div>
                    <p className="text-sm font-semibold text-gray-900">{label}</p>
                    {description && <p className="mt-1 text-xs text-gray-500">{description}</p>}
                </div>

                {busy ? (
                    <div className="h-6 w-6 animate-spin rounded-full border-2 border-primary-600 border-t-transparent" />
                ) : (
                    <div className="rounded-full bg-primary-100 p-2 text-primary-700">
                        <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                        </svg>
                    </div>
                )}
            </button>
        </div>
    )
}
