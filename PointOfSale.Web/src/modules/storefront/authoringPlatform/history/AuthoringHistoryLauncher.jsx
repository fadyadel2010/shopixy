import { History } from 'lucide-react'

import { AuthoringButton } from '../components/AuthoringButton'

export function AuthoringHistoryLauncher({ action }) {
    return <AuthoringButton action={action} defaultLabel="History" defaultIcon={History} />
}
