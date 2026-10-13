// AFKit V2 — task bar reader (injected by AFKit into every page; only acts on pokeidle.io).
// It only READS what the game already shows/receives. It does not click, send anything to the server or change the game.
//  - Your Pokémon: the "Em campo" (in play) panel (#p-ativo): name, level, HP bar and the animated sprite canvas.
//  - Enemy: server messages over the WebSocket — "campo" (target + mobs: n, nv, hp, mhp, sh, lt) and "batalha".
// Sends to AFKit through window.chrome.webview.postMessage:
//  {afkit:'tb', type:'state', me:{name, lv, hp, mhp, xp, xpMax, shiny, key} | null, foe:{name, lv, hp, mhp, shiny, key} | null}
//  {afkit:'tb', type:'sprite', key, frames:[dataURL, ...], ms}   (Pokémon: "out:<sheet>:<direction>", move: "fx:<TYPE>")
//  {afkit:'tb', type:'hit', fx:'fx:<TYPE>', damage}               (your Pokémon hit the current enemy)
// The names of the game's own messages, DOM ids/classes and modules (campo, batalha, #p-ativo, sprites.mjs…) stay in
// Portuguese because they belong to the game.
(() => {
  'use strict';
  if (window.top !== window) return;                       // main page only, not iframes
  if (!/(^|\.)pokeidle\.io$/i.test(location.hostname)) return;
  if (window.__afkitTaskbarProbe) return;
  window.__afkitTaskbarProbe = true;

  const post = (msg) => {
    try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage(Object.assign({ afkit: 'tb' }, msg)); }
    catch (e) { /* AFKit closing the tab */ }
  };

  // ------------------------------------------------------------ enemy (WebSocket)
  const mobs = new Map();   // slot -> {n, nv, hp, mhp, sh, lt}
  let target = null;        // slot of the enemy your Pokémon is attacking
  let lastField = 0;        // when the last "campo" message arrived
  let onHit = () => { };    // defined further down (move effect)

  const onServerMessage = (text) => {
    if (typeof text !== 'string' || text.charCodeAt(0) !== 123 /* { */) return;
    if (text.indexOf('"t":"campo"') < 0 && text.indexOf('"t":"batalha"') < 0) return; // cheap: ignore the rest
    let m;
    try { m = JSON.parse(text); } catch (e) { return; }
    if (m.t === 'campo') {
      lastField = Date.now();
      target = (m.alvo === undefined || m.alvo === null) ? null : m.alvo;
      if (Array.isArray(m.mobs)) {
        for (const x of m.mobs) {
          if (!x || x.s === undefined) continue;
          mobs.set(x.s, { n: x.n, nv: x.nv, hp: x.hp, mhp: x.mhp, sh: x.sh, lt: x.lt });
        }
        if (mobs.size > 300) { const old = [...mobs.keys()].slice(0, mobs.size - 300); old.forEach((k) => mobs.delete(k)); }
      }
    } else if (m.t === 'batalha' && Array.isArray(m.ev)) {
      for (const ev of m.ev) {
        if (!ev) continue;
        if (ev.k === 'ataque' && ev.por === 'jogador') {
          if (mobs.has(ev.slot) && typeof ev.hpAlvo === 'number') mobs.get(ev.slot).hp = ev.hpAlvo;
          try { onHit(ev); } catch (e) { }
        } else if (ev.k === 'morte' && ev.quem === 'selvagem' && mobs.has(ev.slot)) {
          mobs.get(ev.slot).hp = 0;
        }
      }
    }
  };

  // Wraps the constructor BEFORE the game opens the connection (AFKit injects this script at the start of the page).
  const NativeWS = window.WebSocket;
  if (NativeWS) {
    window.WebSocket = new Proxy(NativeWS, {
      construct(ctorTarget, args, newTarget) {
        const ws = Reflect.construct(ctorTarget, args, newTarget);
        try { ws.addEventListener('message', (ev) => onServerMessage(ev.data)); } catch (e) { }
        return ws;
      }
    });
  }

  // ------------------------------------------------------------ your Pokémon (the "Em campo" panel)
  const digits = (s) => { const d = String(s || '').replace(/[^\d]/g, ''); return d ? parseInt(d, 10) : null; };

  const readMe = () => {
    const panel = document.getElementById('p-ativo');
    if (!panel) return null;
    const nameEl = panel.querySelector('.ativo-nome');
    if (!nameEl) return null;
    const first = nameEl.childNodes[0];
    let name = (first && first.nodeType === 3 ? first.textContent : nameEl.textContent).trim();
    // Shiny: the game marks it with ✨ in the name (as in the announcements) and/or with a "shiny" class on the panel.
    const shiny = name.includes('✨') || !!panel.querySelector('[class*="shiny" i]');
    name = name.replace(/✨/g, '').trim();
    if (!name) return null;
    const lvEl = nameEl.querySelector('.pl-lv');
    let hp = null, mhp = null;
    const hpTxt = panel.querySelector('.barra.hp span');
    if (hpTxt) {
      const parts = hpTxt.textContent.split('/');
      if (parts.length === 2) { hp = digits(parts[0]); mhp = digits(parts[1]); }
    }
    // The Pokémon's experience (only inside the panel; the trainer's bar is another one): "175.640 / 348.700 xp".
    let xp = null, xpMax = null;
    const xpTxt = panel.querySelector('.barra.xp span');
    if (xpTxt) {
      const parts = xpTxt.textContent.split('/');
      if (parts.length === 2) { xp = digits(parts[0]); xpMax = digits(parts[1]); }
    }
    return { name, shiny, lv: lvEl ? digits(lvEl.textContent) : null, hp, mhp, xp, xpMax };
  };

  // ------------------------------------------------------------ sprites (drawn straight from the game's sheet)
  // PokeIdle's own sprite module (/sprites.mjs) loads each Pokémon's sheet ("outfit"):
  // 4 directions (1 north, 2 east, 3 south, 4 west) with the movement frames. We draw each frame on our own
  // canvas and send the images to AFKit. (A canvas outside the page does not animate — that is why we do not use
  // spriteAnimado, and the "Em campo" panel draws your Pokémon standing still on purpose.)
  let modPromise = null;
  const getMod = () => (modPromise = modPromise || import('/sprites.mjs'));

  const sentSprites = new Set();
  const pending = new Set();

  const sendSprite = async (key, render) => {
    if (!key || sentSprites.has(key) || pending.has(key)) return;
    pending.add(key);
    try {
      const r = await render();
      if (r && r.frames && r.frames.length) { sentSprites.add(key); post({ type: 'sprite', key, frames: r.frames, ms: r.ms }); }
    } catch (e) { /* tries again on the next change */ }
    finally { pending.delete(key); }
  };

  // All the frames of one direction, in a square with the feet at the bottom center
  // (the square's size is the same for all, so an Onix is still bigger than a Pikachu).
  const renderOutfit = async (id, dir) => {
    const m = await getMod();
    const sprite = await m.carregarOutfit(id);
    if (!sprite) return null;
    const sourceFrames = m.quadrosDe(sprite, dir) || [];
    if (!sourceFrames.length) return null;
    const size = Math.max(96, ...sourceFrames.map((q) => Math.max(q.w || 0, q.h || 0)));
    const frames = sourceFrames.slice(0, 16).map((q) => {
      const c = document.createElement('canvas');
      c.width = size; c.height = size;
      const ctx = c.getContext('2d');
      m.desenharQuadro(ctx, sprite, q, size / 2, size, 1);
      return c.toDataURL('image/png');
    });
    return { frames, ms: 220 }; // the game switches frames every 220 ms
  };

  // Your Pokémon: finds the sheet by name in the game's index (shiny version, if there is one and it is shiny).
  const outfitIds = new Map();
  const findOutfitId = async (name, shiny) => {
    const cacheKey = (shiny ? 'S:' : 'N:') + name;
    if (outfitIds.has(cacheKey)) return outfitIds.get(cacheKey);
    const m = await getMod();
    const idx = await m.carregarIndice();
    const list = Object.values(idx || {});
    const pick = (wanted, kinds) => {
      const hits = list.filter((o) => o && typeof o.name === 'string' && o.name.toLowerCase() === wanted && kinds.includes(o.kind));
      return (hits.find((o) => o.gender === 'male') || hits[0] || null);
    };
    const lower = name.toLowerCase();
    let o = shiny ? pick('shiny ' + lower, ['shiny', 'pokemon']) : null;
    if (!o) o = pick(lower, ['pokemon']);
    const id = o ? o.id : null;
    outfitIds.set(cacheKey, id);
    return id;
  };

  // Move effect by type (DARK, ICE...): vertical strip, frame i at (0, i * height).
  const fxSent = new Set();
  const sendEffect = (moveType) => {
    const t = String(moveType || '').toUpperCase();
    if (!t || fxSent.has(t)) return 'fx:' + t;
    fxSent.add(t);
    sendSprite('fx:' + t, async () => {
      const m = await getMod();
      if (m.carregarEfeitos) await m.carregarEfeitos();
      const e = await m.efeitoDoTipo(t);
      if (!e || !e.img) return null;
      const img = e.img;
      const fw = e.frameW || e.w || e.largura || img.naturalWidth || img.width;
      const fh = e.frameH || e.h || e.altura || fw;
      if (!fw || !fh) return null;
      const n = Math.max(1, Math.min(40, e.frames || e.quadros || Math.floor((img.naturalHeight || img.height) / fh)));
      const ms = e.ms || e.msQuadro || (e.duracao ? Math.round(e.duracao / n) : 60);
      const frames = [];
      for (let i = 0; i < n; i++) {
        const c = document.createElement('canvas');
        c.width = fw; c.height = fh;
        const ctx = c.getContext('2d');
        ctx.imageSmoothingEnabled = false;
        ctx.drawImage(img, 0, i * fh, fw, fh, 0, 0, fw, fh);
        frames.push(c.toDataURL('image/png'));
      }
      return { frames, ms: Math.max(20, Math.min(400, ms)) };
    }).finally(() => { if (!sentSprites.has('fx:' + t)) fxSent.delete(t); });
    return 'fx:' + t;
  };

  // Every hit of your Pokémon on the current enemy: AFKit plays the effect over it.
  onHit = (ev) => {
    if (ev.slot !== target && !ev.aoe) return;
    const fx = sendEffect(ev.tipo);
    post({ type: 'hit', fx, damage: typeof ev.dano === 'number' ? ev.dano : null });
  };

  // ------------------------------------------------------------ sending the state
  let lastJson = '';
  let meKey = null, meKeyFor = null;

  const tick = () => {
    const me = readMe();
    let foe = null;
    if (target !== null && Date.now() - lastField < 10000) {
      const x = mobs.get(target);
      if (x && x.n) foe = { name: x.n, lv: x.nv ?? null, hp: x.hp ?? null, mhp: x.mhp ?? null, shiny: !!x.sh, key: x.lt ? 'out:' + x.lt + ':3' : null, lt: x.lt };
    }

    // Your Pokémon: resolves the sheet by name (once per Pokémon) and sends the back-view frames (like the GBA).
    if (me) {
      const forKey = (me.shiny ? 'S:' : 'N:') + me.name;
      if (meKeyFor !== forKey) {
        meKeyFor = forKey; meKey = null;
        findOutfitId(me.name, me.shiny).then((id) => { if (meKeyFor === forKey && id) meKey = 'out:' + id + ':1'; }).catch(() => { });
      }
      if (meKey) { const id = meKey.split(':')[1]; sendSprite(meKey, () => renderOutfit(id, 1)); }
    }
    // Enemy: the "lt" code is already its sheet; front view (facing south, like the GBA).
    if (foe && foe.lt) {
      const lt = foe.lt, nm = foe.name, sh = foe.shiny;
      sendSprite(foe.key, async () => {
        if (sh) { const sid = await findOutfitId(nm, true); if (sid && sid !== lt) { const r = await renderOutfit(sid, 3); if (r) return r; } }
        return renderOutfit(lt, 3);
      });
    }
    if (foe) delete foe.lt;

    const meOut = me ? { name: me.name, lv: me.lv, hp: me.hp, mhp: me.mhp, xp: me.xp, xpMax: me.xpMax, shiny: me.shiny, key: meKey } : null;
    const json = JSON.stringify({ meOut, foe });
    if (json !== lastJson) { lastJson = json; post({ type: 'state', me: meOut, foe }); }
  };

  // AFKit asking to resend everything (e.g. the battle window was opened after the page loaded).
  try {
    window.chrome.webview.addEventListener('message', (e) => {
      if (e && e.data === 'afkit-tb-resync') { lastJson = ''; sentSprites.clear(); fxSent.clear(); }
    });
  } catch (e) { }

  setInterval(tick, 700);
})();
