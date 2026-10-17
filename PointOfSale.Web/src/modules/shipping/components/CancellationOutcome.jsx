import { useTranslation } from 'react-i18next'
import { admitCancellationOutcome } from '../cancellationOutcome'

export default function CancellationOutcome({ result }) {
    const { t } = useTranslation()
    if (!result) return null
    const admitted = admitCancellationOutcome(result)
    return <div role="status" className={`my-3 rounded-lg border p-3 text-sm ${admitted.outcome === 'Confirmed' ? 'bg-green-50 text-green-800' : 'bg-amber-50 text-amber-900'}`}>
        {t(`shipping.cancellation${admitted.outcome}`)}
    </div>
}
