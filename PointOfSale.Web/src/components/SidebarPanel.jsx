import { useState } from 'react'

export default function SidebarPanel({
    title,
    subtitle,
    defaultOpen = true,
    children,
    className = '',
    bodyClassName = ''
}) {
    const [isOpen, setIsOpen] = useState(defaultOpen)

    return (
        <section className={`overflow-hidden rounded-2xl border border-gray-200 bg-white shadow-sm ${className}`}>
            <button
                type="button"
                onClick={() => setIsOpen(prev => !prev)}
                className="flex w-full items-center justify-between gap-4 px-4 py-4 text-left transition hover:bg-gray-50"
            >
                <div>
                    <h3 className="text-sm font-semibold text-gray-900">{title}</h3>
                    {subtitle && (
                        <p className="mt-1 text-xs text-gray-500">{subtitle}</p>
                    )}
                </div>
                <svg
                    className={`h-4 w-4 flex-shrink-0 text-gray-400 transition-transform ${isOpen ? 'rotate-180' : ''}`}
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                </svg>
            </button>

            {isOpen && (
                <div className={`border-t border-gray-100 p-4 ${bodyClassName}`}>
                    {children}
                </div>
            )}
        </section>
    )
}
