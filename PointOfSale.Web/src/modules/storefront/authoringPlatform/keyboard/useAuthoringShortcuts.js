import { useEffect } from 'react'

export function useAuthoringShortcuts(shortcuts = []) {
    useEffect(() => {
        const handleShortcut = (event) => {
            const key = event.key.toLowerCase()
            const shortcut = shortcuts.find((candidate) => (
                candidate
                && candidate.key.toLowerCase() === key
                && Boolean(candidate.shiftKey) === event.shiftKey
                && (!candidate.ctrlOrMeta || event.ctrlKey || event.metaKey)
                && candidate.disabled !== true
            ))

            if (!shortcut) return
            event.preventDefault()
            shortcut.onInvoke(event)
        }

        document.addEventListener('keydown', handleShortcut)
        return () => document.removeEventListener('keydown', handleShortcut)
    }, [shortcuts])
}
