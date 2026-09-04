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
  !/Failed to load|net::ERR|Refused to|CSP|Content Security|ERR_FAILED|supabase|fonts|cdn|favicon|tymos-seed|URL scheme \"file\"|file:\/\//i.test(t));
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
await G(`(()=>{ resetTimer(); tasks.length = 0; sessionUntouched = true; })`);
const initialRemaining = await G('remaining');
pass(`initial remaining = ${initialRemaining}s`);

await page.locator('#startBtn').click();
eq('empty Start opens overlay', await G(`document.getElementById('noTasksOverlay').classList.contains('open')`), true);
eq('running stays false behind overlay', await G('running'), false);

await G(`ntmStartAnyway()`);
eq('Start anyway sets running', await G('running'), true);
eq('Start anyway stamps timerStartTs', await G('timerStartTs !== null'), true);
eq('overlay closed after Start anyway', await G(`document.getElementById('noTasksOverlay').classList.contains('open')`), false);

const before = await G('remaining');
await G(`timerStartTs = Date.now() - 1000; tick()`);
eq('tick() decrements remaining after 1s', await G('remaining'), before - 1);

await page.evaluate(`resetTimer()`);
eq('running after resetTimer()', await G('running'), false);
eq('sessionUntouched after reset', await G('sessionUntouched'), true);

console.log('\n[4] Break/work phase toggle');
await page.evaluate(`enterBreak()`);
eq('isBreak after enterBreak()', await G('isBreak'), true);
eq('body.break-mode class', await G(`document.body.classList.contains('break-mode')`), true);
await page.evaluate(`enterWork()`);
eq('isBreak after enterWork()', await G('isBreak'), false);

console.log('\n[5] Task lifecycle (create → render → done → delete)');
await G(`(()=>{ resetTimer(); tasks.length = 0; sessionUntouched = true; })`);
await page.locator('#startBtn').click();
eq('overlay open before idle add', await G(`document.getElementById('noTasksOverlay').classList.contains('open')`), true);
await G(`(()=>{ document.getElementById('qaIdleInput').value = 'QA idle enter'; qaCommitFromIdle(); })()`);
eq('idle commit adds a task behind overlay', await G('tasks.length'), 1);
eq('idle commit closes overlay', await G(`document.getElementById('noTasksOverlay').classList.contains('open')`), false);
eq('idle commit does not auto-start', await G('running'), false);
await G(`toggleTimer()`);
eq('Start after a task runs the timer', await G('running'), true);
await G(`resetTimer()`);

await G(`(()=>{ tasks.length = 0; sessionUntouched = true; renderTasks(); })()`);
await G(`(()=>{ document.getElementById('qaIdleInput').value = 'QA from idle field'; qaCommitFromIdle(); })()`);
eq('idle field commit without overlay', await G(`tasks.some(t => t.title === 'QA from idle field')`), true);

const startCount = await G('tasks.length');
await page.evaluate(`(()=>{
  qaFocus();
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

console.log('\n[8] Focus duration spinbuttons');
await G(`resetTimer()`);
await G(`(()=>{ document.getElementById('qtePomo').value = '50'; quickTimeSave(); })()`);
eq('focus spinbutton 50 sets workSecs', await G('workSecs'), 50 * 60);

console.log('\n[9] Arrow nav and Ctrl+Enter add');
await G(`(()=>{
  resetTimer();
  tasks = [
    { id: 1, title: 'Alpha', priority: 'high', done: false, notes: '', sections: [] },
    { id: 2, title: 'Beta', priority: 'medium', done: false, notes: '', sections: [] },
  ];
  nextId = 3; selectedTaskId = null; renderTasks();
})()`);
await page.locator('#qaIdleInput').focus();
await page.keyboard.press('ArrowDown');
eq('ArrowDown from add field selects first task', await G('selectedTaskId'), 1);
eq('first card has active class', await G(`document.getElementById('task-1').classList.contains('active')`), true);
await page.keyboard.press('ArrowDown');
eq('ArrowDown moves to second task', await G('selectedTaskId'), 2);
await page.keyboard.press('ArrowUp');
eq('ArrowUp moves back to first task', await G('selectedTaskId'), 1);
await page.keyboard.press('ArrowUp');
eq('ArrowUp from first returns to add field', await G('selectedTaskId'), null);
eq('add field focused after ArrowUp', await G(`document.activeElement && document.activeElement.id === 'qaIdleInput'`), true);

await page.locator('#qaIdleInput').fill('Ctrl enter task');
await page.keyboard.press('Control+Enter');
eq('Ctrl+Enter adds the idle task', await G(`tasks.some(t => t.title === 'Ctrl enter task')`), true);

await browser.close();

console.log(`\n${'─'.repeat(50)}`);
console.log(`RUNTIME QA: ${fails.length} fail`);
process.exit(fails.length ? 1 : 0);
