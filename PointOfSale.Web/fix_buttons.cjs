const fs = require('fs');
const path = require('path');

const pageDirs = [
    path.join(__dirname, 'src', 'modules', 'affiliate', 'pages'),
    path.join(__dirname, 'src', 'modules', 'affiliate', 'components')
];

const replacements = [
    // Hero buttons & CTA buttons: text-gray-900 dark:text-white -> text-white
    { regex: /bg-gradient-to-r from-\[\#6C63FF\] to-\[\#8B5CF6\] text-gray-900 dark:text-white/g, replace: 'bg-gradient-to-r from-[#6C63FF] to-[#8B5CF6] text-white' },

    // Dollar Icons inside purple gradients
    { regex: /<DollarSign className="w-5 h-5 text-gray-900 dark:text-white"/g, replace: '<DollarSign className="w-5 h-5 text-white"' },
    { regex: /<DollarSign className="w-4 h-4 text-gray-900 dark:text-white"/g, replace: '<DollarSign className="w-4 h-4 text-white"' },
    { regex: /<DollarSign className="w-7 h-7 text-gray-900 dark:text-white"/g, replace: '<DollarSign className="w-7 h-7 text-white"' },
    { regex: /<Rocket className="w-7 h-7 text-gray-900 dark:text-white"/g, replace: '<Rocket className="w-7 h-7 text-white"' },

    // Top Header Badge
    { regex: /bg-white\/5 border border-gray-200/g, replace: 'bg-white dark:bg-white/5 border border-gray-200 shadow-sm dark:shadow-none' },

    // How It works icons bg
    { regex: /bg-white\/5 flex items-center justify-center mb-6/g, replace: 'bg-white dark:bg-white/5 shadow-sm dark:shadow-none flex items-center justify-center mb-6' },

    // Search area
    { regex: /bg-white\/5 text-gray-900 dark:text-white placeholder:text-gray-500/g, replace: 'bg-white dark:bg-white/5 shadow-sm dark:shadow-none text-gray-900 dark:text-white placeholder:text-gray-500' },

    // Pulse loading placeholders
    { regex: /bg-white\/5 rounded-lg mb-4/g, replace: 'bg-gray-200 dark:bg-white/5 rounded-lg mb-4' },
    { regex: /bg-white\/5 rounded w-3\/4/g, replace: 'bg-gray-200 dark:bg-white/5 rounded w-3/4' },
    { regex: /bg-white\/5 rounded w-1\/2/g, replace: 'bg-gray-200 dark:bg-white/5 rounded w-1/2' },
    { regex: /bg-white\/5 rounded-lg/g, replace: 'bg-gray-200 dark:bg-white/5 rounded-lg' },
    { regex: /border border-white\/5 bg-white dark:bg-white/g, replace: 'border border-gray-100 dark:border-white/5 bg-white dark:bg-white' },

    // Product image bg
    { regex: /bg-gradient-to-br from-white\/5 to-white\/\[0\.02\]/g, replace: 'bg-gradient-to-br from-gray-100 to-gray-50 dark:from-white/5 dark:to-white/[0.02]' },

    // Commission badge
    { regex: /bg-emerald-500\/90 text-gray-900 dark:text-white/g, replace: 'bg-emerald-500/90 text-white' },

    // Footer lines
    { regex: /border-t border-white\/5/g, replace: 'border-t border-gray-200 dark:border-white/5' },
    { regex: /border-b border-white\/5/g, replace: 'border-b border-gray-200 dark:border-white/5' },

    // Why Join specific
    { regex: /border border-white\/5 bg-white/g, replace: 'border border-gray-100 dark:border-white/5 bg-white' },
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

pageDirs.forEach(walk);
console.log('Fixing buttons and badges completed.');
