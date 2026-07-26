export default function ProductWorkspaceLayout({ topBar, children }) {
    return (
        <div className="-mx-4 sm:-mx-6 lg:-mx-8">
            <div className="sticky top-16 z-20 border-b border-gray-200 bg-white/95 backdrop-blur">
                <div className="px-4 py-4 sm:px-6 lg:px-8">
                    {topBar}
                </div>
            </div>

            <div className="px-4 py-6 sm:px-6 lg:px-8">
                {children}
            </div>
        </div>
    )
}
