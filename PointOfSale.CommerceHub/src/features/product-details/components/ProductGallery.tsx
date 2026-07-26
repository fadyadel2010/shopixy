import { ImageOff, Maximize2, X } from "lucide-react";
import { memo, useState } from "react";

type ProductGalleryProps = {
  images: string[];
  productName: string;
  activeImage: string | null;
  onActiveImageChange: (image: string) => void;
};

export const ProductGallery = memo(({
  images,
  productName,
  activeImage,
  onActiveImageChange
}: ProductGalleryProps) => {
  const [isFullscreenOpen, setIsFullscreenOpen] = useState(false);
  const hasImages = images.length > 0 && Boolean(activeImage);

  return (
    <>
      <div className="space-y-4">
        <div className="group relative overflow-hidden rounded-[2rem] border border-white/70 bg-white shadow-card">
          {hasImages ? (
            <>
              <button
                type="button"
                onClick={() => setIsFullscreenOpen(true)}
                className="block aspect-square w-full bg-secondary/40"
                aria-label="Open product image"
              >
                <img
                  src={activeImage ?? ""}
                  alt={productName}
                  className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-[1.02]"
                />
              </button>
              <div className="pointer-events-none absolute inset-x-0 bottom-0 h-24 bg-gradient-to-t from-slate-950/30 to-transparent" />
              <div className="absolute left-4 top-4 rounded-full bg-white/88 px-3 py-1.5 text-xs font-semibold uppercase tracking-[0.22em] text-foreground shadow-soft backdrop-blur">
                Shopixy gallery
              </div>
              <div className="absolute bottom-4 left-4 rounded-full bg-white/88 px-3 py-1.5 text-xs font-semibold uppercase tracking-[0.22em] text-foreground shadow-soft backdrop-blur">
                {images.length} image{images.length === 1 ? "" : "s"}
              </div>
              <button
                type="button"
                onClick={() => setIsFullscreenOpen(true)}
                className="absolute right-4 top-4 rounded-full border border-white/70 bg-white/90 p-2 text-slate-700 shadow-sm transition-colors hover:bg-white"
                aria-label="View fullscreen image"
              >
                <Maximize2 className="h-4 w-4" />
              </button>
            </>
          ) : (
            <div className="flex aspect-square flex-col items-center justify-center gap-4 bg-gradient-to-br from-slate-100 via-white to-slate-50 px-6 text-center text-slate-500">
              <div className="flex h-16 w-16 items-center justify-center rounded-3xl bg-white shadow-soft">
                <ImageOff className="h-6 w-6" />
              </div>
              <div>
                <p className="text-base font-semibold text-slate-900">Image coming soon</p>
                <p className="mt-1 text-sm">We&apos;re still syncing this product gallery.</p>
              </div>
            </div>
          )}
        </div>

        {images.length > 1 ? (
          <div className="grid grid-cols-4 gap-3 sm:grid-cols-5">
            {images.map((image, index) => {
              const isSelected = image === activeImage;

              return (
                <button
                  key={`${image}-${index}`}
                  type="button"
                  onClick={() => onActiveImageChange(image)}
                  className={[
                    "overflow-hidden rounded-[1.35rem] border bg-white transition-all duration-200",
                    isSelected
                      ? "border-primary shadow-soft ring-2 ring-primary/15"
                      : "border-white/70 opacity-80 hover:-translate-y-0.5 hover:opacity-100"
                  ].join(" ")}
                  aria-label={`View image ${index + 1}`}
                  aria-pressed={isSelected}
                >
                  <img
                    src={image}
                    alt={`${productName} thumbnail ${index + 1}`}
                    className="aspect-square h-full w-full object-cover"
                    loading="lazy"
                  />
                </button>
              );
            })}
          </div>
        ) : null}
      </div>

      {isFullscreenOpen && activeImage ? (
        <div className="fixed inset-0 z-[70] bg-slate-950/92 p-4 backdrop-blur-md">
          <button
            type="button"
            onClick={() => setIsFullscreenOpen(false)}
            className="absolute right-4 top-4 rounded-full border border-white/20 bg-white/10 p-2 text-white transition-colors hover:bg-white/20"
            aria-label="Close image viewer"
          >
            <X className="h-5 w-5" />
          </button>

          <div className="mx-auto flex h-full max-w-6xl flex-col justify-center gap-4">
            <div className="overflow-hidden rounded-[2rem] border border-white/10 bg-white/5">
              <img src={activeImage} alt={productName} className="max-h-[78vh] w-full object-contain" />
            </div>

            {images.length > 1 ? (
              <div className="flex flex-wrap justify-center gap-3">
                {images.map((image, index) => (
                  <button
                    key={`${image}-fullscreen-${index}`}
                    type="button"
                    onClick={() => onActiveImageChange(image)}
                    className={[
                      "overflow-hidden rounded-2xl border bg-white/10 transition-all",
                      image === activeImage ? "border-white shadow-soft" : "border-white/10 opacity-70 hover:opacity-100"
                    ].join(" ")}
                    aria-label={`View fullscreen image ${index + 1}`}
                  >
                    <img
                      src={image}
                      alt={`${productName} fullscreen thumbnail ${index + 1}`}
                      className="h-16 w-16 object-cover"
                      loading="lazy"
                    />
                  </button>
                ))}
              </div>
            ) : null}
          </div>
        </div>
      ) : null}
    </>
  );
});

ProductGallery.displayName = "ProductGallery";
