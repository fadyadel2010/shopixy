import { useTheme } from '../theme/ThemeContext'
import { Sun, Moon } from 'lucide-react'

export default function ThemeSwitcher() {
    const { theme, toggleTheme } = useTheme()

    return (
        <button
            onClick={toggleTheme}
            className="p-2 rounded-lg bg-gray-100/50 dark:bg-white/[0.03] border border-gray-200 dark:border-white/[0.06] text-gray-500 hover:text-gray-900 dark:text-gray-400 dark:hover:text-white transition-all flex items-center justify-center"
            title={theme === 'dark' ? 'Switch to Light Mode' : 'Switch to Dark Mode'}
        >
            {theme === 'dark' ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
        </button>
    )
}
