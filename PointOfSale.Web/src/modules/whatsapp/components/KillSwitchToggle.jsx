import React, { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertTriangle } from 'lucide-react'

export default function KillSwitchToggle({ isKilled, onActivate, onDeactivate }) {
    const { t } = useTranslation()
    const [reason, setReason] = useState('')
    const [confirming, setConfirming] = useState(false)

    const handleToggle = () => {
        if (isKilled) {
            // Deactivating is safer, just confirm
            if (confirm(t('whatsapp.safety.confirm.deactivate'))) {
                onDeactivate()
            }
        } else {
            // Activating is dangerous, show modal
            setConfirming(true)
        }
    }

    const handleActivate = () => {
        if (!reason.trim()) {
            alert(t('whatsapp.safety.reasonRequired'))
            return
        }
        onActivate(reason)
        setConfirming(false)
        setReason('')
    }

    return (
        <div className="bg-white shadow sm:rounded-lg">
            <div className="px-4 py-5 sm:p-6">
                <h3 className="text-lg leading-6 font-medium text-gray-900">
                    {t('whatsapp.safety.killSwitch')}
                </h3>
                <div className="mt-2 max-w-xl text-sm text-gray-500">
                    <p>
                        {t('whatsapp.safety.description')}
                    </p>
                </div>
                <div className="mt-5 flex items-center">
                    {/* Tailwind Toggle (pure CSS, no headlessui) */}
                    <label className="relative inline-flex items-center cursor-pointer">
                        <input
                            type="checkbox"
                            className="sr-only peer"
                            checked={isKilled}
                            onChange={handleToggle}
                        />
                        <div className={`w-11 h-6 rounded-full peer peer-focus:ring-4 transition-colors duration-200
                            ${isKilled
                                ? 'bg-red-600 peer-focus:ring-red-300'
                                : 'bg-gray-200 peer-focus:ring-gray-300'}
                            after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all
                            ${isKilled ? 'after:translate-x-full after:border-white' : ''}`}
                        ></div>
                    </label>
                    <span className="ml-3 text-sm font-medium text-gray-900">
                        {isKilled ? t('whatsapp.safety.state.activated') : t('whatsapp.safety.state.deactivated')}
                    </span>
                </div>
            </div>

            {/* Confirmation Modal */}
            {confirming && (
                <div className="fixed z-10 inset-0 overflow-y-auto" aria-labelledby="modal-title" role="dialog" aria-modal="true">
                    <div className="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
                        <div className="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" onClick={() => setConfirming(false)}></div>
                        <span className="hidden sm:inline-block sm:align-middle sm:h-screen" aria-hidden="true">&#8203;</span>
                        <div className="inline-block align-bottom bg-white rounded-lg px-4 pt-5 pb-4 text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-lg sm:w-full sm:p-6">
                            <div className="sm:flex sm:items-start">
                                <div className="mx-auto flex-shrink-0 flex items-center justify-center h-12 w-12 rounded-full bg-red-100 sm:mx-0 sm:h-10 sm:w-10">
                                    <AlertTriangle className="h-6 w-6 text-red-600" aria-hidden="true" />
                                </div>
                                <div className="mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left">
                                    <h3 className="text-lg leading-6 font-medium text-gray-900" id="modal-title">
                                        {t('whatsapp.safety.confirm.activateTitle')}
                                    </h3>
                                    <div className="mt-2">
                                        <p className="text-sm text-gray-500">
                                            {t('whatsapp.safety.confirm.activateDescription')}
                                        </p>
                                        <div className="mt-4">
                                            <label htmlFor="reason" className="block text-sm font-medium text-gray-700">
                                                {t('whatsapp.safety.reasonLabel')}
                                            </label>
                                            <input
                                                type="text"
                                                name="reason"
                                                id="reason"
                                                className="shadow-sm focus:ring-red-500 focus:border-red-500 block w-full sm:text-sm border-gray-300 rounded-md mt-1"
                                                placeholder={t('whatsapp.safety.reasonPlaceholder')}
                                                value={reason}
                                                onChange={(e) => setReason(e.target.value)}
                                            />
                                        </div>
                                    </div>
                                </div>
                            </div>
                            <div className="mt-5 sm:mt-4 sm:flex sm:flex-row-reverse">
                                <button
                                    type="button"
                                    className="w-full inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-red-600 text-base font-medium text-white hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 sm:ml-3 sm:w-auto sm:text-sm"
                                    onClick={handleActivate}
                                >
                                    {t('whatsapp.safety.actions.activate')}
                                </button>
                                <button
                                    type="button"
                                    className="mt-3 w-full inline-flex justify-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:mt-0 sm:w-auto sm:text-sm"
                                    onClick={() => setConfirming(false)}
                                >
                                    {t('common.cancel')}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}
