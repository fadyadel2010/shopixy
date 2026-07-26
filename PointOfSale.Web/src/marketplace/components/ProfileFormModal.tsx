import React, { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useFormatting } from '../../hooks/useFormatting'

interface ProfileFormModalProps {
    initialData?: any
    onSubmit: (data: any) => Promise<void>
    onClose: () => void
}

const safeParseStringArray = (value: unknown): string[] => {
    if (Array.isArray(value)) {
        return value.filter((item): item is string => typeof item === 'string')
    }

    if (typeof value !== 'string') {
        return []
    }

    try {
        const parsed = JSON.parse(value)
        if (Array.isArray(parsed)) {
            return parsed.filter((item): item is string => typeof item === 'string')
        }
        return []
    } catch {
        return []
    }
}

export function ProfileFormModal({ initialData, onSubmit, onClose }: ProfileFormModalProps) {
    const { t } = useTranslation()
    const { isRTL } = useFormatting()
    const [formData, setFormData] = useState({
        displayName: initialData?.displayName || '',
        bio: initialData?.bio || '',
        location: initialData?.location || '',
        skills: safeParseStringArray(initialData?.skills),
        portfolioLinks: safeParseStringArray(initialData?.portfolioLinks),
        profileImageUrl: initialData?.profileImageUrl || '',
        coverImageUrl: initialData?.coverImageUrl || '',
    })
    const [isSubmitting, setIsSubmitting] = useState(false)
    const [skillInput, setSkillInput] = useState('')
    const [linkInput, setLinkInput] = useState('')
    const [profileImagePreview, setProfileImagePreview] = useState<string | null>(
        initialData?.profileImageUrl || null,
    )
    const [coverImagePreview, setCoverImagePreview] = useState<string | null>(
        initialData?.coverImageUrl || null,
    )

    const handleProfileImageUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0]
        if (file) {
            const reader = new FileReader()
            reader.onloadend = () => {
                const dataUrl = reader.result as string
                setProfileImagePreview(dataUrl)
                setFormData((prev) => ({ ...prev, profileImageUrl: dataUrl }))
            }
            reader.readAsDataURL(file)
        }
    }

    const handleCoverImageUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0]
        if (file) {
            const reader = new FileReader()
            reader.onloadend = () => {
                const dataUrl = reader.result as string
                setCoverImagePreview(dataUrl)
                setFormData((prev) => ({ ...prev, coverImageUrl: dataUrl }))
            }
            reader.readAsDataURL(file)
        }
    }

    const addSkill = () => {
        if (skillInput.trim() && !formData.skills.includes(skillInput.trim())) {
            setFormData({ ...formData, skills: [...formData.skills, skillInput.trim()] })
            setSkillInput('')
        }
    }

    const removeSkill = (skill: string) => {
        setFormData({ ...formData, skills: formData.skills.filter((value: string) => value !== skill) })
    }

    const addLink = () => {
        if (linkInput.trim() && !formData.portfolioLinks.includes(linkInput.trim())) {
            setFormData({ ...formData, portfolioLinks: [...formData.portfolioLinks, linkInput.trim()] })
            setLinkInput('')
        }
    }

    const removeLink = (link: string) => {
        setFormData({
            ...formData,
            portfolioLinks: formData.portfolioLinks.filter((value: string) => value !== link),
        })
    }

    const handleSubmit = async (event: React.FormEvent) => {
        event.preventDefault()
        setIsSubmitting(true)

        try {
            const submitData = {
                ...formData,
                skills: JSON.stringify(formData.skills),
                portfolioLinks: JSON.stringify(formData.portfolioLinks),
            }
            await onSubmit(submitData)
            onClose()
        } catch (error) {
            console.error('Failed to save profile:', error)
            window.alert(t('servicesMarketplace.profileForm.saveFailed'))
        } finally {
            setIsSubmitting(false)
        }
    }

    return (
        <div
            className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4"
            onClick={onClose}
            dir={isRTL ? 'rtl' : 'ltr'}
        >
            <div
                className="bg-white rounded-lg shadow-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto"
                onClick={(event) => event.stopPropagation()}
            >
                <div className="p-6">
                    <h3 className="text-2xl font-bold text-gray-900 mb-6">
                        {initialData?.hasMarketplaceProfile
                            ? t('servicesMarketplace.profileForm.editTitle')
                            : t('servicesMarketplace.profileForm.createTitle')}
                    </h3>

                    <form onSubmit={handleSubmit} className="space-y-4">
                        <div>
                            <label className="label">
                                {t('servicesMarketplace.profileForm.fields.displayName')}
                            </label>
                            <input
                                type="text"
                                required
                                className="input"
                                value={formData.displayName}
                                onChange={(event) => setFormData({ ...formData, displayName: event.target.value })}
                                placeholder={t('servicesMarketplace.profileForm.placeholders.displayName')}
                            />
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.profileForm.fields.bio')}
                            </label>
                            <textarea
                                required
                                rows={4}
                                className="input"
                                value={formData.bio}
                                onChange={(event) => setFormData({ ...formData, bio: event.target.value })}
                                placeholder={t('servicesMarketplace.profileForm.placeholders.bio')}
                            />
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.profileForm.fields.location')}
                            </label>
                            <input
                                type="text"
                                className="input"
                                value={formData.location}
                                onChange={(event) => setFormData({ ...formData, location: event.target.value })}
                                placeholder={t('servicesMarketplace.profileForm.placeholders.location')}
                            />
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.profileForm.fields.profileImage')}
                            </label>
                            <input
                                type="file"
                                accept="image/*"
                                className="input"
                                onChange={handleProfileImageUpload}
                            />
                            {profileImagePreview ? (
                                <div className="mt-2">
                                    <img
                                        src={profileImagePreview}
                                        alt={t('servicesMarketplace.profileForm.profilePreviewAlt')}
                                        className="w-24 h-24 rounded-full object-cover"
                                    />
                                </div>
                            ) : null}
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.profileForm.fields.coverImage')}
                            </label>
                            <input
                                type="file"
                                accept="image/*"
                                className="input"
                                onChange={handleCoverImageUpload}
                            />
                            {coverImagePreview ? (
                                <div className="mt-2">
                                    <img
                                        src={coverImagePreview}
                                        alt={t('servicesMarketplace.profileForm.coverPreviewAlt')}
                                        className="w-full h-32 object-cover rounded-lg"
                                    />
                                </div>
                            ) : null}
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.profileForm.fields.skills')}
                            </label>
                            <div className="flex gap-2 mb-2">
                                <input
                                    type="text"
                                    className="input flex-1"
                                    value={skillInput}
                                    onChange={(event) => setSkillInput(event.target.value)}
                                    onKeyDown={(event) => {
                                        if (event.key === 'Enter') {
                                            event.preventDefault()
                                            addSkill()
                                        }
                                    }}
                                    placeholder={t('servicesMarketplace.profileForm.placeholders.skill')}
                                />
                                <button type="button" onClick={addSkill} className="btn-secondary">
                                    {t('servicesMarketplace.profileForm.add')}
                                </button>
                            </div>
                            <div className="flex flex-wrap gap-2">
                                {formData.skills.map((skill: string, index: number) => (
                                    <span
                                        key={`${skill}-${index}`}
                                        className="px-3 py-1 bg-indigo-100 text-indigo-700 rounded-full text-sm flex items-center gap-2"
                                    >
                                        {skill}
                                        <button
                                            type="button"
                                            onClick={() => removeSkill(skill)}
                                            className="text-indigo-900 hover:text-red-600"
                                            aria-label={t('servicesMarketplace.profileForm.removeSkill', {
                                                skill,
                                                defaultValue: `Remove ${skill}`
                                            })}
                                        >
                                            x
                                        </button>
                                    </span>
                                ))}
                            </div>
                        </div>

                        <div>
                            <label className="label">
                                {t('servicesMarketplace.profileForm.fields.portfolioLinks')}
                            </label>
                            <div className="flex gap-2 mb-2">
                                <input
                                    type="url"
                                    className="input flex-1"
                                    value={linkInput}
                                    onChange={(event) => setLinkInput(event.target.value)}
                                    onKeyDown={(event) => {
                                        if (event.key === 'Enter') {
                                            event.preventDefault()
                                            addLink()
                                        }
                                    }}
                                    placeholder={t('servicesMarketplace.profileForm.placeholders.portfolioLink')}
                                />
                                <button type="button" onClick={addLink} className="btn-secondary">
                                    {t('servicesMarketplace.profileForm.add')}
                                </button>
                            </div>
                            <div className="space-y-2">
                                {formData.portfolioLinks.map((link: string, index: number) => (
                                    <div key={`${link}-${index}`} className="flex items-center gap-2 text-sm">
                                        <span className="flex-1 text-indigo-600 truncate">{link}</span>
                                        <button
                                            type="button"
                                            onClick={() => removeLink(link)}
                                            className="text-red-600 hover:text-red-700"
                                        >
                                            {t('servicesMarketplace.profileForm.remove')}
                                        </button>
                                    </div>
                                ))}
                            </div>
                        </div>

                        <div className="flex gap-3 pt-4">
                            <button
                                type="submit"
                                disabled={isSubmitting}
                                className="btn-primary flex-1 disabled:opacity-50"
                            >
                                {isSubmitting
                                    ? t('common.saving')
                                    : initialData?.hasMarketplaceProfile
                                        ? t('servicesMarketplace.profileForm.update')
                                        : t('servicesMarketplace.profileForm.create')}
                            </button>
                            <button type="button" onClick={onClose} className="btn-secondary">
                                {t('common.cancel')}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </div>
    )
}
