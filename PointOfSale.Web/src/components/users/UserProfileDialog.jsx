import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import UserDialog from './UserDialog'
import { getPosUser, updatePosUserProfile } from '../../services/userManagementApi'
import { isVersionConflict, userErrorKey } from './userErrors'

const inputClass = 'w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500'

export default function UserProfileDialog({ user, onClose, onSaved }) {
    const { t } = useTranslation()
    const [form, setForm] = useState({
        firstName: user.firstName || '',
        lastName: user.lastName || '',
        email: user.email || '',
        phone: user.phone || '',
    })
    const [version, setVersion] = useState(user.version)
    const [error, setError] = useState('')
    const [serverUser, setServerUser] = useState(null)
    const [submitting, setSubmitting] = useState(false)

    const set = (key) => (event) => {
        setForm((current) => ({ ...current, [key]: event.target.value }))
    }

    const submit = async (event) => {
        event.preventDefault()
        if (submitting) return
        setSubmitting(true)
        setError('')
        try {
            await updatePosUserProfile(user.id, {
                firstName: form.firstName.trim(),
                lastName: form.lastName.trim(),
                email: form.email.trim(),
                phone: form.phone.trim() || undefined,
            }, version)
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
        <UserDialog title={t('userManagement.profileDialog.title')} onClose={onClose}>
            {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
            {serverUser && (
                <div className="mb-4 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
                    <p className="font-medium">{t('userManagement.conflict.currentServerValues')}</p>
                    <p>{serverUser.firstName} {serverUser.lastName}</p>
                    <p>{serverUser.email}</p>
                    <p>{serverUser.phone || t('userManagement.table.unknown')}</p>
                </div>
            )}
            <form onSubmit={submit} className="space-y-4">
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                    <label className="block text-sm font-medium text-gray-700">
                        {t('userManagement.profileDialog.firstName')}
                        <input required maxLength={100} value={form.firstName} onChange={set('firstName')} className={inputClass} />
                    </label>
                    <label className="block text-sm font-medium text-gray-700">
                        {t('userManagement.profileDialog.lastName')}
                        <input required maxLength={100} value={form.lastName} onChange={set('lastName')} className={inputClass} />
                    </label>
                </div>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.profileDialog.email')}
                    <input required type="email" maxLength={200} value={form.email} onChange={set('email')} className={inputClass} />
                </label>
                <p className="text-xs text-gray-500">{t('userManagement.profileDialog.emailChangeNotice')}</p>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.profileDialog.phone')}
                    <input maxLength={50} value={form.phone} onChange={set('phone')} className={inputClass} />
                </label>
                <div className="flex justify-end gap-2">
                    <button type="button" onClick={onClose} className="rounded-lg border px-4 py-2 text-gray-600 hover:bg-gray-50">
                        {t('common.cancel')}
                    </button>
                    <button
                        type="submit"
                        disabled={submitting}
                        className="rounded-lg bg-indigo-600 px-4 py-2 text-white hover:bg-indigo-700 disabled:opacity-50"
                    >
                        {submitting ? t('userManagement.profileDialog.submitting') : t('userManagement.profileDialog.submit')}
                    </button>
                </div>
            </form>
        </UserDialog>
    )
}
