// AFKit V2 — PokeIdle grid mode (injected by AFKit into every page; only acts on pokeidle.io).
//
// Names that belong to the game itself (DOM ids/classes, localStorage keys, game modules and history fields such as
// #cena-sessao, 'sessao-hunt:', definirImersivo, em/nome/potencia…) stay in Portuguese on purpose.
// Texts shown to the user come from window.__afkitText, which AFKit prepends in the user's language.
//
// Protocol with AFKit (fixed):
//  AFKit → Page (strings via chrome.webview 'message'):
//   'afkit-grid:1' / 'afkit-grid:0' — this tab entered / left a grid cell (overlay + immersive + layout).
//   'afkit-info:1' / 'afkit-info:0' — AFKit wants / no longer wants this tab's data for the battle window.
//   'afkit-vis:1'  / 'afkit-vis:0'  — the tab is / is not visible on screen. Default until something arrives: visible.
//   'afkit-grid-ping'               — asks for the 'ready' message again.
//   'afkit-eco:1' / 'afkit-eco:0'   — turn the game's Economy Mode on / off (cell header button). In Economy Mode the cell
//       shows the game as it is (no immersive, no watermarks, no layout). What was turned on this way goes off on leaving the grid.
//   Everything may arrive before the game is ready: we keep the state and apply it later. Repeats do nothing.
//  Page → AFKit (chrome.webview.postMessage):
//   {afkit:'grid', type:'ready'} when the game is ready (#cena-sessao, #cena-capturas and login present).
//   {afkit:'grid', type:'eco', on:bool} Economy Mode state: with 'ready' and whenever it changes.
//   Only while 'afkit-info:1':
//   {afkit:'grid', type:'summary', time, xp, gold, profit} — strings formatted as in the summary card (xp = trainer XP
//       per hour; profit null if unknown, may come with "−"/"-"). At most 1/s and only when something changes.
//   {afkit:'grid', type:'caps', record: Row|null, recent: Row[]} — session record + up to 5 most recent (the
//       record is not repeated). Only when the signature changes. Row = {k:'em:id', name, level, time, img (data URL|null),
//       shiny, border ('rgb(…)'|null), seals:[{t, bg, color}]}; provisional row: img/border null, seals [{t:'P#'}].
//   On receiving 'afkit-info:1' both are sent right away (forced).
//
// Collection (1 s timer, reading the summary, the capture history and the cache): runs while the tab is in the grid
// OR with 'afkit-info:1'. With neither there is no timer (only the initial wait for the game to be ready).
// Game panels opened underneath (session kept open, captures opened for a moment): only with
// (grid + immersive on) OR ('afkit-info:1' + tab off screen). When 'afkit-vis:1' or 'afkit-info:0' arrives the
// panels go back to the noted state right away. Without panels: numbers from the game's localStorage and captures from the cache.
//
// What it does while the tab is in the grid:
//  1. Turns on the game's own "Immersive Mode" (definirImersivo from /preferencias.mjs). If the user leaves it through
//     the game's button (#imersivo-sair), we respect that: we do not turn it back on in this tab until it leaves the grid
//     and comes back (sessionStorage 'afkit-grid-no-immersive', survives F5). On leaving the grid, it turns immersive off.
//  2. Draws watermarks over the battle (own container in document.body, clicks pass through):
//     - top-left: card with the session summary (Time, XP/h, Gold/h, Profit/h — green/red by sign);
//       with the mouse over it shows a (visual-only) copy of the game's full session panel;
//     - top-right: highlight card with the session record (highest grade, even if old) and, below, the 5
//       most recent captures by time (without repeating the record). Each row is read from the game's captures panel
//       as DATA (portrait → data URL, name, level, time, P/IV/Q/N seals and border with the computed colors) and
//       drawn by compactRow(); the same data goes to the battle window. No reading yet → provisional row.
//       A pokéball button above the list shows/hides it (localStorage 'afkit-grid-caps-hidden') and "Clear all" empties
//       it: the time is saved in 'afkit-grid-caps-cleared:<nick>' and older captures are no longer listed (the game's
//       history is not touched; the battle window follows).
//     With immersive on the game's panels are invisible, so we keep the session panel open (so the
//     game keeps updating the numbers) and open the captures one for a moment when there is a new capture without
//     a reading (at most 4 tries in a row per key, then every 60 s). With immersive off (and the tab
//     visible) no panel is opened: the numbers come from the game's localStorage.
//  On leaving the grid (or if immersive is turned off) the panels go back to the state they were in and everything
//  created is removed; the timers stop, unless 'afkit-info:1' is still on (then collection and sending go on).
//
// From the login object (localStorage 'sessao') we only read the `nick` field; nothing else is stored or sent.
// Does not use the {afkit:'tb'} message nor 'afkit-tb-resync' (they belong to taskbar-probe.js).
(() => {
  'use strict';
  try {
    if (window.top !== window) return;                       // main page only, not iframes
    if (!/(^|\.)pokeidle\.io$/i.test(location.hostname)) return;
    if (window.__afkitGrid) return;
    window.__afkitGrid = true;
  } catch (e) { return; }

  const KEY_NO_IMMERSIVE = 'afkit-grid-no-immersive', KEY_ECO_SET = 'afkit-grid-eco-set';
  const SEL_SESSION_BTN = '#cena-sessao', SEL_SESSION = '#sessao-painel';
  const SEL_CAP_BTN = '#cena-capturas', SEL_CAP = '#capturas-painel';

  // Texts in the user's language (AFKit defines window.__afkitText before this script). English as a fallback.
  const TXT = Object.assign({
    Time: 'Time', XpPerHour: 'XP/h', GoldPerHour: 'Gold/h', ProfitPerHour: 'Profit/h',
    SessionRecord: 'Session record', Level: 'Lv',
    Kills: 'Defeated', KillsPerHour: 'Defeated/h', Captures: 'Captures', CapturesPerHour: 'Captures/h', Shiny: 'Shiny seen/caught',
    ShowCaptures: 'Show captures', HideCaptures: 'Hide captures', ClearAll: 'Clear all'
  }, window.__afkitText || {});

  // AFKit's own preferences (own keys, the game's data is never written): list shown/hidden, and the moment of the last
  // "Clear all" (captures up to then are no longer listed — the game's own history stays untouched).
  const KEY_CAPS_HIDDEN = 'afkit-grid-caps-hidden', KEY_CAPS_CLEARED = 'afkit-grid-caps-cleared:';
  const lsGet = (k) => { try { return localStorage.getItem(k); } catch (e) { return null; } };
  const lsSet = (k, v) => { try { localStorage.setItem(k, v); } catch (e) { } };
  const capsHidden = () => lsGet(KEY_CAPS_HIDDEN) === '1';
  const clearedAt = (n) => (n && Number(lsGet(KEY_CAPS_CLEARED + n.toLowerCase()))) || 0;

  // ------------------------------------------------------------ utilities
  const $ = (s) => document.querySelector(s);
  const isOpen = (s) => { const el = $(s); return !!el && !el.classList.contains('hidden'); };
  const click = (s) => { try { const el = $(s); if (el) el.click(); } catch (e) { } };
  const textOfSel = (s) => { const el = $(s); return el ? (el.textContent || '').trim() : ''; };
  const ss = {
    get: (k) => { try { return sessionStorage.getItem(k); } catch (e) { return null; } },
    set: (k, v) => { try { sessionStorage.setItem(k, v); } catch (e) { } },
    del: (k) => { try { sessionStorage.removeItem(k); } catch (e) { } },
  };
  const lsJson = (k) => { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : null; } catch (e) { return null; } };
  // Only the nick: the rest of the object (including the token) is discarded right away.
  const readNick = () => {
    try {
      const s = JSON.parse(localStorage.getItem('sessao') || 'null');
      return (s && typeof s.nick === 'string' && s.nick) ? s.nick : null;
    } catch (e) { return null; }
  };
  // The game formats its numbers the Brazilian way ("12.980.326"); the fallback numbers match that.
  const fmtInt = (n) => Number.isFinite(n) ? Math.round(n).toLocaleString('pt-BR') : '—';
  const pad2 = (n) => String(n).padStart(2, '0');
  const fmtDuration = (ms) => {
    if (!Number.isFinite(ms) || ms < 0) return '—';
    const s = Math.floor(ms / 1000);
    return `${pad2(Math.floor(s / 3600))}:${pad2(Math.floor(s / 60) % 60)}:${pad2(s % 60)}`;
  };
  const immersiveActive = () => { try { return document.documentElement.classList.contains('modo-imersivo'); } catch (e) { return false; } };

  const post = (msg) => {
    try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage(Object.assign({ afkit: 'grid' }, msg)); }
    catch (e) { /* AFKit closing the tab */ }
  };

  // ------------------------------------------------------------ the game's immersive mode
  // Same URL app.js imports → same module instance (same game state).
  let prefsPromise = null;
  const getPrefs = () => (prefsPromise = prefsPromise || import('/preferencias.mjs').catch(() => null));

  const setImmersive = async (on) => {
    try {
      const m = await getPrefs();
      if (m && typeof m.definirImersivo === 'function') {
        let current = null;
        try { if (typeof m.imersivoLigado === 'function') current = !!m.imersivoLigado(); } catch (e) { }
        if (current === null) current = immersiveActive();
        if (current !== on) m.definirImersivo(on);
        return;
      }
    } catch (e) { }
    // Fallback: only the class on <html> and the saved preference.
    try { document.documentElement.classList.toggle('modo-imersivo', on); } catch (e) { }
    try { localStorage.setItem('cfg-imersivo', on ? '1' : '0'); } catch (e) { }
  };

  // ------------------------------------------------------------ state
  let wanted = null;        // last request from AFKit: true (grid) / false (out) / null (none yet)
  let applied = null;       // what has already been applied
  let ready = false;
  let nick = null;
  let timer = null, cycle = 0, observer = null;
  let capTimeout = null, capPhase = false, needCaps = false;
  let noted = null;         // {s, c}: panels that were open before we started touching them
  let root = null, styleEl = null, els = {};
  let lastEco = null;       // last Economy Mode state seen/reported
  let hover = false, lastClone = null, lastProfit = null;
  let fbMs = null, fbChangedAt = 0;
  let lastTop = [], capsSignature = '';
  let lastRecord = null;    // {k, x}: record to show in the highlight (x = history item, if any)
  let recordK = null;       // data-k of the record read from the game's .cap-recorde button (null → fallback by grade)
  let cloneVersion = 0;
  // data-k ("em:id") -> {grade, gradeTxt, data (Row without k/shiny), clone (compactRow(data)), imgOk, ver, tries, triedAt}
  const cache = new Map();
  // AFKit's battle window.
  let infoOn = false;       // 'afkit-info:1' received
  let infoPending = false;  // forced send waiting for the game to be ready
  let visible = true;       // 'afkit-vis:*' (default: visible, the safe side)
  let summarySent = '', summarySentAt = 0, signatureSent = '';

  // Economy Mode (the game's own: no scene, only the numbers panel): in it the cell shows the game as it is — no immersive
  // mode, no watermarks, no layout — so everything below that is "grid + immersive" is off while it is on.
  const ecoActive = () => { try { return document.documentElement.classList.contains('modo-economia'); } catch (e) { return false; } };
  const inGridImmersive = () => applied === true && immersiveActive() && !ecoActive();
  const collectWanted = () => applied === true || infoOn;
  // May the game's panels stay open? Grid + immersive (invisible) or battle window on with the tab off screen.
  const canHoldPanels = () => inGridImmersive() || (infoOn && !visible);

  const snapshot = () => ({ s: isOpen(SEL_SESSION), c: isOpen(SEL_CAP) });
  const restore = (n) => {
    try {
      if (isOpen(SEL_SESSION) && !n.s) click(SEL_SESSION_BTN);
      if (isOpen(SEL_CAP) && !n.c) click(SEL_CAP_BTN);
      if (!isOpen(SEL_SESSION) && n.s) click(SEL_SESSION_BTN);
      if (!isOpen(SEL_CAP) && n.c) click(SEL_CAP_BTN);
    } catch (e) { }
  };
  const cancelCaps = () => { if (capTimeout) clearTimeout(capTimeout); capTimeout = null; capPhase = false; };

  // "Live" = we may keep panels open (nobody sees them): grid + immersive, or battle window with the tab hidden.
  // Notes the panels' state when it starts and hands it back when it ends. Only notes while collection is running.
  const checkMode = () => {
    const live = !!timer && canHoldPanels();
    if (live && !noted) { noted = snapshot(); needCaps = true; }
    else if (!live && noted) { cancelCaps(); restore(noted); noted = null; }
    // The watermarks (summary, captures) exist only while in the grid with immersive on: leaving immersive (or turning
    // Economy Mode on) removes them, and they come back with it.
    if (inGridImmersive()) createOverlay(); else removeOverlay();
    if (!inGridImmersive()) undoLayout(); // outside grid/immersive: the cell goes back to the game's normal layout
    return live;
  };
  const keepSession = () => { if (noted && !capPhase && !isOpen(SEL_SESSION)) click(SEL_SESSION_BTN); };

  // ------------------------------------------------------------ watermarks
  const CSS = `
/* safeguard: the game panels we keep open never show up over the battle in immersive mode */
html.modo-imersivo #sessao-painel,html.modo-imersivo #capturas-painel{visibility:hidden !important}
#afkit-gw-root{position:fixed;inset:0;pointer-events:none;z-index:2147483000;overflow:hidden}
#afkit-gw-root .afkit-gw-mark{font-family:Tahoma,Verdana,"Segoe UI",sans-serif;font-size:clamp(10px,calc(1.4vmin + 6px),14px);line-height:1.3;color:#fff}
/* readable card: translucent dark background + blur, subtle border, rounded corners, shadow */
#afkit-gw-root .afkit-gw-card{background:rgba(16,10,14,.72);-webkit-backdrop-filter:blur(3px);backdrop-filter:blur(3px);
  border:1px solid rgba(255,255,255,.14);border-radius:8px;box-shadow:0 2px 10px rgba(0,0,0,.45);opacity:.92;
  padding:clamp(3px,.9vmin,7px) clamp(5px,1.3vmin,10px)}
#afkit-gw-session{position:absolute;top:6px;left:6px;pointer-events:auto;max-width:calc(100vw - 12px)}
#afkit-gw-root .afkit-gw-summary{display:grid;grid-template-columns:auto auto;column-gap:.9em;row-gap:.1em;width:max-content;cursor:default}
#afkit-gw-root .afkit-gw-k{color:#c9c2c7;font-weight:normal}
#afkit-gw-root .afkit-gw-v{color:#fff;font-weight:bold;text-align:right;font-variant-numeric:tabular-nums}
#afkit-gw-root .afkit-gw-v.afkit-gw-pos{color:#5fe08a}
#afkit-gw-root .afkit-gw-v.afkit-gw-neg{color:#ff6b6b}
#afkit-gw-panel{display:none;margin-top:4px;opacity:1;overflow:auto;overscroll-behavior:contain;max-width:calc(100vw - 12px);max-height:calc(100vh - 80px)}
#afkit-gw-panel > *{position:static !important;inset:auto !important;transform:none !important;margin:0 !important;max-height:none !important;visibility:visible !important;opacity:1 !important}
#afkit-gw-caps{position:absolute;top:56px;right:6px;display:flex;flex-direction:column;gap:clamp(3px,.8vmin,6px);width:max-content;max-width:min(46vw,300px)}
#afkit-gw-root .afkit-gw-block{min-width:0;max-width:100%;box-sizing:border-box}
#afkit-gw-root .afkit-gw-tools{display:flex;justify-content:flex-end;align-items:center;gap:6px;pointer-events:auto}
#afkit-gw-root .afkit-gw-btn{pointer-events:auto;cursor:pointer;font:inherit;font-size:.9em;color:#fff;background:rgba(16,10,14,.8);
  border:1px solid rgba(255,255,255,.25);border-radius:6px;padding:.15em .6em}
#afkit-gw-root .afkit-gw-btn:hover{background:rgba(120,30,40,.9);border-color:rgba(255,255,255,.5)}
/* pokéball: red top, white bottom, black band and center button (pure CSS) */
#afkit-gw-root .afkit-gw-ball{pointer-events:auto;cursor:pointer;padding:0;width:2.4em;height:2.4em;border-radius:50%;box-sizing:border-box;
  border:2px solid #111;position:relative;overflow:hidden;opacity:.9;
  background:linear-gradient(#e8393b 0 46%,#111 46% 54%,#f4f4f4 54% 100%);box-shadow:0 2px 6px rgba(0,0,0,.5)}
#afkit-gw-root .afkit-gw-ball::after{content:"";position:absolute;left:50%;top:50%;width:34%;height:34%;transform:translate(-50%,-50%);
  border-radius:50%;background:#f4f4f4;border:2px solid #111;box-sizing:border-box}
#afkit-gw-root .afkit-gw-ball:hover{opacity:1;filter:brightness(1.1)}
#afkit-gw-root .afkit-gw-ball.afkit-gw-off{filter:grayscale(.85) brightness(.8)}
#afkit-gw-root .afkit-gw-highlight{border-color:#ffcc33;box-shadow:0 0 0 1px rgba(255,204,51,.35),0 0 8px rgba(255,204,51,.4),0 2px 10px rgba(0,0,0,.45)}
#afkit-gw-root .afkit-gw-label{display:block;font-size:.8em;color:#ffdd77;font-weight:bold;letter-spacing:.03em;margin:0 0 .25em}
/* captures: compact row in the game's style (round portrait, name, level, time and colored seals on a single line) */
#afkit-gw-caps{font-size:clamp(9px,calc(1.1vmin + 5px),12px);max-width:min(42vw,240px)}
#afkit-gw-root .afkit-gw-list{display:flex;flex-direction:column;gap:3px;min-width:0}
#afkit-gw-root .afkit-gw-row{display:flex;align-items:center;gap:.5em;padding:.2em .5em .2em .3em;min-width:0;box-sizing:border-box;
  border-left:3px solid rgba(255,255,255,.3);border-radius:5px;background:rgba(255,255,255,.05)}
#afkit-gw-root .afkit-gw-portrait{flex:0 0 auto;width:2.4em;height:2.4em;border-radius:50%;background:rgba(0,0,0,.5);
  display:flex;align-items:flex-end;justify-content:center;overflow:hidden}
#afkit-gw-root .afkit-gw-portrait img{height:112%;width:auto;image-rendering:pixelated;display:block}
#afkit-gw-root .afkit-gw-body{flex:1 1 auto;display:flex;flex-direction:column;gap:.15em;min-width:0}
#afkit-gw-root .afkit-gw-l1{display:flex;align-items:baseline;gap:.4em;white-space:nowrap;min-width:0}
#afkit-gw-root .afkit-gw-name{font-weight:bold;overflow:hidden;text-overflow:ellipsis;min-width:0}
#afkit-gw-root .afkit-gw-level{font-style:normal;color:#c9c2c7;font-size:.85em;flex:0 0 auto}
#afkit-gw-root .afkit-gw-time{margin-left:auto;padding-left:.4em;color:#a9a2a7;font-size:.8em;flex:0 0 auto}
#afkit-gw-root .afkit-gw-shiny{color:#ffe066;flex:0 0 auto}
#afkit-gw-root .afkit-gw-seals{display:flex;gap:.25em;flex-wrap:nowrap;white-space:nowrap;overflow:hidden}
#afkit-gw-root .afkit-gw-seal{font-size:.78em;font-weight:bold;line-height:1.4;padding:0 .35em;border-radius:3px;
  background:rgba(255,255,255,.18);color:#fff;text-shadow:none;flex:0 0 auto}
`;

  const el = (tag, cls, txt) => {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (txt !== undefined) e.textContent = txt;
    return e;
  };

  const createOverlay = () => {
    if (root) return;
    styleEl = el('style'); styleEl.id = 'afkit-gw-style'; styleEl.textContent = CSS;
    (document.head || document.documentElement).appendChild(styleEl);

    root = el('div'); root.id = 'afkit-gw-root';
    const session = el('div'); session.id = 'afkit-gw-session';
    const summary = el('div', 'afkit-gw-mark afkit-gw-card afkit-gw-summary');
    const addRow = (label, id) => {
      const k = el('span', 'afkit-gw-k', label);
      summary.appendChild(k);
      const v = el('span', 'afkit-gw-v', '—'); v.id = id; v.afkitLabel = k; summary.appendChild(v); return v;
    };
    els.time = addRow(TXT.Time, 'afkit-gw-time');
    els.xp = addRow(TXT.XpPerHour, 'afkit-gw-xp');
    els.gold = addRow(TXT.GoldPerHour, 'afkit-gw-gold');
    els.profit = addRow(TXT.ProfitPerHour, 'afkit-gw-profit');
    // Rows that used to exist only in the hover copy; hidden while the game has no value for them (see setExtra).
    els.kills = addRow(TXT.Kills, 'afkit-gw-kills');
    els.killsH = addRow(TXT.KillsPerHour, 'afkit-gw-killsh');
    els.capsN = addRow(TXT.Captures, 'afkit-gw-capsn');
    els.capsH = addRow(TXT.CapturesPerHour, 'afkit-gw-capsh');
    els.shiny = addRow(TXT.Shiny, 'afkit-gw-shiny');
    for (const k of ['kills', 'killsH', 'capsN', 'capsH', 'shiny']) { els[k].style.display = 'none'; els[k].afkitLabel.style.display = 'none'; }
    const panel = el('div'); panel.id = 'afkit-gw-panel';
    session.append(summary, panel);
    els.panel = panel;

    // The session block receives the mouse (hover), but no click reaches the game and nothing happens inside the clone.
    for (const t of ['click', 'dblclick', 'auxclick', 'contextmenu']) {
      session.addEventListener(t, (e) => { e.preventDefault(); e.stopPropagation(); });
    }
    for (const t of ['mousedown', 'mouseup', 'pointerdown', 'pointerup', 'touchstart', 'touchend', 'wheel', 'keydown']) {
      session.addEventListener(t, (e) => { e.stopPropagation(); }, { passive: true });
    }
    session.addEventListener('mouseenter', () => { hover = true; try { showClone(); } catch (e) { } });
    session.addEventListener('mouseleave', () => {
      hover = false;
      try { panel.style.display = 'none'; panel.replaceChildren(); } catch (e) { }
    });

    const caps = el('div', 'afkit-gw-mark'); caps.id = 'afkit-gw-caps'; caps.style.display = 'none';
    els.caps = caps;
    root.append(session, caps);
    (document.body || document.documentElement).appendChild(root);
  };

  const removeOverlay = () => {
    try { if (root) root.remove(); } catch (e) { }
    try { if (styleEl) styleEl.remove(); } catch (e) { }
    root = null; styleEl = null; els = {}; hover = false; capsSignature = '';
  };

  // Visual-only copy of the game's session panel: no ids and no name (so it does not steal state from real inputs).
  const prepareClone = (p) => {
    const c = p.cloneNode(true);
    c.removeAttribute('id');
    c.classList.remove('hidden');
    c.querySelectorAll('[id]').forEach((x) => x.removeAttribute('id'));
    c.querySelectorAll('[name]').forEach((x) => x.removeAttribute('name'));
    c.querySelectorAll('[for]').forEach((x) => x.removeAttribute('for'));
    return c;
  };

  const showClone = () => {
    const panel = els.panel;
    if (!panel) return;
    if (!lastClone) { panel.style.display = 'none'; panel.replaceChildren(); return; }
    const scroll = panel.scrollTop;
    panel.replaceChildren(lastClone.cloneNode(true));
    panel.style.display = 'block';
    const top = panel.getBoundingClientRect().top;
    panel.style.maxHeight = Math.max(60, window.innerHeight - top - 6) + 'px';
    panel.scrollTop = scroll;
  };

  // ------------------------------------------------------------ session summary
  // Sending to the battle window: only with 'afkit-info:1', only when a value changes and at most once per second (forced ignores).
  const sendSummary = (r, force) => {
    if (!infoOn) return;
    const s = JSON.stringify([r.time, r.xp, r.gold, r.profit]);
    const now = Date.now();
    if (!force && (s === summarySent || now - summarySentAt < 900)) return;   // 900: slack for setInterval
    summarySent = s; summarySentAt = now;
    post({ type: 'summary', time: String(r.time), xp: String(r.xp), gold: String(r.gold), profit: r.profit == null ? null : String(r.profit) });
  };
  // Extra rows of the card (defeated, captures, rates, shiny): the same numbers the game shows in the session panel.
  const toNum = (s) => Number(String(s).replace(/\./g, '').replace(',', '.'));
  const durHours = (t) => {   // "22:36:29" / "3:34" → hours (null if unknown)
    const p = String(t).split(':').map(Number);
    if (p.length < 2 || p.some((n) => !Number.isFinite(n))) return null;
    const s = p.length === 3 ? p[0] * 3600 + p[1] * 60 + p[2] : p[0] * 60 + p[1];
    return s > 0 ? s / 3600 : null;
  };
  const perSec = (perHour) => (perHour / 3600).toLocaleString('pt-BR', { maximumFractionDigits: 3 }) + '/s';
  const setExtra = (cell, text) => {
    const show = text !== null && text !== undefined;
    const d = show ? '' : 'none';
    if (cell.style.display !== d) { cell.style.display = d; cell.afkitLabel.style.display = d; }
    if (show && cell.textContent !== text) cell.textContent = text;
  };
  const updateExtras = (ex, time) => {
    const hours = durHours(time);
    const kh = ex.killsH !== null && ex.killsH !== undefined ? toNum(ex.killsH) : null;
    setExtra(els.kills, ex.kills);
    setExtra(els.killsH, kh !== null && Number.isFinite(kh) ? `${ex.killsH} · ${perSec(kh)}` : null);
    setExtra(els.capsN, ex.caps);
    const c = ex.caps !== null && hours ? toNum(ex.caps) / hours : null;   // the game shows no per-hour for captures
    setExtra(els.capsH, c !== null && Number.isFinite(c) ? `${fmtInt(c)} · ${perSec(c)}` : null);
    setExtra(els.shiny, ex.seen !== null && ex.seen !== undefined ? `${ex.seen} / ${ex.shiny ?? '0'}` : null);
  };

  // Reads the numbers (live panel or fallback), updates the card (if there is an overlay) and sends to the battle window (if on).
  const updateSummary = (live, force) => {
    let time, xp, gold, profit;
    let ex = { kills: null, killsH: null, caps: null, seen: null, shiny: null };   // extras: null = the game has no value
    const hasIds = $('#sessao-tempo') && $('#sessao-xp-treinador-h') && $('#sessao-gold-h');
    if (hasIds && (live || isOpen(SEL_SESSION))) {
      // The game only repaints the open panel: here the numbers are live.
      time = textOfSel('#sessao-tempo') || '—';
      xp = textOfSel('#sessao-xp-treinador-h') || '—';
      gold = textOfSel('#sessao-gold-h') || '—';
      const l = textOfSel('#sessao-lucro-h');
      if (l) lastProfit = l;
      profit = lastProfit || '—';
      const p = $(SEL_SESSION);
      if (p && els.panel) lastClone = prepareClone(p);   // hover copy: only exists with the overlay
      const dk = (k) => { const e = p && p.querySelector('[data-k="' + k + '"]'); return e ? (e.textContent || '').trim() : null; };
      ex = { kills: dk('abates'), killsH: textOfSel('#sessao-abates-h') || null, caps: dk('capturas'),
        seen: dk('shiniesVistos'), shiny: dk('shinies') };   // shiny cards only exist where shinies can drop
    } else {
      // Fallback: the summary the game writes to localStorage (at most every 2 s).
      const h = nick ? lsJson('sessao-hunt:' + nick.toLowerCase()) : null;
      const ms = h && Number(h.ms);
      if (Number.isFinite(ms) && ms > 0) {
        const now = Date.now();
        if (fbMs === null) { fbMs = ms; fbChangedAt = 0; }       // old value: do not extrapolate until it changes
        else if (ms !== fbMs) { fbMs = ms; fbChangedAt = now; }
        const extra = fbChangedAt && (now - fbChangedAt) <= 10000 ? now - fbChangedAt : 0;
        const hours = ms / 3600000;
        time = fmtDuration(ms + extra);
        xp = fmtInt(Number(h.xpTreinador) / hours);
        gold = fmtInt(Number(h.gold) / hours);
        const f = (v) => (Number.isFinite(Number(v)) && v !== null && v !== undefined) ? fmtInt(Number(v)) : null;
        ex = { kills: f(h.abates), killsH: f(h.abates) && fmtInt(Number(h.abates) / hours), caps: f(h.capturas),
          seen: f(h.shiniesVistos), shiny: f(h.shinies) };
      } else {
        time = '—'; xp = '—'; gold = '—';
      }
      profit = lastProfit || '—';
    }
    try { sendSummary({ time, xp, gold, profit: lastProfit || null }, force); } catch (e) { }
    if (!els.time) return;
    try { updateExtras(ex, time); } catch (e) { }
    if (els.time.textContent !== time) els.time.textContent = time;
    if (els.xp.textContent !== xp) els.xp.textContent = xp;
    if (els.gold.textContent !== gold) els.gold.textContent = gold;
    if (els.profit.textContent !== profit) els.profit.textContent = profit;
    try {
      // Sign of Profit/h: "−93.951" (unicode minus), "-93.951" or "–" → red; non-zero number → green.
      const neg = /^\s*[−\-–]\s*\d/.test(profit);
      const pos = !neg && /[1-9]/.test(profit);
      els.profit.classList.toggle('afkit-gw-neg', neg);
      els.profit.classList.toggle('afkit-gw-pos', pos);
    } catch (e) { }
  };

  // ------------------------------------------------------------ recent captures
  const store = (k, v) => {
    cache.delete(k); cache.set(k, v);
    while (cache.size > 100) cache.delete(cache.keys().next().value);
  };

  const readGrade = (t) => {
    const m = String(t || '').match(/-?\d+(?:[.,]\d+)?/);
    if (!m) return { num: null, txt: null };
    const num = parseFloat(m[0].replace(',', '.'));
    return Number.isFinite(num) ? { num, txt: m[0].replace('.', ',') } : { num: null, txt: null };
  };

  // Copies the portrait (pixel art) from the game's panel. Blank → null (tries again later).
  const copyCanvas = (c) => {
    try {
      if (!c || !c.width || !c.height) return null;
      const o = document.createElement('canvas');
      o.width = c.width; o.height = c.height;
      const ctx = o.getContext('2d');
      ctx.drawImage(c, 0, 0);
      const d = ctx.getImageData(0, 0, o.width, o.height).data;
      let any = false;
      for (let i = 3; i < d.length; i += 4) { if (d[i]) { any = true; break; } }
      return any ? o.toDataURL('image/png') : null;
    } catch (e) { return null; }
  };

  // Compact row built from the row of the game's captures panel: portrait (canvas → <img>), name, level,
  // time and seals (P/IV/Q/N) with the game's computed colors copied into the style (does not depend on the panel's CSS).
  // imgOk = false if the portrait was still blank (tries again on a later reading).
  const textOf = (parent, sel) => { try { const e = parent.querySelector(sel); return e ? (e.textContent || '').trim() : ''; } catch (e) { return ''; } };
  const compactRow = ({ name, level, time, img, seals, border, shiny }) => {
    const row = el('div', 'afkit-gw-row');
    if (border) row.style.borderLeftColor = border;
    const portrait = el('span', 'afkit-gw-portrait');
    if (img) { const im = el('img'); im.alt = ''; im.src = img; portrait.appendChild(im); }
    const body = el('span', 'afkit-gw-body');
    const l1 = el('span', 'afkit-gw-l1');
    l1.append(el('span', 'afkit-gw-name', name || '?'));
    if (level) l1.append(el('i', 'afkit-gw-level', level));
    if (shiny) l1.append(el('span', 'afkit-gw-shiny', '★'));
    if (time) l1.append(el('span', 'afkit-gw-time', time));
    const sealBox = el('span', 'afkit-gw-seals');
    for (const sl of seals || []) {
      const c = el('span', 'afkit-gw-seal', sl.t);
      if (sl.bg) c.style.backgroundColor = sl.bg;
      if (sl.bi && sl.bi !== 'none') c.style.backgroundImage = sl.bi;
      if (sl.color) c.style.color = sl.color;
      sealBox.appendChild(c);
    }
    body.append(l1, sealBox);
    row.append(portrait, body);
    return row;
  };
  const cloneRow = (li) => {
    let img = null, border = null;
    try { const o = li.querySelector('.cap-retrato canvas') || li.querySelector('canvas'); img = o ? copyCanvas(o) : null; } catch (e) { }
    try {
      const cs = getComputedStyle(li);
      if (parseFloat(cs.borderLeftWidth) > 0) border = cs.borderLeftColor;
    } catch (e) { }
    const seals = [];
    try {
      for (const sEl of li.querySelectorAll('.cap-selos .cm-pot, .cap-selos .pk-selo')) {
        const t = (sEl.textContent || '').trim();
        if (!t) continue;
        let bg = null, bi = null, color = null;
        try { const cs = getComputedStyle(sEl); bg = cs.backgroundColor; bi = cs.backgroundImage; color = cs.color; } catch (e) { }
        seals.push({ t, bg, bi, color });
      }
    } catch (e) { }
    // Row data (a single source: feeds compactRow and the sending to the battle window). Shiny comes from the history.
    const data = { name: textOf(li, '.cap-nome'), level: textOf(li, '.cap-nv'), time: textOf(li, '.cap-hora'), img, seals, border, shiny: false };
    return { data, clone: compactRow(data), imgOk: !!img };
  };

  const empty = () => ({ grade: null, gradeTxt: null, data: null, clone: null, imgOk: false, ver: 0, tries: 0 });

  // Reads the open panel: the record's data-k (.cap-recorde button or li.recorde) and, for the requested keys + the
  // record, clones the row and reads the Grade (N seal).
  const readRows = (keys) => {
    try {
      const btn = document.querySelector(SEL_CAP + ' button.cap-recorde[data-k]') || document.querySelector('button.cap-recorde[data-k]');
      const liRec = document.querySelector(SEL_CAP + ' li.capturas-linha.recorde[data-k]');
      recordK = (btn && btn.getAttribute('data-k')) || (liRec && liRec.getAttribute('data-k')) || null;
      if (recordK) keys = new Set([...keys, recordK]);
    } catch (e) { }
    const seen = new Set();
    for (const li of document.querySelectorAll(SEL_CAP + ' li.capturas-linha[data-k]')) {
      try {
        const k = li.getAttribute('data-k');
        if (!keys.has(k)) continue;
        seen.add(k);
        const prev = cache.get(k) || empty();
        const sealEl = li.querySelector('.pk-selo.pk-selo-n') || li.querySelector('.pk-selo-n');
        const n = readGrade(sealEl && sealEl.textContent);
        let { data, clone, imgOk, ver } = prev;
        if (!data || !imgOk) {
          const r = cloneRow(li);
          // Does not replace a row that has a portrait with one that does not.
          if (!data || r.imgOk) { data = r.data; clone = r.clone; imgOk = r.imgOk; ver = ++cloneVersion; }
        }
        store(k, { grade: n.num ?? prev.grade, gradeTxt: n.txt ?? prev.gradeTxt, data, clone, imgOk, ver, tries: prev.tries + 1, triedAt: Date.now() });
      } catch (e) { }
    }
    for (const k of keys) {
      if (seen.has(k)) continue;
      const prev = cache.get(k) || empty();
      store(k, Object.assign({}, prev, { tries: prev.tries + 1, triedAt: Date.now() }));
    }
  };

  // Opens the captures panel (nobody sees it: immersive or tab off screen), waits ~800 ms, reads and goes back to the
  // session one. If in the meantime panels stop being allowed, cancelCaps() kills this timeout.
  const fetchCaptures = (keys) => {
    capPhase = true;
    if (!isOpen(SEL_CAP)) click(SEL_CAP_BTN);
    capTimeout = setTimeout(() => {
      capTimeout = null;
      try { if (noted && canHoldPanels()) { readRows(keys); needCaps = false; } } catch (e) { }
      capPhase = false;
      try { if (noted && canHoldPanels()) keepSession(); } catch (e) { }
      try { updateCaptures(false); } catch (e) { }   // recomputes record/recent with what was read (without reopening)
    }, 800);
  };

  const keyOf = (x) => `${x.em}:${x.id}`;

  // Is this key still unread? (no entry, or no clone/portrait and still with tries left)
  const missing = (k) => {
    const c = cache.get(k);
    // After 4 tries in a row, it tries again only every 60 s (the row may take a while to show up in the panel).
    return !c || ((!c.data || !c.imgOk) && (c.tries < 4 || Date.now() - (c.triedAt || 0) > 60000));
  };

  const updateCaptures = (live, force) => {
    nick = readNick() || nick;
    const hist = nick ? lsJson('capturas-hist:' + nick.toLowerCase()) : null;
    const cutoff = clearedAt(nick);   // "Clear all": only captures after it are listed
    const arr = Array.isArray(hist) ? hist.filter((x) => x && typeof x === 'object' && (!cutoff || (Number(x.em) || 0) > cutoff)) : [];
    const byDate = arr.slice().sort((a, b) => (Number(b.em) || 0) - (Number(a.em) || 0));
    const byKey = new Map(arr.map((x) => [keyOf(x), x]));

    // Record: the game's one (.cap-recorde button); without it, the highest Grade among the cached captures.
    let rk = cutoff ? null : recordK;   // after a clear the game's record may be an old one: the best of the rest is used
    if (!rk) {
      let best = null;
      for (const [k, c] of cache) {
        if (!byKey.has(k) || !Number.isFinite(c.grade)) continue;
        if (!best || c.grade > best.grade) best = { k, grade: c.grade };
      }
      rk = best ? best.k : null;
    }
    if (!arr.length) rk = null;
    lastRecord = rk && (byKey.has(rk) || (cache.get(rk) || {}).data) ? { k: rk, x: byKey.get(rk) || null } : null;

    // 5 most recent by time, without repeating the record (fills in with the next one).
    lastTop = byDate.filter((x) => !lastRecord || keyOf(x) !== lastRecord.k).slice(0, 5);
    const keys = lastTop.map(keyOf);
    const lacking = keys.some(missing) || (!!lastRecord && missing(lastRecord.k));
    if (lastRecord) keys.push(lastRecord.k);
    // The 6th most recent is also read: if a new capture becomes the record, it enters the list without reopening.
    if (byDate[5]) keys.push(keyOf(byDate[5]));
    if (lacking) needCaps = true;
    if (live && needCaps && !capPhase && keys.length) fetchCaptures(new Set(keys));
    publishCaptures(force);
  };

  // Provisional row data (capture not yet read from the panel): only what the storage has.
  const provisionalData = (x) => {
    const pot = Number(x.potencia) || 0;
    const time = Number(x.em) ? new Date(Number(x.em)).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }) : '';
    return { name: String(x.nome ?? '?'), level: TXT.Level + ' ' + (x.level ?? '?'), time, img: null,
      seals: [{ t: 'P' + (pot || '?') }], border: null, shiny: !!x.shiny };
  };

  // Complete Row (with k and the history's shiny) — the same data that draws the row.
  const rowOf = (k, x) => {
    const c = cache.get(k);
    if (c && c.data) return Object.assign({}, c.data, { k, shiny: !!(x && x.shiny) });
    return x ? Object.assign(provisionalData(x), { k }) : null;
  };

  // Row in the battle window's contract format (seals only with t/bg/color; provisional ones only with t).
  const toPayload = (r) => {
    if (!r) return null;
    const seals = [];
    for (const s of r.seals || []) {
      if (!s || !s.t) continue;
      seals.push(('bg' in s || 'color' in s) ? { t: String(s.t), bg: s.bg || null, color: s.color || null } : { t: String(s.t) });
    }
    return { k: String(r.k), name: String(r.name || '?'), level: String(r.level || ''), time: String(r.time || ''),
      img: r.img || null, shiny: !!r.shiny, border: r.border || null, seals };
  };

  const rowFor = (k, x) => {
    const c = cache.get(k);
    if (c && c.data && c.clone) {
      const row = c.clone.cloneNode(true);
      // Shiny comes from the game's history (shiny field), not from the row's drawing.
      try {
        if (x && x.shiny) {
          const l1 = row.querySelector('.afkit-gw-l1'), time = row.querySelector('.afkit-gw-time');
          if (l1) l1.insertBefore(el('span', 'afkit-gw-shiny', '★'), time);
        }
      } catch (e) { }
      return row;
    }
    return x ? compactRow(provisionalData(x)) : null;
  };

  const wrapRows = (rows) => {
    const list = el('div', 'afkit-gw-list');
    for (const li of rows) if (li) list.appendChild(li);
    return list;
  };

  // Same signature for redrawing the overlay and for sending to the battle window (each keeps its own last one).
  const publishCaptures = (force) => {
    const sig = (k) => { const c = cache.get(k); return c && c.data ? c.ver : 0; };
    const signature = JSON.stringify([
      capsHidden(),
      lastRecord ? [lastRecord.k, sig(lastRecord.k)] : null,
      lastTop.map((x) => { const k = keyOf(x); return [k, sig(k), x.nome, x.level, x.potencia, !!x.shiny]; }),
    ]);
    if (els.caps && signature !== capsSignature) {
      capsSignature = signature;
      try { renderCaptures(); } catch (e) { }
    }
    if (infoOn && (force || signature !== signatureSent)) {
      signatureSent = signature;
      try {
        const record = lastRecord ? toPayload(rowOf(lastRecord.k, lastRecord.x)) : null;
        const recent = lastTop.map((x) => toPayload(rowOf(keyOf(x), x))).filter(Boolean);
        post({ type: 'caps', record, recent });
      } catch (e) { }
    }
  };

  const renderCaptures = () => {
    const box = els.caps;
    if (!box) return;
    const frag = document.createDocumentFragment();
    const hidden = capsHidden();
    const hasRows = !!lastRecord || lastTop.length > 0;
    if (hasRows) {
      // Toolbar: "Clear all" (only with the list open) + the pokéball that shows/hides the list.
      const tools = el('div', 'afkit-gw-tools');
      if (!hidden) {
        const clear = el('button', 'afkit-gw-btn', TXT.ClearAll);
        clear.type = 'button';
        clear.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); clearCaptures(); });
        tools.appendChild(clear);
      }
      const ball = el('button', 'afkit-gw-ball' + (hidden ? ' afkit-gw-off' : ''));
      ball.type = 'button';
      ball.title = hidden ? TXT.ShowCaptures : TXT.HideCaptures;
      ball.setAttribute('aria-label', ball.title);
      ball.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); toggleCaptures(); });
      tools.appendChild(ball);
      for (const t of ['mousedown', 'mouseup', 'pointerdown', 'pointerup', 'dblclick', 'auxclick', 'contextmenu']) {
        tools.addEventListener(t, (e) => e.stopPropagation());
      }
      frag.appendChild(tools);
    }
    if (lastRecord && !hidden) {
      const li = rowFor(lastRecord.k, lastRecord.x);
      if (li) {
        const card = el('div', 'afkit-gw-card afkit-gw-block afkit-gw-highlight');
        card.append(el('span', 'afkit-gw-label', '🏆 ' + TXT.SessionRecord), wrapRows([li]));
        frag.appendChild(card);
      }
    }
    if (lastTop.length && !hidden) {
      const card = el('div', 'afkit-gw-card afkit-gw-block afkit-gw-recent');
      card.appendChild(wrapRows(lastTop.map((x) => rowFor(keyOf(x), x))));
      frag.appendChild(card);
    }
    const has = frag.childNodes.length > 0;
    box.replaceChildren(frag);
    box.style.display = has ? '' : 'none';   // empty history → hides the block
  };

  const toggleCaptures = () => {
    lsSet(KEY_CAPS_HIDDEN, capsHidden() ? '0' : '1');
    capsSignature = '';
    try { renderCaptures(); } catch (e) { }
    try { updateCaptures(false, false); } catch (e) { }   // refreshes the signature
  };

  // "Clear all": from now on only captures after this moment are listed (here and in the battle window).
  const clearCaptures = () => {
    nick = readNick() || nick;
    if (!nick) return;
    lsSet(KEY_CAPS_CLEARED + nick.toLowerCase(), String(Date.now()));
    capsSignature = '';
    try { updateCaptures(false, true); } catch (e) { }
  };

  // ------------------------------------------------------------ grid cell layout
  // In the grid (grid on + immersive on) only the battle shows and fills the whole cell:
  //  - finds the map (largest <canvas> that is a direct child of .palco) and the chain of its ancestors up to <html>;
  //  - hides (attribute data-afkit-gw-hidden) the siblings of each ancestor, except our overlay, "invisible"
  //    tags (style/script/...) and fixed/absolute elements (they may be the game's modals/sheets/toasts);
  //  - also hides, even if fixed/absolute, the version badge ("v1.275.1") and anything with flags or
  //    Discord/X/Twitter/Instagram links (the footer strip), without depending on the game's selectors;
  //  - stretches the html/body/.app/.col.centro/.palco-moldura/.palco chain to 100% of the viewport and fires
  //    'resize' so the game recomputes the canvas. No transform/object-fit on the canvas (it would break the clicks).
  // Never hides the path to the canvas nor anything inside .palco (e.g. #imersivo-sair, .cm-folha).
  const CSS_LAYOUT = `
html.modo-imersivo[data-afkit-grid] [data-afkit-gw-hidden]{display:none !important}
html.modo-imersivo[data-afkit-grid],html.modo-imersivo[data-afkit-grid] body{width:100% !important;height:100% !important;min-height:0 !important;margin:0 !important;padding:0 !important;overflow:hidden !important;background:rgb(5,7,10) !important}
html.modo-imersivo[data-afkit-grid] [data-afkit-gw-path]{box-sizing:border-box !important;width:100% !important;height:100% !important;max-width:none !important;max-height:none !important;min-width:0 !important;min-height:0 !important;margin:0 !important;padding:0 !important}
html.modo-imersivo[data-afkit-grid] .app{height:100vh !important;display:flex !important;flex-direction:column !important;gap:0 !important}
html.modo-imersivo[data-afkit-grid] .col.centro{flex:1 1 auto !important;display:flex !important;flex-direction:column !important;overflow:hidden !important}
html.modo-imersivo[data-afkit-grid] .palco-moldura{flex:1 1 0% !important;display:flex !important;flex-direction:column !important;border:0 !important;border-radius:0 !important;box-shadow:none !important}
html.modo-imersivo[data-afkit-grid] .palco{flex:1 1 0% !important;display:flex !important;align-items:center !important;justify-content:center !important;overflow:hidden !important;border:0 !important;border-radius:0 !important;background:rgb(5,7,10) !important}
html.modo-imersivo[data-afkit-grid] .palco > canvas{flex:0 0 auto !important;margin:0 !important}
`;
  const FREE_TAGS = new Set(['STYLE', 'SCRIPT', 'LINK', 'TEMPLATE', 'NOSCRIPT', 'META', 'TITLE', 'BASE']);
  const RE_VERSION = /^v\d+(\.\d+)+$/i;
  const RE_FLAG = /[\u{1F1E6}-\u{1F1FF}]{2}/u;
  const RE_SOCIAL = /(^|\/\/|\.)(discord\.(gg|com)|x\.com|twitter\.com|instagram\.com)(\/|$|\?|#)/i;
  const SEL_SOCIAL = 'a[href*="discord" i],a[href*="x.com" i],a[href*="twitter" i],a[href*="instagram" i],' +
    '[aria-label*="discord" i],[title*="discord" i],[aria-label*="instagram" i],[title*="instagram" i],' +
    '[aria-label*="twitter" i],[title*="twitter" i]';
  const SEL_FLAG = 'img[src*="flag" i],img[src*="bandeira" i],img[alt*="flag" i],img[alt*="bandeira" i],' +
    '[class*="bandeira" i],[class*="flag" i],[class*="idioma" i]';

  let lyStyle = null, lyActive = false, lyViewport = '', lyHidden = [], lyPath = [], lyExtras = [];
  let lyCycle = 0, lyTimers = [];

  const isOutOfFlow = (e) => { try { const p = getComputedStyle(e).position; return p === 'fixed' || p === 'absolute'; } catch (x) { return false; } };

  const fireResize = () => {
    try { window.dispatchEvent(new Event('resize')); } catch (e) { }
  };
  const staggeredResize = () => {
    try {
      for (const t of lyTimers) clearTimeout(t);
      fireResize();
      lyTimers = [300, 1200].map((ms) => setTimeout(() => { if (lyActive) fireResize(); }, ms));
    } catch (e) { }
  };

  // Largest canvas that is a direct child of some .palco (by on-screen area; with no area, by bitmap size).
  const findMap = () => {
    let best = null, area = -1;
    for (const c of document.querySelectorAll('.palco > canvas')) {
      const r = c.getBoundingClientRect();
      const a = (r.width * r.height) || ((c.width || 0) * (c.height || 0) / 1e6);
      if (a > area) { area = a; best = c; }
    }
    return best;
  };

  // "Extra" elements (version, flags, social networks) outside the path and outside .palco.
  // More expensive sweep: runs every 5 s or when the viewport changes.
  const sweepExtras = (path, stage) => {
    const found = new Set();
    const canHide = (e) => e && e.nodeType === 1 && e !== document.body && e !== document.documentElement &&
      !path.has(e) && !stage.contains(e) && !(root && root.contains(e)) && !e.contains(stage);
    // Climbs to the child of a path element (the whole strip), stopping at the first fixed one (the game's layer).
    const climbStrip = (e) => {
      while (e.parentElement && e.parentElement !== document.body && !path.has(e.parentElement) && !isOutOfFlow(e)) e = e.parentElement;
      return e;
    };
    const add = (e) => { if (canHide(e)) found.add(e); };

    for (const x of document.querySelectorAll(SEL_SOCIAL)) {
      if (!canHide(x)) continue;
      if (x.tagName === 'A') { const h = x.getAttribute('href') || ''; if (!RE_SOCIAL.test(h) && !/discord|instagram|twitter/i.test(h)) continue; }
      add(climbStrip(x));
    }
    for (const x of document.querySelectorAll(SEL_FLAG)) { if (canHide(x)) add(climbStrip(x)); }

    const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let n = w.nextNode(); n; n = w.nextNode()) {
      const t = n.nodeValue;
      if (!t || t.length > 200) continue;
      const p = n.parentElement;
      if (!p || FREE_TAGS.has(p.tagName) || !canHide(p)) continue;
      if (RE_FLAG.test(t)) { add(climbStrip(p)); continue; }
      if (t.length <= 24 && /\d/.test(t) && RE_VERSION.test((p.textContent || '').trim())) {
        // Only the version badge (and the wrappers that contain just it).
        let e = p;
        const s = (p.textContent || '').trim();
        while (e.parentElement && canHide(e.parentElement) && (e.parentElement.textContent || '').trim() === s) e = e.parentElement;
        add(e);
      }
    }
    return [...found];
  };

  const mark = (list, attr, before) => {
    const next = new Set(list);
    for (const e of before) { if (!next.has(e)) { try { e.removeAttribute(attr); } catch (x) { } } }
    for (const e of list) { try { if (!e.hasAttribute(attr)) e.setAttribute(attr, ''); } catch (x) { } }
  };
  const sameList = (a, b) => a.length === b.length && a.every((x, i) => x === b[i]);

  const undoLayout = () => {
    try {
      for (const t of lyTimers) clearTimeout(t);
      lyTimers = [];
      const had = lyActive || !!lyStyle;
      if (!had && !lyHidden.length && !document.documentElement.hasAttribute('data-afkit-grid')) return;   // nothing to undo (cheap in tick)
      document.querySelectorAll('[data-afkit-gw-hidden]').forEach((e) => e.removeAttribute('data-afkit-gw-hidden'));
      document.querySelectorAll('[data-afkit-gw-path]').forEach((e) => e.removeAttribute('data-afkit-gw-path'));
      document.documentElement.removeAttribute('data-afkit-grid');
      if (lyStyle) lyStyle.remove();
      lyStyle = null; lyActive = false; lyViewport = ''; lyHidden = []; lyPath = []; lyExtras = [];
      if (had) fireResize();
    } catch (e) { }
  };

  // Called every 1 s by tick(). Cheap: only re-marks/re-fires resize if something changed.
  const adjustLayout = () => {
    try {
      if (!inGridImmersive()) { if (lyActive) undoLayout(); return; }
      const map = findMap();
      if (!map) return;
      const stage = map.parentElement;
      if (!lyStyle) { lyStyle = el('style'); lyStyle.id = 'afkit-gw-layout'; lyStyle.textContent = CSS_LAYOUT; }
      if (!lyStyle.isConnected) (document.head || document.documentElement).appendChild(lyStyle);
      if (!document.documentElement.hasAttribute('data-afkit-grid')) document.documentElement.setAttribute('data-afkit-grid', '');

      // Path: .palco → ... → child of <body> (the canvas and .palco itself follow the game's/.palco CSS).
      const pathArr = [];
      for (let a = stage; a && a !== document.body && a !== document.documentElement; a = a.parentElement) pathArr.push(a);
      const path = new Set(pathArr);
      path.add(map); path.add(document.body); path.add(document.documentElement);

      // Siblings of each ancestor.
      const hidden = [];
      for (const a of pathArr) {
        const p = a.parentElement;
        if (!p) continue;
        for (const s of p.children) {
          if (s === a || path.has(s) || s.id === 'afkit-gw-root' || FREE_TAGS.has(s.tagName)) continue;
          if (s.hasAttribute('data-afkit-gw-hidden') && lyHidden.includes(s)) { hidden.push(s); continue; }
          if (isOutOfFlow(s)) continue;
          hidden.push(s);
        }
      }

      const vp = `${window.innerWidth}x${window.innerHeight}`;
      if (vp !== lyViewport || !lyActive || lyCycle++ % 5 === 0 || lyExtras.some((e) => !e.isConnected)) {
        lyExtras = sweepExtras(path, stage);
      }
      const all = [...new Set(hidden.concat(lyExtras))];

      const pathChanged = !sameList(pathArr, lyPath);
      const hiddenChanged = !sameList(all, lyHidden);
      if (pathChanged) { mark(pathArr, 'data-afkit-gw-path', lyPath); lyPath = pathArr; }
      if (hiddenChanged) { mark(all, 'data-afkit-gw-hidden', lyHidden); lyHidden = all; }
      const vpChanged = vp !== lyViewport;
      lyViewport = vp;
      if (!lyActive || pathChanged || hiddenChanged || vpChanged) { lyActive = true; staggeredResize(); }
    } catch (e) { }
  };

  const startLayout = () => { lyCycle = 0; adjustLayout(); };
  const endLayout = () => undoLayout();

  // ------------------------------------------------------------ Economy Mode
  // The cell header's button asks for it ('afkit-eco:1/0'). We use the game's own buttons (the hunt/boss-arena shortcut turns it
  // on, "Ligar a cena" turns it off) so the game runs its whole switch; the state is reported back whenever it changes
  // (also when the user changes it inside the page).
  const isShown = (s) => { const e = $(s); return !!e && !e.classList.contains('hidden'); };
  const setEconomy = (on) => {
    try {
      if (on === ecoActive()) return;
      if (on) {
        const sel = isShown('#hunt-eco') || !$('#arena-eco') ? '#hunt-eco' : '#arena-eco';
        if (!$(sel)) return;
        ss.set(KEY_ECO_SET, '1');
        click(sel);
      } else {
        ss.del(KEY_ECO_SET);
        click('#eco-sair');
      }
      syncEco();
    } catch (e) { }
  };
  // Immersive off while Economy Mode is on (its panel would be hidden by the immersive CSS), back on when it goes off.
  const syncEco = () => {
    try {
      const e = ecoActive();
      if (e === lastEco) return;
      lastEco = e;
      post({ type: 'eco', on: e });
      if (applied === true) {
        if (e) setImmersive(false);
        else if (ss.get(KEY_NO_IMMERSIVE) !== '1') setImmersive(true);
        checkMode();
      }
    } catch (err) { }
  };

  // ------------------------------------------------------------ cycle (grid on or battle window on)
  const tick = () => {
    try {
      cycle++;
      if (root && !root.isConnected && document.body) document.body.appendChild(root);
      if (styleEl && !styleEl.isConnected) (document.head || document.documentElement).appendChild(styleEl);
      syncEco();
      const live = checkMode();
      if (live) keepSession();
      updateSummary(live, false);
      if (hover) showClone();
      if (cycle % 2 === 1) updateCaptures(live, false);   // every 2 s (the game writes in the same tab: no storage event)
      adjustLayout();                                      // grid cell: only the battle, filling everything
    } catch (e) { }
  };

  // Collection: 1 s timer + observer of <html>'s class (immersive turning on/off).
  const startCollecting = () => {
    if (timer) return;
    try {
      needCaps = true;
      fbMs = null;
      cycle = 0;
      timer = setInterval(tick, 1000);
      observer = new MutationObserver(() => {
        try { syncEco(); if (checkMode()) keepSession(); } catch (e) { }
      });
      observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
    } catch (e) { }
  };

  const stopCollecting = () => {
    try { if (timer) clearInterval(timer); } catch (e) { }
    timer = null;
    cancelCaps();
    try { if (observer) observer.disconnect(); } catch (e) { }
    observer = null;
    if (noted) restore(noted);
    noted = null;
  };

  // Turns collection on/off according to grid/battle window.
  const updateCollecting = () => {
    try {
      if (!ready) return;
      if (collectWanted()) startCollecting(); else stopCollecting();
    } catch (e) { }
  };

  // Re-evaluates right away whether the panels may stay open (e.g. 'afkit-vis:1' → closes before showing on screen).
  const reevaluatePanels = () => {
    try { if (ready && checkMode()) keepSession(); } catch (e) { }
  };

  // Forced sending of the current state to the battle window (on receiving 'afkit-info:1').
  const sendNow = () => {
    try {
      if (!ready || !infoOn) return;
      infoPending = false;
      const live = checkMode();
      if (live) keepSession();
      updateSummary(live, true);
      updateCaptures(live, true);
    } catch (e) { }
  };

  const enter = () => {
    applied = true;
    try {
      if (ss.get(KEY_NO_IMMERSIVE) !== '1' && !ecoActive()) setImmersive(true);
      lastEco = ecoActive();
      startLayout();
      needCaps = true;
      updateCollecting();
      tick();
    } catch (e) { }
  };

  const leave = () => {
    applied = false;
    ss.del(KEY_NO_IMMERSIVE);
    // Economy Mode turned on from the cell's button was for the grid only, so it goes off with it.
    if (ss.get(KEY_ECO_SET) === '1') { ss.del(KEY_ECO_SET); if (ecoActive()) click('#eco-sair'); }
    lastEco = ecoActive(); post({ type: 'eco', on: lastEco });
    // Before turning immersive off, so nothing flashes on screen: gives the panels back if they can no longer stay
    // open (with the battle window on and the tab hidden they stay).
    try { reevaluatePanels(); } catch (e) { }
    removeOverlay();
    endLayout();
    lastClone = null;
    updateCollecting();                      // stops the timers, unless the battle window still wants the data
    setImmersive(false);
  };

  const apply = () => {
    try {
      if (!ready) return;
      if (wanted === true && applied !== true) enter();
      else if (wanted === false && applied !== false) leave();
      updateCollecting();
      if (infoPending) sendNow();
    } catch (e) { }
  };

  // The user left immersive through the game's button: respect it until leaving the grid. Runs on capture, before the
  // game turns immersive off, so the panels go back to normal without showing on screen.
  try {
    document.addEventListener('click', (e) => {
      try {
        if (applied !== true) return;
        const t = e.target;
        if (!(t instanceof Element) || !t.closest('#imersivo-sair')) return;
        ss.set(KEY_NO_IMMERSIVE, '1');
        if (noted) { cancelCaps(); restore(noted); noted = null; }
      } catch (err) { }
    }, true);
  } catch (e) { }

  // ------------------------------------------------------------ messages from AFKit
  const announceReady = () => { post({ type: 'ready' }); lastEco = ecoActive(); post({ type: 'eco', on: lastEco }); };
  try {
    const wv = window.chrome && window.chrome.webview;
    if (wv) {
      wv.addEventListener('message', (e) => {
        try {
          const d = e && e.data;
          if (d === 'afkit-grid:1') { wanted = true; apply(); }
          else if (d === 'afkit-grid:0') { wanted = false; apply(); }
          else if (d === 'afkit-grid-ping') { if (ready) announceReady(); }
          else if (d === 'afkit-eco:1') { if (ready) setEconomy(true); }
          else if (d === 'afkit-eco:0') { if (ready) setEconomy(false); }
          else if (d === 'afkit-info:1') {
            // Always sends the current state right away (forced), even if it was already on.
            infoOn = true; infoPending = true;
            updateCollecting();
            sendNow();
          } else if (d === 'afkit-info:0') {
            if (infoOn) {
              infoOn = false; infoPending = false;
              reevaluatePanels();              // gives the panels back right away (if not in grid + immersive)
              updateCollecting();
            }
          } else if (d === 'afkit-vis:1') {
            if (!visible) { visible = true; reevaluatePanels(); }   // right away: no panel over the map
          } else if (d === 'afkit-vis:0') {
            if (visible) { visible = false; reevaluatePanels(); }
          }
        } catch (err) { }
      });
    }
  } catch (e) { }

  // ------------------------------------------------------------ wait for the game to be ready
  // Plain polling (no requestAnimationFrame: the tab may not be drawing).
  const waitForGame = () => {
    let t = null;
    const check = () => {
      try {
        if (ready) return;
        const n = $(SEL_SESSION_BTN) && $(SEL_CAP_BTN) ? readNick() : null;
        if (!n) return;
        nick = n;
        ready = true;
        if (t) clearInterval(t);
        announceReady();
        apply();
      } catch (e) { }
    };
    t = setInterval(check, 500);
    check();
  };
  try {
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', waitForGame, { once: true });
    else waitForGame();
  } catch (e) { }
})();
