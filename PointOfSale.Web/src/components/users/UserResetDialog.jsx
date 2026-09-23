import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import UserDialog from './UserDialog'
import { issuePosUserPasswordReset } from '../../services/userManagementApi'
import { userErrorKey } from './userErrors'

export default function UserResetDialog({ user, onClose }) {
    const { t } = useTranslation()
    const [error, setError] = useState('')
    const [success, setSuccess] = useState(false)
    const [submitting, setSubmitting] = useState(false)

    const submit = async (event) => {
        event.preventDefault()
        if (submitting) return
        setSubmitting(true)
        setError('')
        try {
            await issuePosUserPasswordReset(user.id, user.version)
            setSuccess(true)
        } catch (err) {
            setError(t(`userManagement.${userErrorKey(err)}`))
        } finally {
            setSubmitting(false)
        }
    }

    return (
        <UserDialog title={t('userManagement.resetDialog.title')} onClose={onClose}>
            {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
            {success ? (
                <div>
                    <p role="status" className="mb-4 rounded-lg bg-green-50 p-3 text-sm text-green-800">
                        {t('userManagement.resetDialog.success')}
                    </p>
                    <div className="flex justify-end">
                        <button type="button" onClick={onClose} className="rounded-lg bg-indigo-600 px-4 py-2 text-white hover:bg-indigo-700">
                            {t('common.close')}
                        </button>
                    </div>
                </div>
            ) : (
                <form onSubmit={submit}>
                    <p className="mb-4 text-sm text-gray-600">{t('userManagement.resetDialog.message')}</p>
                    <div className="flex justify-end gap-2">
                        <button type="button" onClick={onClose} className="rounded-lg border px-4 py-2 text-gray-600 hover:bg-gray-50">
                            {t('common.cancel')}
                        </button>
                        <button
                            type="submit"
                            disabled={submitting}
                            className="rounded-lg bg-indigo-600 px-4 py-2 text-white hover:bg-indigo-700 disabled:opacity-50"
                        >
                            {submitting ? t('userManagement.resetDialog.submitting') : t('userManagement.resetDialog.submit')}
                        </button>
                    </div>
                </form>
            )}
        </UserDialog>
    )
}
