import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root=process.cwd(), evidence='.ai/evidence/storefront-theme-images-20261020';
const before=JSON.parse(fs.readFileSync(`${evidence}/workspace-before.json`,'utf8'));
const diff=spawnSync('git',['diff','--name-only'],{encoding:'utf8'}).stdout.trim().split(/\r?\n/);
const intentional=['PointOfSale.Storefront/src/auth/components/RegisterForm.tsx','PointOfSale.Storefront/src/auth/pages/RegisterPage.tsx'];
const added=['theme-authority/appearanceRoles.ts','PointOfSale.Storefront/src/storefront/products/purchaseAppearance.ts',
    'PointOfSale.Storefront/src/components/theme-components/ThemeActions.appearance.test.tsx',
    'PointOfSale.Storefront/src/storefront/themes/hype/layout/AnnouncementMarquee.appearance.test.tsx',
    'PointOfSale.Storefront/src/components/sections/heroBannerTypes.ts',
    'PointOfSale.Storefront/src/theme/applyTheme.integrity.test.ts','PointOfSale.Storefront/src/components/data/CategoryNav.bounds.test.tsx',
    'PointOfSale.Storefront/src/theme/appearanceIntegrity.test.ts','PointOfSale.Storefront/src/components/media/MediaFrame.tsx',
    'PointOfSale.Storefront/src/components/media/MediaFrame.test.tsx','PointOfSale.Web/src/modules/storefront/components/AppearancePreview.jsx'];
const files=[...new Set([...diff.filter(f=>!before.sha256[f]||intentional.includes(f)),...added])]
    .filter(f=>/^(PointOfSale.Storefront|PointOfSale.Web)\/.*\.(ts|tsx|js|jsx)$/.test(f));
const commands=[], results=[];
for(const project of ['PointOfSale.Storefront','PointOfSale.Web']) {
    const targets=files.filter(f=>f.startsWith(`${project}/`)).map(f=>path.resolve(root,f));
    const run=spawnSync(process.execPath,[path.resolve('PointOfSale.Web/node_modules/eslint/bin/eslint.js'),...targets,
        '--config',path.resolve(project,'.eslintrc.cjs'),'--resolve-plugins-relative-to',path.resolve('PointOfSale.Web'),'--format','json'],
    {cwd:path.resolve(project),encoding:'utf8',env:{...process.env,ESLINT_USE_FLAT_CONFIG:'false'}});
    commands.push({project,exitCode:run.status,stderr:run.stderr});
    try{results.push(...JSON.parse(run.stdout));}catch{commands.at(-1).output=run.stdout;}
}
for (const file of ['theme-authority/appearanceRoles.ts', ...fs.readdirSync('tests/browser').filter(name=>/^storefront-theme-images-20261020-.*\.js$/.test(name)).map(name=>`tests/browser/${name}`)]) {
    const source=fs.readFileSync(file,'utf8');
    const run=spawnSync(process.execPath,[path.resolve('PointOfSale.Web/node_modules/eslint/bin/eslint.js'),
        '--stdin','--stdin-filename',path.resolve(file),'--no-ignore','--config',path.resolve('PointOfSale.Storefront/.eslintrc.cjs'),
        '--resolve-plugins-relative-to',path.resolve('PointOfSale.Web'),'--format','json'],
    {cwd:path.resolve('PointOfSale.Storefront'),encoding:'utf8',env:{...process.env,ESLINT_USE_FLAT_CONFIG:'false'},
        input:file.endsWith('.js')?`export default (\n${source}\n);\n`:source});
    commands.push({file,exitCode:run.status,stderr:run.stderr});
    try{results.push(...JSON.parse(run.stdout));}catch{commands.at(-1).output=run.stdout;}
}
const errors=results.reduce((n,f)=>n+f.errorCount,0), warnings=results.reduce((n,f)=>n+f.warningCount,0);
fs.writeFileSync(`${evidence}/focused-lint.json`,JSON.stringify({files,commands,results,errors,warnings},null,2));
console.log(JSON.stringify({files:files.length,errors,warnings}));
process.exitCode=errors?1:0;
