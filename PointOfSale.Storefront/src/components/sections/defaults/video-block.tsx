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

const VideoBlock: React.FC<any> = ({ videoUrl, autoplay, showControls }) => {
    const embedUrl = getYoutubeEmbedUrl(videoUrl, Boolean(autoplay), showControls !== false);

    return (
        <section className="py-12 px-4 container mx-auto">
            {embedUrl ? (
                <div className="overflow-hidden rounded-2xl border border-muted/20 bg-black shadow-sm">
                    <div className="aspect-video">
                        <iframe
                            src={embedUrl}
                            title="Storefront video"
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
                        className="aspect-video h-full w-full"
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
