import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import UserDialog from './UserDialog'
import { transferPosOwnership } from '../../services/userManagementApi'
import { userErrorKey } from './userErrors'

const inputClass = 'w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500'

export default function UserOwnershipDialog({ users, currentOwner, onClose, onSaved }) {
    const { t } = useTranslation()
    const [targetId, setTargetId] = useState('')
    const [typedConfirm, setTypedConfirm] = useState('')
    const [error, setError] = useState('')
    const [submitting, setSubmitting] = useState(false)

    const eligible = (users || []).filter(
        (user) => user.id !== currentOwner?.id && user.role !== 'Owner' && user.status === 'Active',
    )
    const target = eligible.find((user) => user.id === targetId)
    const confirmationWord = t('userManagement.ownershipDialog.confirmationWord')
    const canSubmit = !submitting && target && typedConfirm === confirmationWord

    const submit = async (event) => {
        event.preventDefault()
        if (!canSubmit) return
        setSubmitting(true)
        setError('')
        try {
            await transferPosOwnership(targetId, target.version, currentOwner.version)
            onSaved()
            onClose()
        } catch (err) {
            setError(t(`userManagement.${userErrorKey(err)}`))
        } finally {
            setSubmitting(false)
        }
    }

    return (
        <UserDialog title={t('userManagement.ownershipDialog.title')} onClose={onClose} wide>
            {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
            <p className="mb-4 text-sm text-gray-600">{t('userManagement.ownershipDialog.intro')}</p>
            <div className="mb-4 grid grid-cols-1 gap-4 md:grid-cols-2">
                <div className="rounded-lg bg-gray-50 p-3 text-sm">
                    <p className="font-medium text-gray-500">{t('userManagement.ownershipDialog.currentOwner')}</p>
                    <p className="font-semibold text-gray-800">{currentOwner?.username} ({t('userManagement.roles.Owner')})</p>
                </div>
                <div className="rounded-lg bg-indigo-50 p-3 text-sm">
                    <p className="font-medium text-gray-500">{t('userManagement.ownershipDialog.newOwner')}</p>
                    <p className="font-semibold text-gray-800">
                        {target ? `${target.username} (${t(`userManagement.roles.${target.role}`)} → ${t('userManagement.roles.Owner')})` : '—'}
                    </p>
                </div>
            </div>
            <form onSubmit={submit} className="space-y-4">
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.ownershipDialog.selectUser')}
                    <select required value={targetId} onChange={(event) => setTargetId(event.target.value)} className={inputClass}>
                        <option value="">{t('userManagement.ownershipDialog.selectUser')}</option>
                        {eligible.map((user) => (
                            <option key={user.id} value={user.id}>
                                {user.username} — {t(`userManagement.roles.${user.role}`)}
                            </option>
                        ))}
                    </select>
                </label>
                <p className="text-xs text-gray-500">{t('userManagement.ownershipDialog.targetMustBeActive')}</p>
                <p className="rounded-lg bg-amber-50 p-3 text-sm text-amber-800">
                    {t('userManagement.ownershipDialog.demoteNotice')} {t('userManagement.ownershipDialog.sessionWarning')}
                </p>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.ownershipDialog.confirmLabel')}
                    <input
                        value={typedConfirm}
                        onChange={(event) => setTypedConfirm(event.target.value)}
                        placeholder={confirmationWord}
                        className={inputClass}
                    />
                </label>
                <div className="flex justify-end gap-2">
                    <button type="button" onClick={onClose} className="rounded-lg border px-4 py-2 text-gray-600 hover:bg-gray-50">
                        {t('common.cancel')}
                    </button>
                    <button
                        type="submit"
                        disabled={!canSubmit}
                        className="rounded-lg bg-red-600 px-4 py-2 text-white hover:bg-red-700 disabled:opacity-50"
                    >
                        {submitting ? t('userManagement.ownershipDialog.submitting') : t('userManagement.ownershipDialog.submit')}
                    </button>
                </div>
            </form>
        </UserDialog>
    )
}
