import { appearanceRoles, appearanceContrastWarnings, isHexColor } from '@theme-authority/appearanceRoles'
import { ThemeRuntimePreview } from './builder/ThemeRuntimePreview'

export function AppearancePreview({ colors, font, baseSize, borderRadius, config, tenantId, language }) {
    if (!Object.values(colors).every(isHexColor)) return <p role="status">Enter valid colors to preview.</p>
    const roles = appearanceRoles(colors)
    const warnings = appearanceContrastWarnings(colors)
    const candidate = config ? {
        ...config,
        theme: { ...config.theme, colors, font: { ...config.theme.font, family: font, baseSize },
            ui: { ...config.theme.ui, borderRadius: `${borderRadius}px` } },
    } : null
    return <>
        <div className="rounded-xl overflow-hidden border border-gray-200 shadow-lg"
            data-appearance-palette="true"
            style={{ backgroundColor: colors.background, color: colors.text, fontFamily: font, fontSize: baseSize }}>
            <div className="p-4 font-bold" style={{ backgroundColor: colors.secondary, color: roles['on-secondary'] }}>Store Name</div>
            <div className="p-4 space-y-4">
                <div className="p-4 text-center" style={{ backgroundColor: roles.surface, borderRadius }}>
                    <h3 className="text-lg font-bold mb-2">Welcome</h3>
                    <button type="button" className="px-4 py-2 text-sm font-medium"
                        style={{ backgroundColor: colors.primary, color: roles['on-primary'], borderRadius }}>Shop Now</button>
                </div>
                <p className="text-sm" style={{ color: colors.muted }}>Secondary text</p>
                <div className="grid grid-cols-2 gap-3">{[1,2].map(i => <div key={i} className="border p-3"
                    style={{ borderColor: roles.border, borderRadius }}>
                    <div className="aspect-square mb-2" style={{ backgroundColor: roles.subtle, borderRadius }} />
                    <p className="text-sm font-medium">Product {i}</p>
                    <p className="text-sm font-bold" style={{ color: roles['primary-text'] }}>$99.99</p>
                </div>)}</div>
            </div>
        </div>
        {warnings.length > 0 && <div role="status" className="mt-4 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-950">
            <p className="font-semibold">Palette contrast needs attention</p>
            {warnings.map(warning => <p key={warning}>{warning}</p>)}
            <p>Saved colors are preserved. Button labels use a contrasting foreground; accent text uses a readable shade.</p>
        </div>}
        {candidate && <div className="mt-6"><h3 className="mb-3 font-semibold text-gray-900">Actual Storefront draft</h3>
            <ThemeRuntimePreview config={candidate} tenantId={tenantId} language={language} viewportWidth={390} />
        </div>}
    </>
}
