import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const root = process.cwd();
const evidence = path.join(root, '.ai/evidence/storefront-phase7-20261018');
fs.mkdirSync(evidence, { recursive: true });
const dirs = ['PointOfSale.API/Services/AI', 'PointOfSale.Storefront/src/components/chat', 'PointOfSale.Storefront/src/auth'];
const files = [
    'PointOfSale.API/Controllers/AdminCommerceAiController.cs', 'PointOfSale.API/Controllers/StorefrontChatController.cs',
    'PointOfSale.API/Controllers/StorefrontAuthController.cs', 'PointOfSale.API/Controllers/StorefrontAuthProvidersAdminController.cs',
    'PointOfSale.API/Services/Storefront/StorefrontSocialLoginService.cs', 'PointOfSale.API/Services/Admin/StorefrontAuthProviderAdminService.cs',
    'PointOfSale.API/Services/Admin/IStorefrontAuthProviderAdminService.cs', 'PointOfSale.API/Services/Admin/AdminStoreConfigService.cs',
    'PointOfSale.API/Services/Admin/IAdminStoreConfigService.cs', 'PointOfSale.API/Controllers/AdminStoreConfigController.cs',
    'PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs', 'PointOfSale.Infrastructure/Data/ApplicationDbContext.cs',
    'PointOfSale.Infrastructure/Data/Configurations/SocialLoginConfiguration.cs', 'PointOfSale.Web/src/App.jsx',
    'PointOfSale.Web/src/pages/CommerceAI.jsx', 'PointOfSale.Web/src/modules/storefront/pages/AuthProvidersPage.jsx',
    'PointOfSale.Web/src/modules/storefront/services/authProvidersApi.js', 'PointOfSale.Web/src/i18n/locales/ar.json', 'PointOfSale.Web/src/i18n/locales/en.json',
];
function visit(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const name = path.join(dir, entry.name);
        if (entry.isDirectory()) visit(name); else files.push(name);
    }
}
dirs.forEach(visit);
const manifest = files.map(file => {
    const destination = path.join(evidence, 'originals', file);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.copyFileSync(file, destination);
    return { path: file.replaceAll('\\', '/'), hash: crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex') };
});
fs.writeFileSync(path.join(evidence, 'baseline-originals.json'), JSON.stringify(manifest, null, 2));
for (const [name, args] of Object.entries({
    head: ['rev-parse', 'HEAD'], index: ['diff', '--cached', '--name-status'],
    deleted: ['ls-files', '--deleted'], untracked: ['ls-files', '--others', '--exclude-standard'],
})) fs.writeFileSync(path.join(evidence, `baseline-${name}.txt`), execFileSync('git', args, { maxBuffer: 32 * 1024 * 1024 }));
console.log(`Captured ${manifest.length} source originals; index, HEAD, deletions and untracked manifests saved.`);
