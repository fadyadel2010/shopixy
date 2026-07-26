import { useEffect, useRef } from 'react'

/**
 * Simple Donut Chart Component
 * A lightweight chart component without external dependencies
 */
export default function DonutChart({ data, height = 200 }) {
    const canvasRef = useRef(null)

    const colors = [
        '#3b82f6', // blue
        '#10b981', // green
        '#f59e0b', // amber
        '#ef4444', // red
        '#8b5cf6', // purple
        '#ec4899', // pink
        '#06b6d4', // cyan
        '#84cc16', // lime
    ]

    useEffect(() => {
        if (!data || data.length === 0) return

        const canvas = canvasRef.current
        if (!canvas) return

        const ctx = canvas.getContext('2d')
        const dpr = window.devicePixelRatio || 1

        // Set canvas size accounting for device pixel ratio
        const rect = canvas.getBoundingClientRect()
        canvas.width = rect.width * dpr
        canvas.height = height * dpr
        ctx.scale(dpr, dpr)

        const width = rect.width
        const centerX = width / 2
        const centerY = height / 2
        const radius = Math.min(width, height) / 2 - 40
        const innerRadius = radius * 0.6

        // Clear canvas
        ctx.clearRect(0, 0, width, height)

        // Calculate total
        const total = data.reduce((sum, item) => sum + item.value, 0)

        if (total === 0) {
            ctx.fillStyle = '#9ca3af'
            ctx.font = '14px sans-serif'
            ctx.textAlign = 'center'
            ctx.fillText('No data', centerX, centerY)
            return
        }

        // Draw donut segments
        let currentAngle = -Math.PI / 2 // Start at top

        data.forEach((item, index) => {
            const sliceAngle = (item.value / total) * Math.PI * 2
            const color = colors[index % colors.length]

            // Draw slice
            ctx.fillStyle = color
            ctx.beginPath()
            ctx.arc(centerX, centerY, radius, currentAngle, currentAngle + sliceAngle)
            ctx.arc(centerX, centerY, innerRadius, currentAngle + sliceAngle, currentAngle, true)
            ctx.closePath()
            ctx.fill()

            // Draw label line and text
            const midAngle = currentAngle + sliceAngle / 2
            const labelX = centerX + Math.cos(midAngle) * (radius + 20)
            const labelY = centerY + Math.sin(midAngle) * (radius + 20)

            ctx.strokeStyle = color
            ctx.lineWidth = 2
            ctx.beginPath()
            ctx.moveTo(centerX + Math.cos(midAngle) * radius, centerY + Math.sin(midAngle) * radius)
            ctx.lineTo(labelX, labelY)
            ctx.stroke()

            // Draw percentage
            const percentage = ((item.value / total) * 100).toFixed(1)
            ctx.fillStyle = '#374151'
            ctx.font = 'bold 11px sans-serif'
            ctx.textAlign = labelX > centerX ? 'left' : 'right'
            ctx.fillText(`${percentage}%`, labelX + (labelX > centerX ? 5 : -5), labelY - 5)

            currentAngle += sliceAngle
        })

        // Draw center circle (donut hole)
        ctx.fillStyle = '#ffffff'
        ctx.beginPath()
        ctx.arc(centerX, centerY, innerRadius, 0, Math.PI * 2)
        ctx.fill()

        // Draw total in center
        ctx.fillStyle = '#374151'
        ctx.font = 'bold 16px sans-serif'
        ctx.textAlign = 'center'
        ctx.fillText(`$${total.toFixed(0)}`, centerX, centerY - 5)
        ctx.font = '12px sans-serif'
        ctx.fillStyle = '#6b7280'
        ctx.fillText('Total', centerX, centerY + 15)

    }, [data, height])

    return (
        <div className="flex flex-col items-center">
            <canvas
                ref={canvasRef}
                style={{ width: '100%', height: `${height}px` }}
                className="w-full"
            />
            {/* Legend */}
            <div className="mt-4 flex flex-wrap justify-center gap-4">
                {data.map((item, index) => (
                    <div key={index} className="flex items-center gap-2">
                        <div
                            className="w-3 h-3 rounded-full"
                            style={{ backgroundColor: colors[index % colors.length] }}
                        />
                        <span className="text-xs text-gray-600">{item.label}</span>
                    </div>
                ))}
            </div>
        </div>
    )
}
