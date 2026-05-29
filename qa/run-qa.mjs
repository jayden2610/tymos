// One-shot QA runner: static analysis first (fast, no browser), then runtime.
// Exit non-zero if either stage fails. Used locally and by CI.
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const run = (file) => spawnSync(process.execPath, [file], { cwd: here, stdio: 'inherit' }).status ?? 1;

console.log('═══ TYMOS QA ═══');
console.log('\n▶ STAGE 1 — Static analysis');
const s1 = run('static-check.mjs');
console.log('\n▶ STAGE 2 — Runtime + functional (Playwright)');
const s2 = run('runtime-check.mjs');

console.log(`\n═══ RESULT ═══  static=${s1 ? 'FAIL' : 'ok'}  runtime=${s2 ? 'FAIL' : 'ok'}`);
process.exit(s1 || s2 ? 1 : 0);
