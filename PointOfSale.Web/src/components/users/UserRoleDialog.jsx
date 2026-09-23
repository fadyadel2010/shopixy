import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import UserDialog from './UserDialog'
import { changePosUserRole, getPosUser } from '../../services/userManagementApi'
import { isVersionConflict, userErrorKey } from './userErrors'

const inputClass = 'w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500'

export default function UserRoleDialog({ user, roles, onClose, onSaved }) {
    const { t } = useTranslation()
    const [newRole, setNewRole] = useState('')
    const [version, setVersion] = useState(user.version)
    const [error, setError] = useState('')
    const [serverUser, setServerUser] = useState(null)
    const [submitting, setSubmitting] = useState(false)

    const assignableRoles = (roles || []).filter((role) => role.assignableByActor && role.name !== user.role)
    const selectedRole = (roles || []).find((role) => role.name === newRole)

    const submit = async (event) => {
        event.preventDefault()
        if (submitting || !newRole) return
        setSubmitting(true)
        setError('')
        try {
            await changePosUserRole(user.id, newRole, version)
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
        <UserDialog title={t('userManagement.roleDialog.title')} onClose={onClose}>
            {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
            {serverUser && (
                <p className="mb-4 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
                    {t('userManagement.conflict.currentServerValues')}: {t(`userManagement.roles.${serverUser.role}`)}
                </p>
            )}
            <form onSubmit={submit} className="space-y-4">
                <div className="rounded-lg bg-gray-50 p-3 text-sm text-gray-700">
                    <span className="font-medium">{t('userManagement.roleDialog.currentRole')}: </span>
                    {t(`userManagement.roles.${user.role}`)}
                    {newRole && (
                        <>
                            <span className="mx-2">→</span>
                            <span className="font-medium">{t('userManagement.roleDialog.newRole')}: </span>
                            {t(`userManagement.roles.${newRole}`)}
                        </>
                    )}
                </div>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.roleDialog.newRole')}
                    <select required value={newRole} onChange={(event) => setNewRole(event.target.value)} className={inputClass}>
                        <option value="">{t('userManagement.filters.allRoles')}</option>
                        {assignableRoles.map((role) => (
                            <option key={role.name} value={role.name}>{t(`userManagement.roles.${role.name}`)}</option>
                        ))}
                    </select>
                </label>
                {selectedRole && (
                    <div className="rounded-lg border border-gray-200 p-3 text-sm">
                        <p className="mb-1 font-medium text-gray-700">{t('userManagement.roleDialog.accessSummary')}</p>
                        <p className="text-xs text-gray-500">
                            {t('userManagement.roleDialog.permissionCount', { count: selectedRole.permissions.length })}
                        </p>
                    </div>
                )}
                <p className="rounded-lg bg-amber-50 p-3 text-sm text-amber-800">{t('userManagement.roleDialog.sessionWarning')}</p>
                <p className="text-xs text-gray-500">{t('userManagement.roleDialog.ownerHidden')}</p>
                <div className="flex justify-end gap-2">
                    <button type="button" onClick={onClose} className="rounded-lg border px-4 py-2 text-gray-600 hover:bg-gray-50">
                        {t('common.cancel')}
                    </button>
                    <button
                        type="submit"
                        disabled={submitting || !newRole}
                        className="rounded-lg bg-indigo-600 px-4 py-2 text-white hover:bg-indigo-700 disabled:opacity-50"
                    >
                        {submitting ? t('userManagement.roleDialog.submitting') : t('userManagement.roleDialog.submit')}
                    </button>
                </div>
            </form>
        </UserDialog>
    )
}
