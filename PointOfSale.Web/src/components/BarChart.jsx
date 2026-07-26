import { useEffect, useRef } from 'react'

/**
 * Simple Bar Chart Component
 * A lightweight chart component without external dependencies
 */
export default function BarChart({ data, height = 200, color = '#10b981' }) {
    const canvasRef = useRef(null)

    useEffect(() => {
        if (!data || data.length === 0) return

        const canvas = canvasRef.current
        if (!canvas) return

        const ctx = canvas.getContext('2d')
        const dpr = window.devicePixelRatio || 1

        // Set canvas size accounting for device pixel ratio
        const rect = canvas.getBoundingClientRect()

        // Guard against zero or invalid dimensions
        if (!rect.width || rect.width <= 0 || !isFinite(rect.width)) return

        canvas.width = rect.width * dpr
        canvas.height = height * dpr
        ctx.scale(dpr, dpr)

        const width = rect.width
        const padding = { top: 20, right: 20, bottom: 60, left: 50 }
        const chartWidth = width - padding.left - padding.right
        const chartHeight = height - padding.top - padding.bottom

        // Guard against invalid chart dimensions
        if (chartWidth <= 0 || chartHeight <= 0) return

        // Clear canvas
        ctx.clearRect(0, 0, width, height)

        // Find max value - ensure it's at least 1 to avoid division by zero
        const maxValue = Math.max(...data.map(d => d.value || 0), 1)

        // Draw grid lines
        ctx.strokeStyle = '#e5e7eb'
        ctx.lineWidth = 1
        const gridLines = 5
        for (let i = 0; i <= gridLines; i++) {
            const y = padding.top + (chartHeight / gridLines) * i
            ctx.beginPath()
            ctx.moveTo(padding.left, y)
            ctx.lineTo(padding.left + chartWidth, y)
            ctx.stroke()

            // Draw Y-axis labels
            const value = maxValue - (maxValue / gridLines) * i
            ctx.fillStyle = '#6b7280'
            ctx.font = '11px sans-serif'
            ctx.textAlign = 'right'
            ctx.fillText(`$${value.toFixed(0)}`, padding.left - 10, y + 4)
        }

        // Draw bars
        const barWidth = Math.max(chartWidth / data.length - 10, 1)
        data.forEach((item, index) => {
            const itemValue = item.value || 0
            const x = padding.left + (chartWidth / data.length) * index + 5
            const barHeight = (itemValue / maxValue) * chartHeight
            const y = padding.top + chartHeight - barHeight

            // Guard against non-finite gradient values
            if (!isFinite(y) || !isFinite(barHeight) || barHeight < 0) return

            // Only draw bar if there's a value
            if (itemValue > 0 && barHeight > 0) {
                // Draw bar with gradient
                const gradientY1 = Math.max(0, y)
                const gradientY2 = Math.max(gradientY1 + 1, y + barHeight)
                const gradient = ctx.createLinearGradient(0, gradientY1, 0, gradientY2)
                gradient.addColorStop(0, color)
                gradient.addColorStop(1, color + '80')

                ctx.fillStyle = gradient
                ctx.fillRect(x, y, barWidth, barHeight)

                // Draw value on top of bar
                ctx.fillStyle = '#374151'
                ctx.font = 'bold 11px sans-serif'
                ctx.textAlign = 'center'
                ctx.fillText(`$${itemValue.toFixed(0)}`, x + barWidth / 2, y - 5)
            }

            // Draw label
            ctx.fillStyle = '#6b7280'
            ctx.font = '11px sans-serif'
            ctx.save()
            ctx.translate(x + barWidth / 2, height - 10)
            ctx.rotate(-Math.PI / 4)
            ctx.textAlign = 'right'
            ctx.fillText(item.label || '', 0, 0)
            ctx.restore()
        })

    }, [data, height, color])

    return (
        <canvas
            ref={canvasRef}
            style={{ width: '100%', height: `${height}px` }}
            className="w-full"
        />
    )
}
