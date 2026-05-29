// Static QA: parse index.html, verify JS integrity without a browser.
// - syntax-checks the inline <script> via node:vm
// - confirms every inline HTML handler (onclick=...) resolves to a defined function
// - flags functions that are defined but never referenced (dead code)
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const FILE = new URL('../index.html', import.meta.url);
const html = readFileSync(FILE, 'utf8');

const fails = [];
const warns = [];
const pass = (m) => console.log(`  PASS  ${m}`);
const fail = (m) => { fails.push(m); console.log(`  FAIL  ${m}`); };
const warn = (m) => { warns.push(m); console.log(`  WARN  ${m}`); };

// ── 1. extract the app's inline script (skip the supabase CDN <script src>) ──
const scriptBlocks = [...html.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi)].map(m => m[1]);
if (!scriptBlocks.length) fail('no inline <script> block found');
const js = scriptBlocks.join('\n;\n');
console.log(`\n[1] Inline script: ${scriptBlocks.length} block(s), ${js.split('\n').length} lines`);

// ── 2. syntax check (compile only, never run — avoids hitting Supabase/Spotify) ──
console.log('\n[2] Syntax check');
try {
  new vm.Script(js, { filename: 'index.html#inline' });
  pass('inline JS compiles without syntax errors');
} catch (e) {
  fail(`syntax error: ${e.message}`);
}

// ── 3. collect defined top-level functions ──
const defined = new Set();
for (const m of js.matchAll(/function\s+([a-zA-Z_$][\w$]*)\s*\(/g)) defined.add(m[1]);
for (const m of js.matchAll(/(?:const|let|var)\s+([a-zA-Z_$][\w$]*)\s*=\s*(?:async\s*)?(?:\([^)]*\)|[a-zA-Z_$][\w$]*)\s*=>/g)) defined.add(m[1]);
console.log(`\n[3] Defined functions: ${defined.size}`);

// ── 4. every inline HTML handler must resolve to a defined function ──
console.log('\n[4] Inline handler resolution');
const NON_FN = new Set(['if', 'document', 'window', 'event', 'return', 'this', 'void', 'true', 'false']);
const handlers = [...html.matchAll(/\bon(?:click|input|change|keydown|keyup|focus|blur|submit|mousedown|mouseup)\s*=\s*"([^"]*)"/gi)];
const calledFns = new Set();
const BUILTIN_METHODS = /^(focus|blur|getElementById|preventDefault|stopPropagation|scrollIntoView|querySelector|querySelectorAll|value|checked|classList|getAttribute|setAttribute|target|currentTarget)$/;
for (const [, body] of handlers) {
  // only bare global calls: a name followed by "(" that is NOT preceded by "."
  for (const m of body.matchAll(/(^|[^.\w$])([a-zA-Z_$][\w$]*)\s*\(/g)) {
    const name = m[2];
    if (NON_FN.has(name) || BUILTIN_METHODS.test(name)) continue;
    calledFns.add(name);
  }
}
let missing = 0;
for (const name of [...calledFns].sort()) {
  // allow methods on objects (e.g. foo.bar()) — only check bare global calls
  if (defined.has(name)) continue;
  // tolerate built-ins / object methods referenced bare in handlers
  if (/^(parseInt|parseFloat|setTimeout|setInterval|alert|confirm|console)$/.test(name)) continue;
  fail(`handler calls undefined function: ${name}()`);
  missing++;
}
if (!missing) pass(`all ${calledFns.size} handler functions are defined`);

// ── 5. dead-code: defined but never referenced anywhere else ──
console.log('\n[5] Dead-code scan (defined but unreferenced)');
let dead = 0;
for (const name of defined) {
  const refs = (js.match(new RegExp(`\\b${name}\\b`, 'g')) || []).length;
  const inHtml = new RegExp(`\\b${name}\\s*\\(`).test(html.replace(js, ''));
  if (refs <= 1 && !inHtml) { warn(`'${name}' defined but never referenced`); dead++; }
}
if (!dead) pass('no obviously dead top-level functions');

// ── 6. structural sanity: required DOM ids referenced by JS exist in HTML ──
console.log('\n[6] getElementById targets exist in DOM');
const idsUsed = new Set([...js.matchAll(/getElementById\(\s*['"]([^'"]+)['"]\s*\)/g)].map(m => m[1]));
let missIds = 0;
for (const id of idsUsed) {
  // static HTML attribute  OR  id injected via a JS template string (id="..." / id='...' / id=\`...\`)
  const inStatic = new RegExp(`\\bid\\s*=\\s*["'\\\`]${id}["'\\\`]`).test(html);
  if (!inStatic) {
    warn(`getElementById('${id}') target not found as a static id (may be injected at runtime — verified by runtime-check)`);
    missIds++;
  }
}
if (!missIds) pass(`all ${idsUsed.size} referenced element ids exist`);

// ── summary ──
console.log(`\n${'─'.repeat(50)}`);
console.log(`STATIC QA: ${fails.length} fail, ${warns.length} warn`);
process.exit(fails.length ? 1 : 0);
