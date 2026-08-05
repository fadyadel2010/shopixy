import React from 'react';

function getYoutubeEmbedUrl(url: string, autoplay: boolean, showControls: boolean) {
    if (!url) {
        return null;
    }

    try {
        const parsedUrl = new URL(url);
        let videoId = '';

        if (parsedUrl.hostname.includes('youtu.be')) {
            videoId = parsedUrl.pathname.replace('/', '');
        } else if (parsedUrl.hostname.includes('youtube.com')) {
            if (parsedUrl.pathname === '/watch') {
                videoId = parsedUrl.searchParams.get('v') || '';
            } else if (parsedUrl.pathname.startsWith('/shorts/')) {
                videoId = parsedUrl.pathname.split('/')[2] || '';
            } else if (parsedUrl.pathname.startsWith('/embed/')) {
                videoId = parsedUrl.pathname.split('/')[2] || '';
            }
        }

        if (!videoId) {
            return null;
        }

        const embedUrl = new URL(`https://www.youtube.com/embed/${videoId}`);
        embedUrl.searchParams.set('rel', '0');
        embedUrl.searchParams.set('autoplay', autoplay ? '1' : '0');
        embedUrl.searchParams.set('controls', showControls ? '1' : '0');

        return embedUrl.toString();
    } catch {
        return null;
    }
}

const VideoBlock: React.FC<any> = ({ title, videoUrl, autoplay, showControls, aspectRatio = '16:9' }) => {
    const embedUrl = getYoutubeEmbedUrl(videoUrl, Boolean(autoplay), showControls !== false);
    const frameStyle = { aspectRatio: String(aspectRatio).replace(':', '/') };

    return (
        <section className="py-12 px-4 container mx-auto">
            {title ? <h2 className="mb-6 text-2xl font-bold text-text">{title}</h2> : null}
            {embedUrl ? (
                <div className="overflow-hidden rounded-2xl border border-muted/20 bg-black shadow-sm">
                    <div style={frameStyle}>
                        <iframe
                            src={embedUrl}
                            title={title || 'Storefront video'}
                            className="h-full w-full"
                            allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                            allowFullScreen
                        />
                    </div>
                </div>
            ) : videoUrl ? (
                <div className="overflow-hidden rounded-2xl border border-muted/20 bg-black shadow-sm">
                    <video
                        src={videoUrl}
                        className="h-full w-full"
                        style={frameStyle}
                        autoPlay={Boolean(autoplay)}
                        controls={showControls !== false}
                        muted={Boolean(autoplay)}
                        playsInline
                    />
                </div>
            ) : (
                <div className="rounded-2xl border border-dashed border-muted/30 bg-muted/5 px-6 py-16 text-center text-sm text-muted">
                    Add a video URL to display content here.
                </div>
            )}
        </section>
    );
};

export default VideoBlock;
