const fs = require('fs');
const path = require('path');

const replacements = [
    { regex: /(?<!dark:)bg-\[\#121826\](\/80)?/g, replace: 'bg-white/90 dark:bg-[#121826]$1' },
    { regex: /(?<!dark:)bg-\[\#1A2133\]/g, replace: 'bg-white dark:bg-[#1A2133]' },
    { regex: /(?<!dark:)border-white\/10/g, replace: 'border-gray-200 dark:border-white/10' },
    { regex: /(?<!dark:|from-|to-)bg-black/g, replace: 'bg-gray-900/40 dark:bg-black' },
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
console.log('Done additional styling.');
