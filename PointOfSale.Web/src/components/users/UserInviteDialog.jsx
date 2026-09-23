import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import UserDialog from './UserDialog'
import { invitePosUser } from '../../services/userManagementApi'
import { userErrorKey } from './userErrors'

const inputClass = 'w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500'

export default function UserInviteDialog({ roles, onClose, onSaved }) {
    const { t } = useTranslation()
    const [form, setForm] = useState({ firstName: '', lastName: '', username: '', email: '', phone: '', role: '' })
    const [error, setError] = useState('')
    const [submitting, setSubmitting] = useState(false)

    const assignableRoles = (roles || []).filter((role) => role.assignableByActor)

    const set = (key) => (event) => {
        setForm((current) => ({ ...current, [key]: event.target.value }))
    }

    const submit = async (event) => {
        event.preventDefault()
        if (submitting) return
        setSubmitting(true)
        setError('')
        try {
            await invitePosUser({
                firstName: form.firstName.trim(),
                lastName: form.lastName.trim(),
                username: form.username.trim(),
                email: form.email.trim(),
                phone: form.phone.trim() || undefined,
                role: form.role,
            })
            onSaved()
            onClose()
        } catch (err) {
            setError(t(`userManagement.${userErrorKey(err)}`))
        } finally {
            setSubmitting(false)
        }
    }

    return (
        <UserDialog title={t('userManagement.inviteDialog.title')} onClose={onClose}>
            <p className="mb-4 text-sm text-gray-500">{t('userManagement.inviteDialog.hint')}</p>
            {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
            <form onSubmit={submit} className="space-y-4">
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                    <label className="block text-sm font-medium text-gray-700">
                        {t('userManagement.inviteDialog.firstName')}
                        <input required maxLength={100} value={form.firstName} onChange={set('firstName')} className={inputClass} />
                    </label>
                    <label className="block text-sm font-medium text-gray-700">
                        {t('userManagement.inviteDialog.lastName')}
                        <input required maxLength={100} value={form.lastName} onChange={set('lastName')} className={inputClass} />
                    </label>
                </div>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.inviteDialog.username')}
                    <input required maxLength={100} value={form.username} onChange={set('username')} className={inputClass} />
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.inviteDialog.email')}
                    <input required type="email" maxLength={200} value={form.email} onChange={set('email')} className={inputClass} />
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.inviteDialog.phone')}
                    <input maxLength={50} value={form.phone} onChange={set('phone')} className={inputClass} />
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.inviteDialog.role')}
                    <select required value={form.role} onChange={set('role')} className={inputClass}>
                        <option value="">{t('userManagement.filters.allRoles')}</option>
                        {assignableRoles.map((role) => (
                            <option key={role.name} value={role.name}>{t(`userManagement.roles.${role.name}`)}</option>
                        ))}
                    </select>
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
                        {submitting ? t('userManagement.inviteDialog.submitting') : t('userManagement.inviteDialog.submit')}
                    </button>
                </div>
            </form>
        </UserDialog>
    )
}
