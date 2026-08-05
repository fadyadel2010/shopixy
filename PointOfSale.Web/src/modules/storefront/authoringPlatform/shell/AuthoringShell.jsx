export function AuthoringShell({ children, className = '' }) {
    return (
        <div className={className} data-authoring-platform="storefront">
            {children}
        </div>
    )
}
