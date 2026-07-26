import { useState, useRef } from 'react'
import api from '../services/api'
import { useTranslation } from 'react-i18next'

export default function ImageUpload({ images = [], onChange, maxImages = 5 }) {
    const { t } = useTranslation()
    const [uploading, setUploading] = useState(false)
    const [error, setError] = useState('')
    const fileInputRef = useRef(null)

    const handleFileSelect = async (e) => {
        const files = Array.from(e.target.files)

        if (images.length + files.length > maxImages) {
            setError(t('products.maxImagesAllowed', { max: maxImages }))
            return
        }

        setUploading(true)
        setError('')

        try {
            const formData = new FormData()
            files.forEach(file => formData.append('files', file))

            const response = await api.post('/products/upload-images', formData, {
                headers: { 'Content-Type': 'multipart/form-data' }
            })

            onChange([...images, ...response.data])
        } catch (err) {
            setError(err.response?.data?.message || t('products.failedToUploadImages'))
        } finally {
            setUploading(false)
            if (fileInputRef.current) {
                fileInputRef.current.value = ''
            }
        }
    }

    const handleRemove = (index) => {
        const newImages = images.filter((_, i) => i !== index)
        onChange(newImages)
    }

    const handleSetPrimary = (index) => {
        const newImages = [...images]
        const [primary] = newImages.splice(index, 1)
        newImages.unshift(primary)
        onChange(newImages)
    }

    return (
        <div>
            <label className="label">{t('products.productImages')}</label>

            {error && (
                <div className="mb-3 p-2 bg-red-100 border border-red-400 text-red-700 text-sm rounded">
                    {error}
                </div>
            )}

            <div className="grid grid-cols-5 gap-3 mb-3">
                {images.map((imageUrl, index) => (
                    <div key={index} className="relative group">
                        <img
                            src={imageUrl}
                            alt={`Product ${index + 1}`}
                            className="w-full h-24 object-cover rounded border border-gray-300"
                        />
                        {index === 0 && (
                            <div className="absolute top-1 left-1 bg-primary-600 text-white text-xs px-2 py-1 rounded">
                                {t('products.primary')}
                            </div>
                        )}
                        <div className="absolute inset-0 bg-black bg-opacity-50 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center gap-2">
                            {index !== 0 && (
                                <button
                                    type="button"
                                    onClick={() => handleSetPrimary(index)}
                                    className="text-white text-xs bg-blue-600 px-2 py-1 rounded hover:bg-blue-700"
                                >
                                    {t('products.setPrimary')}
                                </button>
                            )}
                            <button
                                type="button"
                                onClick={() => handleRemove(index)}
                                className="text-white text-xs bg-red-600 px-2 py-1 rounded hover:bg-red-700"
                            >
                                {t('products.remove')}
                            </button>
                        </div>
                    </div>
                ))}

                {images.length < maxImages && (
                    <label className="w-full h-24 border-2 border-dashed border-gray-300 rounded flex flex-col items-center justify-center cursor-pointer hover:border-primary-500 hover:bg-primary-50 transition-colors">
                        <input
                            ref={fileInputRef}
                            type="file"
                            accept="image/*"
                            multiple
                            onChange={handleFileSelect}
                            className="hidden"
                            disabled={uploading}
                        />
                        {uploading ? (
                            <div className="animate-spin h-6 w-6 border-2 border-primary-600 border-t-transparent rounded-full"></div>
                        ) : (
                            <>
                                <svg className="w-8 h-8 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                                </svg>
                                <span className="text-xs text-gray-500 mt-1">{t('products.upload')}</span>
                            </>
                        )}
                    </label>
                )}
            </div>

            <div className="text-xs text-gray-500">
                {t('products.imageUploadHelp', { max: maxImages })}
            </div>
        </div>
    )
}
