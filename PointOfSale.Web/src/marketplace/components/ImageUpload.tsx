import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

interface ImageUploadProps {
  dir: 'rtl' | 'ltr';
  label: string;
  currentImage?: string;
  onImageChange: (imageUrl: string) => void;
  aspectRatio?: 'square' | 'wide' | 'cover';
}

export function ImageUpload({ label, currentImage, onImageChange, aspectRatio = 'square', dir }: ImageUploadProps) {
  const { t } = useTranslation();
  const [preview, setPreview] = useState<string | null>(currentImage || null);
  const [uploading, setUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const latestUploadIdRef = useRef(0);

  const handleFileSelect = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    if (!file.type.startsWith('image/')) {
      e.target.value = '';
      window.alert(t('servicesMarketplace.imageUpload.fileTypeError'));
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      e.target.value = '';
      window.alert(t('servicesMarketplace.imageUpload.fileSizeError'));
      return;
    }

    const localUploadId = ++latestUploadIdRef.current;
    setUploading(true);
    try {
      const dataUrl = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader();

        reader.onloadend = () => {
          if (latestUploadIdRef.current !== localUploadId) {
            resolve('');
            return;
          }

          if (typeof reader.result !== 'string') {
            reject(new Error('Failed to read image file.'));
            return;
          }

          setPreview(reader.result);
          resolve(reader.result);
        };

        reader.onerror = () => {
          if (latestUploadIdRef.current !== localUploadId) {
            resolve('');
            return;
          }

          reject(reader.error ?? new Error('Failed to read image file.'));
        };

        reader.readAsDataURL(file);
      });

      if (latestUploadIdRef.current !== localUploadId || !dataUrl) {
        return;
      }

      await new Promise((resolve) => setTimeout(resolve, 500));

      if (latestUploadIdRef.current !== localUploadId) {
        return;
      }

      onImageChange(dataUrl);
    } catch (error) {
      console.error('Upload failed:', error);
      window.alert(t('servicesMarketplace.imageUpload.uploadFailed'));
    } finally {
      if (latestUploadIdRef.current === localUploadId) {
        setUploading(false);
      }
    }
  };

  const handleRemove = () => {
    setPreview(null);
    onImageChange('');
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  const getAspectClass = () => {
    switch (aspectRatio) {
      case 'wide':
        return 'aspect-video';
      case 'cover':
        return 'aspect-[21/9]';
      default:
        return 'aspect-square';
    }
  };

  return (
    <div dir={dir}>
      <label className="label">{label}</label>

      <div className={`relative ${getAspectClass()} w-full max-w-sm border-2 border-dashed border-gray-300 rounded-lg overflow-hidden bg-gray-50`}>
        {preview ? (
          <>
            <img
              src={preview}
              alt={t('servicesMarketplace.imageUpload.previewAlt')}
              className="w-full h-full object-cover"
            />
            <button
              type="button"
              onClick={handleRemove}
              aria-label={t('servicesMarketplace.imageUpload.remove')}
              title={t('servicesMarketplace.imageUpload.remove')}
              className="absolute top-2 right-2 bg-red-500 text-white rounded-full p-2 hover:bg-red-600 shadow-lg"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </>
        ) : (
          <button
            type="button"
            onClick={() => fileInputRef.current?.click()}
            disabled={uploading}
            className="w-full h-full flex flex-col items-center justify-center text-gray-500 hover:text-gray-700 hover:bg-gray-100 transition-colors"
          >
            {uploading ? (
              <>
                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600 mb-2"></div>
                <span className="text-sm">{t('servicesMarketplace.imageUpload.uploading')}</span>
              </>
            ) : (
              <>
                <svg className="w-12 h-12 mb-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                </svg>
                <span className="text-sm font-medium">{t('servicesMarketplace.imageUpload.cta')}</span>
                <span className="text-xs text-gray-400 mt-1">{t('servicesMarketplace.imageUpload.hint')}</span>
              </>
            )}
          </button>
        )}
      </div>

      <input
        ref={fileInputRef}
        type="file"
        accept="image/*"
        onChange={handleFileSelect}
        className="hidden"
      />
    </div>
  );
}
