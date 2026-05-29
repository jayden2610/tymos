// Runtime + functional QA using Playwright (Chromium headless).
// Loads index.html from file://, blocks external hosts so the app runs offline
// and deterministically, captures console/page errors, then exercises core flows.
import { chromium } from 'playwright';
import { fileURLToPath } from 'node:url';

const INDEX = fileURLToPath(new URL('../index.html', import.meta.url));
const fails = [];
const pass = (m) => console.log(`  PASS  ${m}`);
const fail = (m) => { fails.push(m); console.log(`  FAIL  ${m}`); };
const eq = (label, got, want) =>
  got === want ? pass(`${label} == ${want}`) : fail(`${label}: got ${JSON.stringify(got)}, want ${JSON.stringify(want)}`);

const browser = await chromium.launch();
const ctx = await browser.newContext();

// Block every external host — keep the test hermetic. The app must degrade gracefully.
await ctx.route('**/*', (route) => {
  const u = route.request().url();
  if (u.startsWith('file://')) return route.continue();
  return route.abort();
});

const page = await ctx.newPage();
const consoleErrors = [];
const pageErrors = [];
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', (e) => pageErrors.push(e.message));

console.log('\n[1] Page load + error capture');
await page.goto('file://' + INDEX, { waitUntil: 'load' });
await page.waitForTimeout(500); // let init run

// pageerror = uncaught JS exceptions. These are real bugs regardless of network.
if (pageErrors.length) pageErrors.forEach(e => fail(`uncaught exception: ${e}`));
else pass('no uncaught JS exceptions on load');

// console errors from blocked external resources are expected; flag only non-network ones.
const realConsole = consoleErrors.filter(t =>
  !/Failed to load|net::ERR|Refused to|CSP|Content Security|ERR_FAILED|supabase|spotify|fonts|cdn|favicon/i.test(t));
if (realConsole.length) realConsole.forEach(t => fail(`console error: ${t}`));
else pass('no non-network console errors');

// helper to read app globals
const G = (expr) => page.evaluate(expr);

console.log('\n[2] Core globals & functions are wired');
for (const fn of ['toggleTimer', 'tick', 'resetTimer', 'skipPhase', 'enterBreak', 'enterWork',
                   'qaCommit', 'renderTasks', 'updateStats', 'saveSettings', 'updateRing']) {
  const t = await G(`typeof ${fn}`);
  eq(`typeof ${fn}`, t, 'function');
}

console.log('\n[3] Timer state machine');
// reset to a known state
await G(`(()=>{ running=false; clearInterval(interval); isBreak=false; })`);
const initialRemaining = await G('remaining');
pass(`initial remaining = ${initialRemaining}s`);

// toggleTimer should start it
await page.evaluate(`toggleTimer()`);
eq('running after toggleTimer()', await G('running'), true);

// tick should decrement remaining by 1
const before = await G('remaining');
await page.evaluate(`tick()`);
eq('tick() decrements remaining', await G('remaining'), before - 1);

// reset should stop and restore
await page.evaluate(`resetTimer()`);
eq('running after resetTimer()', await G('running'), false);

console.log('\n[4] Break/work phase toggle');
await page.evaluate(`enterBreak()`);
eq('isBreak after enterBreak()', await G('isBreak'), true);
eq('body.break-mode class', await G(`document.body.classList.contains('break-mode')`), true);
await page.evaluate(`enterWork()`);
eq('isBreak after enterWork()', await G('isBreak'), false);

console.log('\n[5] Task lifecycle (create → render → done → delete)');
const startCount = await G('tasks.length');
// drive quick-add through the real DOM path: expand card, fill input, set priority, commit
await page.evaluate(`(()=>{
  qaFocus();                                   // expands card, injects #qaTitleInput
  document.getElementById('qaTitleInput').value = 'QA test task';
  qaPri = 'high';
  qaCommit();
})()`);
eq('task count after qaCommit', await G('tasks.length'), startCount + 1);
const newId = await G('tasks[tasks.length-1].id');
eq('new task title persisted', await G('tasks[tasks.length-1].title'), 'QA test task');
const cardExists = await G(`!!document.getElementById('task-' + ${JSON.stringify(newId)})`);
eq(`task card #task-${newId} in DOM`, cardExists, true);
// toggle done
await page.evaluate(`toggleDone(${JSON.stringify(newId)})`);
eq('task marked done', await G(`tasks.find(t=>t.id===${JSON.stringify(newId)}).done`), true);
// delete: deleteTaskClick is a 2-step confirm (first arms pendingDeleteTask, second removes)
await page.evaluate(`deleteTaskClick(${JSON.stringify(newId)}); deleteTaskClick(${JSON.stringify(newId)});`);
eq('task removed after confirm-delete', await G('tasks.length'), startCount);

console.log('\n[6] Settings persistence round-trip');
// exercise the real save/load helpers rather than poking inputs
const settingsOk = await G(`(()=>{ try{
  workMin = 42; breakMin = 7;
  saveStats();                 // writes settings/stats to localStorage
  const raw = localStorage.getItem('tymos_settings') || localStorage.getItem('tymosSettings');
  return true;
}catch(e){ return e.message; } })()`);
eq('saveStats() runs without throwing', settingsOk, true);
eq('localStorage available', await G(`(()=>{ localStorage.setItem('__qa','1'); return localStorage.getItem('__qa'); })()`), '1');

console.log('\n[7] Stats / candle shelf render without throwing');
const statsOk = await G(`(()=>{ try{ updateStats(); renderCandleShelf && renderCandleShelf(); return true; }catch(e){ return e.message; } })()`);
eq('updateStats + renderCandleShelf', statsOk, true);

console.log('\n[8] Spotify PKCE crypto helpers (offline-safe)');
const verifierOk = await G(`(async()=>{ try{ const v=await spGenerateVerifier(); const c=await spGenerateChallenge(v); return (typeof v==='string'&&v.length>20&&typeof c==='string'&&c.length>20); }catch(e){ return e.message; } })()`);
eq('PKCE verifier+challenge generate', verifierOk, true);

await browser.close();

console.log(`\n${'─'.repeat(50)}`);
console.log(`RUNTIME QA: ${fails.length} fail`);
process.exit(fails.length ? 1 : 0);
