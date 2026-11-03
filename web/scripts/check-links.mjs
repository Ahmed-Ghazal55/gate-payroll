// Fails the build if any exported page links to /app/app/ (basePath added twice).
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
const bad = [];
const walk = (dir) => { for (const n of readdirSync(dir)) { const p = join(dir, n); if (statSync(p).isDirectory()) walk(p); else if (/\.(html|js|txt)$/.test(n) && readFileSync(p, 'utf8').includes('/app/app/')) bad.push(p); } };
walk(new URL('../out', import.meta.url).pathname);
if (bad.length) { console.error('Double basePath (/app/app/) in:\n' + bad.join('\n')); process.exit(1); }
console.log('PASS no /app/app/ links in the static export');
