// Joins N isolated players to a What The Dub room, auto-dubs and auto-votes, logs phone-page JS errors.
// Usage: node bots.js <ROOM> [count] [phone page URL]
const { chromium } = require('playwright');

if (!process.argv[2]) {
  console.log('Usage: node bots.js <ROOM> [count=10] [site=https://playwtd.com/]');
  process.exit(1);
}
const ROOM = process.argv[2].toUpperCase();
const COUNT = parseInt(process.argv[3] || '10', 10);
const SITE = process.argv[4] || 'https://playwtd.com/';
const DUBS = [
  'I told you the soup was haunted', 'My lawyer is a goose', 'This is fine, mostly',
  'Who ordered the extra cheese', 'I can explain the llama', 'Not my tractor, not my problem',
  'Mum said it was my turn', 'The pigeons know what you did', 'Is this the IKEA queue',
  'I came here to dance', 'Please return my spoon', 'We ride at dawn, after brunch',
  'Nobody touches the toaster', 'I have opinions about cheese', 'My horoscope warned me',
  'Bring me the tiny hats', 'That was a load-bearing biscuit', 'I am legally a sandwich',
  'Tell the ducks I said hi', 'My other car is a shopping trolley', 'This meeting could have been a nap',
  'Who let the vicar drive', 'I peaked in year four', 'Abort mission, the cat is awake',
  'I only came for the crisps', 'Sir, this is a Wetherspoons', 'Grandma knows kung fu',
];

const log = (who, msg) => console.log(`${new Date().toISOString().slice(11, 19)} [${who}] ${msg}`);

async function runBot(browser, i) {
  const name = `BOT${i + 1}`;
  const ctx = await browser.newContext({ viewport: { width: 400, height: 800 } });
  const page = await ctx.newPage();
  page.on('pageerror', e => log(name, `PAGE ERROR: ${e.message}`));
  await page.goto(SITE, { waitUntil: 'networkidle' });
  await page.evaluate(([n, r]) => {
    document.getElementById('name').value = n;
    document.getElementById('room').value = r;
    joinGame();
  }, [name, ROOM]);
  log(name, 'join requested');

  let lastState = '';
  let dubbedRound = -1;
  let round = 0;
  for (;;) {
    let state;
    try {
      state = await page.evaluate(() => {
        const vis = id => { const el = document.getElementById(id); return !!el && el.offsetParent !== null; };
        const buttons = Array.from(document.querySelectorAll('.vote-btn')).filter(b => b.offsetParent !== null).length;
        return {
          dub: vis('enterDubWrapper'),
          vote: vis('votingWrapper'),
          buttons,
          msg: (document.getElementById('message') || {}).innerText || '',
          alert: vis('alert') ? (document.getElementById('alertMessage') || {}).innerText : '',
        };
      });
    } catch (e) {
      log(name, `eval failed: ${e.message}`);
      return;
    }

    const key = JSON.stringify(state);
    if (key !== lastState) {
      lastState = key;
      if (state.alert) log(name, `ALERT: ${state.alert}`);
      else if (!state.dub && !state.vote) log(name, `msg: ${state.msg.replace(/\s+/g, ' ').slice(0, 80)}`);
    }

    if (state.dub && dubbedRound !== round) {
      dubbedRound = round;
      await page.waitForTimeout(1500 + Math.random() * 4000);
      const text = DUBS[(i + round * 3) % DUBS.length];
      await page.evaluate(t => { document.getElementById('enterDub').value = t; submitDub(); }, text);
      log(name, `submitted dub "${text}"`);
    }

    if (state.vote) {
      log(name, `voting screen shows ${state.buttons} button(s)`);
      await page.waitForTimeout(1000 + Math.random() * 3000);
      await page.evaluate(n => voteSelected(Math.floor(Math.random() * n)), Math.max(1, state.buttons)).catch(e => log(name, `vote failed: ${e.message}`));
      log(name, 'voted');
      round++;
      await page.waitForTimeout(1500);
    }

    await page.waitForTimeout(700);
  }
}

(async () => {
  const browser = await chromium.launch();
  const bots = [];
  for (let i = 0; i < COUNT; i++) {
    bots.push(runBot(browser, i).catch(e => log(`BOT${i + 1}`, `crashed: ${e.message}`)));
    await new Promise(r => setTimeout(r, 1200));
  }
  await Promise.all(bots);
})();
