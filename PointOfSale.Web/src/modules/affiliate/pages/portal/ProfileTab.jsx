/**
 * AF-15: Profile/Settings Tab — Profile info + referral code
 * AF-16: Payout Wallet Management
 */
import { useState, useEffect, useCallback, useRef } from 'react'
import { motion } from 'framer-motion'
import affiliateApi from '../../../../services/affiliateApi'
import { User, Copy, Check, Mail, Hash, Calendar, Shield, Wallet, Edit2, Save, X } from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'

const WALLET_TYPES = [
    { value: 'VodafoneCash', label: 'Vodafone Cash' },
    { value: 'OrangeMoney', label: 'Orange Money' },
    { value: 'EtisalatCash', label: 'Etisalat Cash' },
    { value: 'Bank', label: 'Bank Account' },
    { value: 'InstaPay', label: 'InstaPay' },
    { value: 'Other', label: 'Other' },
]

export default function ProfileTab() {
    const { t } = useLanguage()
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [copied, setCopied] = useState(false)
    
    // AF-16: Payout Wallet State
    const [editingWallet, setEditingWallet] = useState(false)
    const [walletForm, setWalletForm] = useState({ walletNumber: '', walletType: 'VodafoneCash' })
    const [savingWallet, setSavingWallet] = useState(false)
    const savingWalletRef = useRef(false)
    const [walletError, setWalletError] = useState(null)
    const [walletSuccess, setWalletSuccess] = useState(false)

    useEffect(() => {
        void fetchProfile().catch(() => {})
    }, [])
    
    const applyBackendProfile = (profile) => {
        setData(profile)
        setWalletForm({
            walletNumber: profile.payoutWalletNumber || '',
            walletType: profile.payoutWalletType || 'VodafoneCash',
        })
    }

    const fetchProfile = async () => {
        try {
            const profile = await affiliateApi.getProfile()
            applyBackendProfile(profile)
            return profile
        } catch {
            setData(null)
            throw new Error('Unable to load accepted backend profile')
        } finally {
            setLoading(false)
        }
    }

    const copyReferralCode = useCallback(() => {
        if (!data?.referralCode) return
        navigator.clipboard.writeText(data.referralCode).then(() => {
            setCopied(true)
            setTimeout(() => setCopied(false), 2000)
        })
    }, [data])
    
    // AF-16: Payout Wallet Handlers
    const handleSaveWallet = async () => {
        if (savingWalletRef.current) return

        setWalletError(null)
        
        if (!walletForm.walletNumber.trim()) {
            setWalletError('Wallet number is required')
            return
        }
        
        if (walletForm.walletNumber.length < 6 || walletForm.walletNumber.length > 32) {
            setWalletError('Wallet number must be 6-32 characters')
            return
        }
        
        savingWalletRef.current = true
        setSavingWallet(true)
        try {
            await affiliateApi.updatePayoutWallet(walletForm.walletNumber, walletForm.walletType)
            await fetchProfile()
            setWalletSuccess(true)
            setEditingWallet(false)
            setTimeout(() => setWalletSuccess(false), 3000)
        } catch (err) {
            setWalletError(err?.response?.data?.message || 'Failed to update wallet')
        } finally {
            savingWalletRef.current = false
            setSavingWallet(false)
        }
    }
    
    const handleCancelWallet = () => {
        setEditingWallet(false)
        setWalletError(null)
        setWalletForm({
            walletNumber: data?.payoutWalletNumber || '',
            walletType: data?.payoutWalletType || 'VodafoneCash',
        })
    }
    
    const maskWalletNumber = (number) => {
        if (!number || number.length <= 6) return '***'
        const first3 = number.substring(0, 3)
        const last3 = number.substring(number.length - 3)
        return `${first3}*****${last3}`
    }

    if (loading) {
        return (
            <div className="max-w-lg mx-auto space-y-4">
                {[...Array(3)].map((_, i) => (
                    <div key={i} className="h-20 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] animate-pulse" />
                ))}
            </div>
        )
    }

    if (!data) {
        return (
            <div className="text-center py-20">
                <User className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">{t('portal.profileTab.notFound')}</h3>
                <p className="text-gray-500 text-sm">{t('portal.profileTab.notFoundDesc')}</p>
            </div>
        )
    }

    const fields = [
        { icon: User, label: t('portal.profileTab.displayName'), value: data.displayName || '—' },
        { icon: Mail, label: t('portal.profileTab.email'), value: data.email || '—' },
        { icon: Shield, label: t('portal.profileTab.status'), value: data.status || 'Active' },
        { icon: Calendar, label: t('portal.profileTab.memberSince'), value: data.createdAt ? new Date(data.createdAt).toLocaleDateString() : '—' },
    ]

    return (
        <div className="max-w-lg mx-auto space-y-6">
            {/* Avatar + Name */}
            <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                className="flex items-center gap-4 p-5 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02]"
            >
                <div className="w-14 h-14 rounded-2xl bg-gradient-to-br from-[#6C63FF]/20 to-[#8B5CF6]/20 border border-[#6C63FF]/20 flex items-center justify-center">
                    <User className="w-7 h-7 text-[#6C63FF]" />
                </div>
                <div>
                    <p className="text-lg font-semibold text-gray-900 dark:text-white">{data.displayName || 'Affiliate'}</p>
                    <p className="text-sm text-gray-500">{data.email}</p>
                </div>
            </motion.div>

            {/* Referral Code */}
            <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.08 }}
                className="p-5 rounded-xl border border-[#6C63FF]/20 bg-[#6C63FF]/5"
            >
                <div className="flex items-center gap-2 mb-3">
                    <Hash className="w-4 h-4 text-[#6C63FF]" />
                    <span className="text-xs font-medium text-[#6C63FF] uppercase tracking-wider">{t('portal.profileTab.referralCode')}</span>
                </div>
                <div className="flex items-center gap-3">
                    <code className="flex-1 text-2xl font-bold font-mono text-gray-900 dark:text-white tracking-widest">
                        {data.referralCode || '—'}
                    </code>
                    <button
                        onClick={copyReferralCode}
                        className={`p-3 rounded-lg transition-all ${copied
                            ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                            : 'bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20 hover:bg-[#6C63FF]/20'
                            }`}
                    >
                        {copied ? <Check className="w-5 h-5" /> : <Copy className="w-5 h-5" />}
                    </button>
                </div>
            </motion.div>

            {/* Profile Fields */}
            <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.16 }}
                className="rounded-xl border border-gray-200 dark:border-white/[0.06] overflow-hidden"
            >
                {fields.map((field, i) => {
                    const Icon = field.icon
                    return (
                        <div key={i} className={`flex items-center gap-4 px-5 py-4 ${i > 0 ? 'border-t border-white/[0.04]' : ''}`}>
                            <div className="w-9 h-9 rounded-lg bg-white dark:bg-white/[0.04] flex items-center justify-center flex-shrink-0">
                                <Icon className="w-4 h-4 text-gray-500" />
                            </div>
                            <div>
                                <p className="text-[10px] font-medium text-gray-500 uppercase tracking-widest">{field.label}</p>
                                <p className="text-sm text-gray-900 dark:text-white mt-0.5">{field.value}</p>
                            </div>
                        </div>
                    )
                })}
            </motion.div>

            {/* AF-16: Payout Wallet Section */}
            <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.24 }}
                className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 overflow-hidden"
            >
                <div className="px-5 py-4 border-b border-white/[0.04]">
                    <div className="flex items-center justify-between">
                        <div className="flex items-center gap-3">
                            <div className="w-9 h-9 rounded-lg bg-emerald-500/10 flex items-center justify-center">
                                <Wallet className="w-4 h-4 text-emerald-400" />
                            </div>
                            <div>
                                <p className="text-sm font-semibold text-gray-900 dark:text-white">Payout Wallet</p>
                                <p className="text-xs text-gray-500 mt-0.5">Where you receive commission payments</p>
                            </div>
                        </div>
                        {!editingWallet && (
                            <button
                                aria-label="Edit payout wallet"
                                onClick={() => setEditingWallet(true)}
                                className="p-2 rounded-lg bg-emerald-500/10 text-emerald-400 hover:bg-emerald-500/20 transition-colors"
                            >
                                <Edit2 className="w-4 h-4" />
                            </button>
                        )}
                    </div>
                </div>

                <div className="px-5 py-4">
                    {walletSuccess && (
                        <div className="mb-4 p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-sm flex items-center gap-2">
                            <Check className="w-4 h-4" />
                            Wallet updated successfully
                        </div>
                    )}

                    {!editingWallet && walletError && (
                        <div className="mb-4 p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-sm flex items-center gap-2">
                            <X className="w-4 h-4" />
                            {walletError}
                        </div>
                    )}

                    {!editingWallet ? (
                        // Display Mode
                        data?.payoutWalletNumber ? (
                            <div className="space-y-3">
                                <div className="flex items-center justify-between">
                                    <span className="text-xs text-gray-500 uppercase tracking-wider">Type</span>
                                    <span className="text-sm text-gray-900 dark:text-white font-medium">{data.payoutWalletType}</span>
                                </div>
                                <div className="flex items-center justify-between">
                                    <span className="text-xs text-gray-500 uppercase tracking-wider">Number</span>
                                    <code className="text-sm text-gray-900 dark:text-white font-mono bg-white dark:bg-white/[0.04] px-3 py-1 rounded">
                                        {maskWalletNumber(data.payoutWalletNumber)}
                                    </code>
                                </div>
                                {data.payoutWalletUpdatedAtUtc && (
                                    <div className="text-xs text-gray-500 text-center pt-2 border-t border-white/[0.04]">
                                        Last updated: {new Date(data.payoutWalletUpdatedAtUtc).toLocaleDateString()}
                                    </div>
                                )}
                            </div>
                        ) : (
                            <div className="text-center py-6">
                                <Wallet className="w-10 h-10 text-gray-600 mx-auto mb-3" />
                                <p className="text-sm text-gray-500">No payout wallet configured</p>
                                <p className="text-xs text-gray-600 mt-1">Click edit to add your wallet</p>
                            </div>
                        )
                    ) : (
                        // Edit Mode
                        <div className="space-y-4">
                            {walletError && (
                                <div className="p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-sm flex items-center gap-2">
                                    <X className="w-4 h-4" />
                                    {walletError}
                                </div>
                            )}

                            <div>
                                <label htmlFor="affiliate-wallet-type" className="block text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wider mb-2">
                                    Wallet Type
                                </label>
                                <select
                                    id="affiliate-wallet-type"
                                    value={walletForm.walletType}
                                    onChange={(e) => setWalletForm({ ...walletForm, walletType: e.target.value })}
                                    className="w-full px-3 py-2 rounded-lg bg-white dark:bg-white/[0.04] border border-gray-200 dark:border-white/[0.06] text-gray-900 dark:text-white text-sm focus:outline-none focus:ring-2 focus:ring-emerald-500/50"
                                >
                                    {WALLET_TYPES.map(type => (
                                        <option key={type.value} value={type.value} className="bg-gray-900">
                                            {type.label}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            <div>
                                <label htmlFor="affiliate-wallet-number" className="block text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wider mb-2">
                                    Wallet Number
                                </label>
                                <input
                                    id="affiliate-wallet-number"
                                    type="text"
                                    value={walletForm.walletNumber}
                                    onChange={(e) => setWalletForm({ ...walletForm, walletNumber: e.target.value })}
                                    placeholder="Enter your wallet number"
                                    className="w-full px-3 py-2 rounded-lg bg-white dark:bg-white/[0.04] border border-gray-200 dark:border-white/[0.06] text-gray-900 dark:text-white text-sm placeholder-gray-600 focus:outline-none focus:ring-2 focus:ring-emerald-500/50"
                                    maxLength={32}
                                />
                                <p className="text-xs text-gray-600 mt-1">6-32 characters, letters and digits only</p>
                            </div>

                            <div className="flex gap-2 pt-2">
                                <button
                                    onClick={handleSaveWallet}
                                    disabled={savingWallet}
                                    className="flex-1 px-4 py-2 rounded-lg bg-emerald-500 text-gray-900 dark:text-white text-sm font-medium hover:bg-emerald-600 transition-colors disabled:opacity-50 flex items-center justify-center gap-2"
                                >
                                    {savingWallet ? (
                                        <>
                                            <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                                            Saving...
                                        </>
                                    ) : (
                                        <>
                                            <Save className="w-4 h-4" />
                                            Save Wallet
                                        </>
                                    )}
                                </button>
                                <button
                                    onClick={handleCancelWallet}
                                    disabled={savingWallet}
                                    className="px-4 py-2 rounded-lg bg-white dark:bg-white/[0.04] text-gray-600 dark:text-gray-400 text-sm font-medium hover:bg-white/[0.08] transition-colors disabled:opacity-50"
                                >
                                    Cancel
                                </button>
                            </div>

                            <div className="p-3 rounded-lg bg-blue-500/10 border border-blue-500/20 text-xs text-blue-400">
                                <strong>Security:</strong> Your wallet info is encrypted and only visible to you and admins during payouts.
                            </div>
                        </div>
                    )}
                </div>
            </motion.div>
        </div>
    )
}
