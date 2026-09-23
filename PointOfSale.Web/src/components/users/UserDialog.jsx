import { useEffect, useRef } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../../hooks/useRTL'

export default function UserDialog({ title, onClose, children, wide = false }) {
    const { t } = useTranslation()
    const { dir, isRTL } = useRTL()
    const returnFocusRef = useRef(typeof document !== 'undefined' ? document.activeElement : null)

    useEffect(() => () => {
        const returnFocus = returnFocusRef.current
        window.requestAnimationFrame(() => {
            if (!document.querySelector('[role="dialog"]') && returnFocus?.isConnected) {
                returnFocus.focus()
            }
        })
    }, [])

    return (
        <Dialog.Root open onOpenChange={(open) => { if (!open) onClose() }}>
            <Dialog.Portal>
                <Dialog.Overlay className="fixed inset-0 z-50 bg-black/50" />
                <Dialog.Content
                    dir={dir}
                    aria-describedby={undefined}
                    className={`fixed left-1/2 top-1/2 z-50 max-h-[90vh] w-[calc(100%-2rem)] -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-xl bg-white p-6 shadow-xl focus:outline-none ${wide ? 'max-w-2xl' : 'max-w-lg'}`}
                >
                    <div className={`mb-4 flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <Dialog.Title className="text-lg font-bold text-gray-800">{title}</Dialog.Title>
                        <Dialog.Close asChild>
                            <button
                                type="button"
                                aria-label={t('common.close')}
                                className="rounded-lg px-2 py-1 text-xl leading-none text-gray-500 hover:bg-gray-100 hover:text-gray-800"
                            >
                                ×
                            </button>
                        </Dialog.Close>
                    </div>
                    {children}
                </Dialog.Content>
            </Dialog.Portal>
        </Dialog.Root>
    )
}
