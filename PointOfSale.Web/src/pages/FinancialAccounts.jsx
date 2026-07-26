import { useState, useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useToast } from '../components/Toast'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'

export default function FinancialAccounts() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const [searchParams, setSearchParams] = useSearchParams()
    const currentTab = searchParams.get('tab') || 'main'

    const [accounts, setAccounts] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const [showModal, setShowModal] = useState(false)
    const [editingAccount, setEditingAccount] = useState(null)

    // Form state
    const [formData, setFormData] = useState({
        name: '',
        accountNumber: '',
        parentAccountId: ''
    })

    useEffect(() => {
        fetchAccounts()
    }, [])

    const fetchAccounts = async () => {
        try {
            setLoading(true)
            const response = await api.get('/financialaccounts')
            setAccounts(response.data)
        } catch (err) {
            setError(err.message)
        } finally {
            setLoading(false)
        }
    }

    // Get main accounts (accounts without parent - check for null, undefined, empty string, and "00000000-0000-0000-0000-000000000000")
    const isMainAccount = (account) => {
        const parentId = account.parentAccountId
        return !parentId || parentId === '00000000-0000-0000-0000-000000000000'
    }

    const mainAccounts = accounts.filter(account => isMainAccount(account))

    // Get sub accounts (accounts with parent)
    const subAccounts = accounts.filter(account => !isMainAccount(account))

    const filteredAccounts = currentTab === 'main' ? mainAccounts : subAccounts

    const handleSubmit = async (e) => {
        e.preventDefault()

        // Validation for sub accounts - must have parent
        if (currentTab === 'sub' && !editingAccount && !formData.parentAccountId) {
            showError(t('accounts.parentAccountRequired'))
            return
        }

        try {
            // For main accounts (when on main tab and creating new), always set parentAccountId to null
            const parentId = currentTab === 'main' && !editingAccount
                ? null
                : (formData.parentAccountId || null)

            const payload = {
                name: formData.name,
                accountNumber: formData.accountNumber,
                parentAccountId: parentId
            }

            if (editingAccount) {
                await api.put(`/financialaccounts/${editingAccount.id}`, payload)
            } else {
                await api.post('/financialaccounts', payload)
            }

            await fetchAccounts()
            setShowModal(false)
            setEditingAccount(null)
            setFormData({ name: '', accountNumber: '', parentAccountId: '' })

            const successMessage = currentTab === 'main'
                ? (editingAccount ? t('accounts.mainAccountUpdated') : t('accounts.mainAccountCreated'))
                : (editingAccount ? t('accounts.subAccountUpdated') : t('accounts.subAccountCreated'))
            showSuccess(successMessage)
        } catch (err) {
            showError(err.response?.data?.message || t('accounts.failedToSaveAccount'))
        }
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('accounts.deleteAccountConfirm'),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            await api.delete(`/financialaccounts/${id}`)
            await fetchAccounts()
            showSuccess(t('accounts.accountDeleted'))
        } catch (err) {
            showError(err.response?.data?.message || t('accounts.failedToDeleteAccount'))
        }
    }

    const openModal = (account = null) => {
        if (account) {
            setEditingAccount(account)
            setFormData({
                name: account.name,
                accountNumber: account.accountNumber || '',
                parentAccountId: account.parentAccountId || ''
            })
        } else {
            setEditingAccount(null)
            setFormData({
                name: '',
                accountNumber: '',
                // For sub accounts, pre-select first main account if available
                parentAccountId: currentTab === 'sub' && mainAccounts.length > 0 ? mainAccounts[0].id : ''
            })
        }
        setShowModal(true)
    }

    // Determine modal title based on current tab and whether editing
    const getModalTitle = () => {
        if (currentTab === 'main') {
            return editingAccount ? t('accounts.editMainAccount') : t('accounts.newMainAccount')
        } else {
            return editingAccount ? t('accounts.editSubAccount') : t('accounts.newSubAccount')
        }
    }

    // Determine add button label based on current tab
    const getAddButtonLabel = () => {
        return currentTab === 'main' ? t('accounts.addMainAccount') : t('accounts.addSubAccount')
    }

    return (
        <div className="space-y-6">
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-900">
                    {currentTab === 'main' ? t('accounts.mainAccountsTab') : t('accounts.subAccountsTab')}
                </h1>
                <div className="flex gap-2">
                    <div className="flex bg-gray-100 p-1 rounded-lg">
                        <button
                            onClick={() => setSearchParams({ tab: 'main' })}
                            className={`px-4 py-2 rounded-md text-sm font-medium transition-colors ${currentTab === 'main'
                                ? 'bg-white text-primary-600 shadow-sm'
                                : 'text-gray-600 hover:text-gray-900'
                                }`}
                        >
                            {t('accounts.mainAccountsTab')}
                        </button>
                        <button
                            onClick={() => setSearchParams({ tab: 'sub' })}
                            className={`px-4 py-2 rounded-md text-sm font-medium transition-colors ${currentTab === 'sub'
                                ? 'bg-white text-primary-600 shadow-sm'
                                : 'text-gray-600 hover:text-gray-900'
                                }`}
                        >
                            {t('accounts.subAccountsTab')}
                        </button>
                    </div>
                    <button
                        onClick={() => openModal()}
                        className="bg-primary-600 text-white px-4 py-2 rounded-lg hover:bg-primary-700 transition-colors flex items-center gap-2"
                    >
                        {getAddButtonLabel()}
                    </button>
                </div>
            </div>

            {/* Info box explaining the difference */}
            <div className={`p-4 rounded-lg border ${currentTab === 'main' ? 'bg-blue-50 border-blue-200' : 'bg-green-50 border-green-200'}`}>
                <p className={`text-sm ${currentTab === 'main' ? 'text-blue-700' : 'text-green-700'}`}>
                    {currentTab === 'main'
                        ? t('accounts.mainAccountsDescription')
                        : t('accounts.subAccountsDescription')
                    }
                </p>
            </div>

            {loading ? (
                <div className="text-center py-12">{t('common.loading')}</div>
            ) : error ? (
                <div className="text-red-600 text-center py-12">{error}</div>
            ) : filteredAccounts.length === 0 ? (
                <div className="text-center py-12 bg-white rounded-xl shadow-sm border border-gray-200">
                    <div className="text-gray-400 text-5xl mb-4">📂</div>
                    <p className="text-gray-500">
                        {currentTab === 'main' ? t('accounts.noMainAccounts') : t('accounts.noSubAccounts')}
                    </p>
                    <button
                        onClick={() => openModal()}
                        className="mt-4 text-primary-600 hover:text-primary-700 font-medium"
                    >
                        {getAddButtonLabel()}
                    </button>
                </div>
            ) : (
                <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('accounts.accountName')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('accounts.accountNumber')}</th>
                                {currentTab === 'sub' && (
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('accounts.parentAccount')}</th>
                                )}
                                {currentTab === 'main' && (
                                    <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">{t('accounts.subAccountsCount')}</th>
                                )}
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">{t('accounts.balance')}</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="bg-white divide-y divide-gray-200">
                            {filteredAccounts.map((account) => (
                                <tr key={account.id} className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap">
                                        <div className="font-medium text-gray-900">{account.name}</div>
                                        {account.isSystem && (
                                            <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800">
                                                {t('accounts.system')}
                                            </span>
                                        )}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-gray-500">
                                        {account.accountNumber || '-'}
                                    </td>
                                    {currentTab === 'sub' && (
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                                                {account.parentAccountName || '-'}
                                            </span>
                                        </td>
                                    )}
                                    {currentTab === 'main' && (
                                        <td className="px-6 py-4 whitespace-nowrap text-center">
                                            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-primary-100 text-primary-800">
                                                {subAccounts.filter(s => s.parentAccountId === account.id).length}
                                            </span>
                                        </td>
                                    )}
                                    <td className="px-6 py-4 whitespace-nowrap text-right font-medium text-gray-900">
                                        {formatCurrency(account.balance)}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                                        {!account.isSystem && (
                                            <>
                                                <button
                                                    onClick={() => openModal(account)}
                                                    className="text-primary-600 hover:text-primary-900 mr-4"
                                                >
                                                    {t('common.edit')}
                                                </button>
                                                <button
                                                    onClick={() => handleDelete(account.id)}
                                                    className="text-red-600 hover:text-red-900"
                                                >
                                                    {t('common.delete')}
                                                </button>
                                            </>
                                        )}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {/* Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-xl shadow-xl max-w-md w-full p-6">
                        <h2 className="text-xl font-bold text-gray-900 mb-2">
                            {getModalTitle()}
                        </h2>

                        {/* Modal description */}
                        <p className="text-sm text-gray-500 mb-4">
                            {currentTab === 'main' && !editingAccount && t('accounts.mainAccountCreateHint')}
                            {currentTab === 'sub' && !editingAccount && t('accounts.subAccountCreateHint')}
                        </p>

                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-1">{t('accounts.nameRequired')}</label>
                                <input
                                    type="text"
                                    required
                                    value={formData.name}
                                    onChange={e => setFormData({ ...formData, name: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-primary-500 focus:border-primary-500"
                                    placeholder={currentTab === 'main' ? t('accounts.mainAccountNamePlaceholder') : t('accounts.subAccountNamePlaceholder')}
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-1">{t('accounts.accountNumber')}</label>
                                <input
                                    type="text"
                                    value={formData.accountNumber}
                                    onChange={e => setFormData({ ...formData, accountNumber: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-primary-500 focus:border-primary-500"
                                    placeholder={t('accounts.accountNumberPlaceholder')}
                                />
                            </div>

                            {/* Only show Parent Account dropdown for Sub Accounts tab OR when editing a sub account */}
                            {(currentTab === 'sub' || (editingAccount && editingAccount.parentAccountId)) && (
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">
                                        {t('accounts.parentAccountRequired')}
                                    </label>
                                    <select
                                        value={formData.parentAccountId}
                                        onChange={e => setFormData({ ...formData, parentAccountId: e.target.value })}
                                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-primary-500 focus:border-primary-500"
                                        required={currentTab === 'sub'}
                                    >
                                        <option value="">{t('accounts.selectParentAccount')}</option>
                                        {mainAccounts
                                            .filter(a => a.id !== editingAccount?.id)
                                            .map(a => (
                                                <option key={a.id} value={a.id}>{a.name}</option>
                                            ))
                                        }
                                    </select>
                                    {mainAccounts.length === 0 && (
                                        <p className="mt-1 text-sm text-red-500">{t('accounts.noMainAccountsForParent')}</p>
                                    )}
                                </div>
                            )}

                            <div className="flex justify-end gap-3 mt-6">
                                <button
                                    type="button"
                                    onClick={() => setShowModal(false)}
                                    className="px-4 py-2 text-gray-700 hover:bg-gray-100 rounded-lg transition-colors"
                                >
                                    {t('common.cancel')}
                                </button>
                                <button
                                    type="submit"
                                    className="px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
                                    disabled={currentTab === 'sub' && mainAccounts.length === 0}
                                >
                                    {t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}
