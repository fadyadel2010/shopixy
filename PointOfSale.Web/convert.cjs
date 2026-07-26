const fs = require('fs');
const path = require('path');

const replacements = [
    { regex: /(?<!dark:)bg-\[\#0B0F19\](\/80)?/g, replace: 'bg-gray-50 dark:bg-[#0B0F19]$1' },
    { regex: /(?<!dark:|from-|to-)bg-\[\#0D1117\](\/50)?/g, replace: 'bg-white dark:bg-[#0D1117]$1' },

    { regex: /(?<!dark:)border-white\/\[0\.06\]/g, replace: 'border-gray-200 dark:border-white/[0.06]' },
    { regex: /(?<!dark:)border-white\/\[0\.08\]/g, replace: 'border-gray-200 dark:border-white/[0.08]' },
    { regex: /(?<!dark:)border-white\/\[0\.1\]/g, replace: 'border-gray-300 dark:border-white/[0.1]' },

    { regex: /(?<!dark:|hover:)bg-white\/\[0\.02\]/g, replace: 'bg-white dark:bg-white/[0.02]' },
    { regex: /(?<!dark:|hover:)bg-white\/\[0\.03\]/g, replace: 'bg-white dark:bg-white/[0.03]' },
    { regex: /(?<!dark:|hover:)bg-white\/\[0\.04\]/g, replace: 'bg-white dark:bg-white/[0.04]' },
    { regex: /(?<!dark:|hover:)bg-white\/\[0\.05\]/g, replace: 'bg-gray-50 dark:bg-white/[0.05]' },

    { regex: /(?<!dark:)hover:bg-white\/\[0\.03\]/g, replace: 'hover:bg-gray-50 dark:hover:bg-white/[0.03]' },
    { regex: /(?<!dark:)hover:bg-white\/\[0\.04\]/g, replace: 'hover:bg-gray-50 dark:hover:bg-white/[0.04]' },
    { regex: /(?<!dark:)hover:bg-white\/\[0\.05\]/g, replace: 'hover:bg-gray-100 dark:hover:bg-white/[0.05]' },

    { regex: /(?<!dark:|hover:)text-white/g, replace: 'text-gray-900 dark:text-white' },
    { regex: /(?<!dark:|hover:)text-gray-200/g, replace: 'text-gray-800 dark:text-gray-200' },
    { regex: /(?<!dark:|hover:)text-gray-300/g, replace: 'text-gray-700 dark:text-gray-300' },
    { regex: /(?<!dark:|hover:)text-gray-400/g, replace: 'text-gray-600 dark:text-gray-400' },

    // Fix some hover states separately
    { regex: /(?<!dark:)hover:text-white/g, replace: 'hover:text-gray-900 dark:hover:text-white' },
    { regex: /(?<!dark:)hover:text-gray-300/g, replace: 'hover:text-gray-700 dark:hover:text-gray-300' },

    { regex: /(?<!dark:)bg-black\/60/g, replace: 'bg-gray-900/40 dark:bg-black/60' },
];

function walk(dir) {
    if (!fs.existsSync(dir)) return;
    const files = fs.readdirSync(dir);
    for (const file of files) {
        const fullPath = path.join(dir, file);
        if (fs.statSync(fullPath).isDirectory()) {
            walk(fullPath);
        } else if (fullPath.endsWith('.jsx') || fullPath.endsWith('.js')) {
            let content = fs.readFileSync(fullPath, 'utf8');
            let updated = content;
            for (const { regex, replace } of replacements) {
                updated = updated.replace(regex, replace);
            }
            if (content !== updated) {
                fs.writeFileSync(fullPath, updated, 'utf8');
                console.log(`Updated ${fullPath}`);
            }
        }
    }
}

walk(path.join(__dirname, 'src', 'modules', 'affiliate', 'pages'));
walk(path.join(__dirname, 'src', 'modules', 'affiliate', 'components'));
console.log('Done.');
