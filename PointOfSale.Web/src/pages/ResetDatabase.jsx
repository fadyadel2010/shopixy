import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useToast } from '../components/Toast'

export default function ResetDatabase() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { showSuccess, showError } = useToast()
    const [isLoading, setIsLoading] = useState(false)
    const [isConfirmed, setIsConfirmed] = useState(false)

    const handleReset = async () => {
        if (!isConfirmed) return

        const confirmed = await confirm({
            confirmLabel: t('databaseReset.resetButton'),
            description: t('databaseReset.warningMessage'),
            title: t('databaseReset.warningTitle'),
            variant: 'danger',
        })

        if (!confirmed) {
            return
        }

        setIsLoading(true)
        try {
            await api.post('/maintenance/reset-database')
            showSuccess(t('databaseReset.successMessage'))
            setIsConfirmed(false)
        } catch (error) {
            console.error('Error resetting database:', error)
            showError(t('databaseReset.failedMessage'))
        } finally {
            setIsLoading(false)
        }
    }

    return (
        <div className="max-w-4xl mx-auto p-6">
            <h1 className="text-3xl font-bold text-gray-900 mb-8">{t('databaseReset.title')}</h1>

            <div className="bg-red-50 border-l-4 border-red-500 p-6 rounded-lg shadow-sm mb-8">
                <div className="flex items-start">
                    <div className="flex-shrink-0">
                        <svg className="h-8 w-8 text-red-500" viewBox="0 0 20 20" fill="currentColor">
                            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                        </svg>
                    </div>
                    <div className="ml-4">
                        <h3 className="text-xl font-bold text-red-800 mb-2">
                            {t('databaseReset.warningTitle')}
                        </h3>
                        <div className="text-red-700 space-y-2">
                            <p className="font-medium text-lg">
                                {t('databaseReset.warningMessage')}
                            </p>
                            <p className="text-sm bg-white/50 p-2 rounded">
                                {t('databaseReset.usersKept')}
                            </p>
                            <p className="text-sm mt-2">
                                {t('databaseReset.instructions')}
                            </p>
                        </div>
                    </div>
                </div>
            </div>

            <div className="bg-white p-8 rounded-lg shadow border border-gray-200">
                <div className="mb-6">
                    <label className="flex items-center gap-3 p-4 border rounded-lg cursor-pointer hover:bg-gray-50 transition-colors">
                        <input
                            type="checkbox"
                            checked={isConfirmed}
                            onChange={(e) => setIsConfirmed(e.target.checked)}
                            className="h-6 w-6 text-red-600 border-gray-300 rounded focus:ring-red-500"
                        />
                        <span className="text-gray-900 font-medium select-none">
                            {t('databaseReset.confirmCheckbox')}
                        </span>
                    </label>
                </div>

                <div className="flex justify-end">
                    <button
                        onClick={handleReset}
                        disabled={!isConfirmed || isLoading}
                        className={`
                            px-8 py-3 rounded-lg text-white font-bold text-lg shadow-lg transform transition-all
                            ${!isConfirmed || isLoading
                                ? 'bg-gray-400 cursor-not-allowed'
                                : 'bg-gradient-to-r from-red-600 to-red-700 hover:from-red-500 hover:to-red-600 hover:-translate-y-1 hover:shadow-xl'
                            }
                        `}
                    >
                        {isLoading ? (
                            <div className="flex items-center gap-2">
                                <svg className="animate-spin h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                </svg>
                                {t('databaseReset.resetting')}
                            </div>
                        ) : (
                            t('databaseReset.resetButton')
                        )}
                    </button>
                </div>
            </div>
        </div>
    )
}
