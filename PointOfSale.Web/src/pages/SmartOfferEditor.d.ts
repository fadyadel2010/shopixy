import type { ComponentType } from 'react'
declare const SmartOfferEditor: ComponentType<{ open: boolean; offer?: Record<string, unknown>; saving: boolean; error: string; onClose: () => void; onSave: (payload: Record<string, unknown>) => void }>
export default SmartOfferEditor
