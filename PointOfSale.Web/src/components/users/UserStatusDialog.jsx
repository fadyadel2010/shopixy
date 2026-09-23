import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import UserDialog from './UserDialog'
import { changePosUserStatus, getPosUser } from '../../services/userManagementApi'
import { isVersionConflict, userErrorKey } from './userErrors'

const inputClass = 'w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500'

export default function UserStatusDialog({ user, onClose, onSaved }) {
    const { t } = useTranslation()
    const disabling = user.status !== 'Disabled'
    const [reason, setReason] = useState('')
    const [typedConfirm, setTypedConfirm] = useState('')
    const [version, setVersion] = useState(user.version)
    const [error, setError] = useState('')
    const [serverUser, setServerUser] = useState(null)
    const [submitting, setSubmitting] = useState(false)

    const requiresTypedConfirm = disabling && user.role === 'Admin'
    const confirmationWord = t('userManagement.statusDialog.confirmationWord')
    const canSubmit = !submitting && (!disabling || reason.trim()) && (!requiresTypedConfirm || typedConfirm === confirmationWord)

    const submit = async (event) => {
        event.preventDefault()
        if (!canSubmit) return
        setSubmitting(true)
        setError('')
        try {
            await changePosUserStatus(user.id, !disabling, disabling ? reason.trim() : undefined, version)
            onSaved()
            onClose()
        } catch (err) {
            if (isVersionConflict(err)) {
                try {
                    const fresh = await getPosUser(user.id)
                    setVersion(fresh.data.version)
                    setServerUser(fresh.data)
                } catch {
                    // Keep the last known version; the retry will surface the truth.
                }
            }
            setError(t(`userManagement.${userErrorKey(err)}`))
        } finally {
            setSubmitting(false)
        }
    }

    return (
        <UserDialog title={t(disabling ? 'userManagement.statusDialog.disableTitle' : 'userManagement.statusDialog.enableTitle')} onClose={onClose}>
            {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
            {serverUser && (
                <p className="mb-4 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
                    {t('userManagement.conflict.currentServerValues')}: {t(`userManagement.status.${serverUser.status}`)}
                </p>
            )}
            <p className="mb-4 text-sm text-gray-600">
                {t(disabling ? 'userManagement.statusDialog.disableMessage' : 'userManagement.statusDialog.enableMessage')}
            </p>
            <form onSubmit={submit} className="space-y-4">
                {disabling && (
                    <label className="block text-sm font-medium text-gray-700">
                        {t('userManagement.statusDialog.reason')}
                        <textarea
                            required
                            maxLength={500}
                            value={reason}
                            onChange={(event) => setReason(event.target.value)}
                            placeholder={t('userManagement.statusDialog.reasonPlaceholder')}
                            className={inputClass}
                            rows={3}
                        />
                    </label>
                )}
                {requiresTypedConfirm && (
                    <div className="rounded-lg bg-red-50 p-3">
                        <p className="mb-2 text-sm font-medium text-red-800">{t('userManagement.statusDialog.adminTargetWarning')}</p>
                        <input
                            value={typedConfirm}
                            onChange={(event) => setTypedConfirm(event.target.value)}
                            placeholder={confirmationWord}
                            className={inputClass}
                            aria-label={t('userManagement.statusDialog.adminTargetWarning')}
                        />
                    </div>
                )}
                <div className="flex justify-end gap-2">
                    <button type="button" onClick={onClose} className="rounded-lg border px-4 py-2 text-gray-600 hover:bg-gray-50">
                        {t('common.cancel')}
                    </button>
                    <button
                        type="submit"
                        disabled={!canSubmit}
                        className={`rounded-lg px-4 py-2 text-white disabled:opacity-50 ${disabling ? 'bg-red-600 hover:bg-red-700' : 'bg-green-600 hover:bg-green-700'}`}
                    >
                        {submitting
                            ? t('userManagement.statusDialog.submitting')
                            : t(disabling ? 'userManagement.statusDialog.confirmDisable' : 'userManagement.statusDialog.confirmEnable')}
                    </button>
                </div>
            </form>
        </UserDialog>
    )
}
