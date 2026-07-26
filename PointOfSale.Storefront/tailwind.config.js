/** @type {import('tailwindcss').Config} */
export default {
    content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
    theme: {
        extend: {
            // All colors use CSS variables - NO HARDCODED VALUES
            colors: {
                primary: 'var(--color-primary)',
                secondary: 'var(--color-secondary)',
                background: 'var(--color-background)',
                text: 'var(--color-text)',
                muted: 'var(--color-muted)',
            },
            // Font family uses CSS variable
            fontFamily: {
                sans: ['var(--font-family)', 'system-ui', 'sans-serif'],
            },
            // Font size uses CSS variable
            fontSize: {
                base: 'var(--font-base-size)',
            },
            // Border radius uses CSS variable
            borderRadius: {
                DEFAULT: 'var(--border-radius)',
                sm: 'calc(var(--border-radius) * 0.5)',
                md: 'var(--border-radius)',
                lg: 'calc(var(--border-radius) * 1.5)',
                xl: 'calc(var(--border-radius) * 2)',
                '2xl': 'calc(var(--border-radius) * 3)',
            },
            // Extended spacing for better control
            spacing: {
                '18': '4.5rem',
                '22': '5.5rem',
            },
            // Container configuration
            container: {
                center: true,
                padding: {
                    DEFAULT: '1rem',
                    sm: '1.5rem',
                    lg: '2rem',
                },
            },
            // Animations
            animation: {
                'fade-in': 'fadeIn 0.3s ease-out',
                'slide-up': 'slideUp 0.3s ease-out',
                'slide-down': 'slideDown 0.3s ease-out',
            },
            keyframes: {
                fadeIn: {
                    '0%': { opacity: '0' },
                    '100%': { opacity: '1' },
                },
                slideUp: {
                    '0%': { opacity: '0', transform: 'translateY(10px)' },
                    '100%': { opacity: '1', transform: 'translateY(0)' },
                },
                slideDown: {
                    '0%': { opacity: '0', transform: 'translateY(-10px)' },
                    '100%': { opacity: '1', transform: 'translateY(0)' },
                },
            },
            // Box shadow using CSS Variables
            boxShadow: {
                'sm': 'var(--shadow-sm)',
                'DEFAULT': 'var(--shadow-md)',
                'md': 'var(--shadow-md)',
                'lg': 'var(--shadow-lg)',
                'xl': 'var(--shadow-xl)',
            },
            // Transition duration
            transitionDuration: {
                'fast': 'var(--transition-fast)',
                'normal': 'var(--transition-normal)',
                'slow': 'var(--transition-slow)',
            },
            // Aspect ratios
            aspectRatio: {
                'product': '4 / 3',
                'card': '3 / 4',
            },
        },
        // Screen breakpoints
        screens: {
            'xs': '475px',
            'sm': '640px',
            'md': '768px',
            'lg': '1024px',
            'xl': '1280px',
            '2xl': '1536px',
        },
    },
    plugins: [],
}
