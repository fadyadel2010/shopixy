import { useEffect, useRef } from 'react'

/**
 * Simple Line Chart Component
 * A lightweight chart component without external dependencies
 */
export default function LineChart({ data, height = 200, color = '#3b82f6' }) {
    const canvasRef = useRef(null)

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
        const padding = { top: 20, right: 20, bottom: 40, left: 50 }
        const chartWidth = width - padding.left - padding.right
        const chartHeight = height - padding.top - padding.bottom

        // Clear canvas
        ctx.clearRect(0, 0, width, height)

        // Find min and max values
        const values = data.map(d => d.amount)
        const maxValue = Math.max(...values, 0)
        const minValue = Math.min(...values, 0)
        const valueRange = maxValue - minValue || 1

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
            const value = maxValue - (valueRange / gridLines) * i
            ctx.fillStyle = '#6b7280'
            ctx.font = '11px sans-serif'
            ctx.textAlign = 'right'
            ctx.fillText(`$${value.toFixed(0)}`, padding.left - 10, y + 4)
        }

        // Draw line chart
        ctx.strokeStyle = color
        ctx.lineWidth = 2
        ctx.beginPath()

        data.forEach((point, index) => {
            const x = padding.left + (chartWidth / (data.length - 1)) * index
            const normalizedValue = (point.amount - minValue) / valueRange
            const y = padding.top + chartHeight - (normalizedValue * chartHeight)

            if (index === 0) {
                ctx.moveTo(x, y)
            } else {
                ctx.lineTo(x, y)
            }
        })
        ctx.stroke()

        // Draw points
        ctx.fillStyle = color
        data.forEach((point, index) => {
            const x = padding.left + (chartWidth / (data.length - 1)) * index
            const normalizedValue = (point.amount - minValue) / valueRange
            const y = padding.top + chartHeight - (normalizedValue * chartHeight)

            ctx.beginPath()
            ctx.arc(x, y, 4, 0, Math.PI * 2)
            ctx.fill()
        })

        // Draw X-axis labels
        ctx.fillStyle = '#6b7280'
        ctx.font = '11px sans-serif'
        ctx.textAlign = 'center'
        data.forEach((point, index) => {
            if (data.length <= 7 || index % Math.ceil(data.length / 7) === 0) {
                const x = padding.left + (chartWidth / (data.length - 1)) * index
                ctx.fillText(point.label, x, height - 10)
            }
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
