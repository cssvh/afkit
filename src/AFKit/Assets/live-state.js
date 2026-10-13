// AFKit — live stream state (Twitch and Kick). Injected by AFKit into every page; only acts on twitch.tv and kick.com.
//
// Goal: the live reopens the way it was when the session was closed — player sound (muted or not, and
// volume) and quality. Nothing is fixed: the script only reads the current state and hands back the last saved one.
//
// Protocol with AFKit:
//  Page → AFKit: {afkit:'live', type:'hello'} at the start of each page (AFKit answers with the saved state).
//                {afkit:'live', type:'state', site, muted, volume, quality} when the state changes (every 2 s at most).
//  AFKit → Page: JSON {afkitLive:{muted, volume, quality}} (state to reapply). Before that there may already be
//                window.__afkitLive in the same format, injected before the page was born (session reopening).
//
// Where each site keeps things (from the diagnosis):
//  - Kick: quality in sessionStorage 'stream_quality' (e.g. "160"), which is lost on close → AFKit writes it again
//    before the player loads. Mute/volume are not stored: we apply them to the video (#video-player).
//  - Twitch: quality in localStorage 'video-quality' and sound in 'video-muted'/'volume' (we only rewrite keys
//    that already exist, in the format the site itself uses); mute/volume are also applied to the video.
(() => {
  'use strict';
  let site = null;
  try {
    if (window.top !== window) return;
    const h = location.hostname.toLowerCase();
    if (/(^|\.)twitch\.tv$/.test(h)) site = 'twitch';
    else if (/(^|\.)kick\.com$/.test(h)) site = 'kick';
    else return;
    if (window.__afkitLiveScript) return;
    window.__afkitLiveScript = true;
  } catch (e) { return; }

  const post = (msg) => {
    try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage(Object.assign({ afkit: 'live' }, msg)); }
    catch (e) { }
  };
  const lsGet = (st, k) => { try { return st.getItem(k); } catch (e) { return null; } };
  const lsSet = (st, k, v) => { try { st.setItem(k, v); } catch (e) { } };

  let wanted = null;          // state to reapply: {muted, volume, quality}
  let soundApplied = false;   // mute/volume already applied to the video on this page
  let startedAt = Date.now();

  const isState = (s) => s && typeof s === 'object';

  // ------------------------------------------------------------ quality and sound stored by the site
  const readQuality = () => site === 'kick'
    ? lsGet(sessionStorage, 'stream_quality')
    : lsGet(localStorage, 'video-quality');

  const writeStorage = (s) => {
    if (!isState(s)) return;
    try {
      if (typeof s.quality === 'string' && s.quality) {
        if (site === 'kick') lsSet(sessionStorage, 'stream_quality', s.quality);
        else lsSet(localStorage, 'video-quality', s.quality);
      }
      if (site === 'twitch') {
        // Only touches keys the site already created, keeping its format.
        const vm = lsGet(localStorage, 'video-muted');
        if (vm && typeof s.muted === 'boolean') {
          try {
            const o = JSON.parse(vm);
            if (o && typeof o === 'object' && 'default' in o) { o.default = s.muted; lsSet(localStorage, 'video-muted', JSON.stringify(o)); }
          } catch (e) { }
        }
        const vol = lsGet(localStorage, 'volume');
        if (vol !== null && Number.isFinite(parseFloat(vol)) && Number.isFinite(s.volume)) {
          lsSet(localStorage, 'volume', String(s.volume));
        }
      }
    } catch (e) { }
  };

  // ------------------------------------------------------------ the live's video
  const mainVideo = () => {
    try {
      if (site === 'kick') {
        const v = document.getElementById('video-player');
        if (v instanceof HTMLVideoElement) return v;
      } else {
        const v = document.querySelector('[data-a-target="video-player"] video');
        if (v instanceof HTMLVideoElement) return v;
      }
      // Fallback: the biggest visible video that is playing.
      let best = null, area = 0;
      for (const v of document.querySelectorAll('video')) {
        const a = v.offsetWidth * v.offsetHeight;
        if (!v.paused && a > area) { best = v; area = a; }
      }
      return best;
    } catch (e) { return null; }
  };

  // ------------------------------------------------------------ reapply mute/volume
  // The player usually starts muted and may touch the sound right after loading: we insist until the state
  // stays the way we want for 3 checks in a row (or give up after 45 s).
  let streak = 0;
  const applySound = () => {
    if (soundApplied || !isState(wanted) || typeof wanted.muted !== 'boolean') { soundApplied = true; return; }
    const v = mainVideo();
    if (!v || v.readyState < 2) return;
    try {
      if (Number.isFinite(wanted.volume) && Math.abs(v.volume - wanted.volume) > 0.01) v.volume = Math.min(1, Math.max(0, wanted.volume));
      if (v.muted !== wanted.muted) { v.muted = wanted.muted; streak = 0; return; }
      if (++streak >= 3) soundApplied = true;
    } catch (e) { }
  };

  // ------------------------------------------------------------ report the state to AFKit
  let lastJson = '';
  const report = () => {
    // While the saved state is still being reapplied, do not report (otherwise it would save the autoplay "muted").
    if (!soundApplied && Date.now() - startedAt < 45000) return;
    const v = mainVideo();
    if (!v || v.readyState < 2) return;
    const state = {
      type: 'state', site,
      muted: !!v.muted,
      volume: Math.round(v.volume * 100) / 100,
      quality: readQuality(),
    };
    const json = JSON.stringify(state);
    if (json === lastJson) return;
    lastJson = json;
    wanted = { muted: state.muted, volume: state.volume, quality: state.quality }; // applies to the next channel
    post(state);
  };

  // ------------------------------------------------------------ messages from AFKit
  try {
    if (isState(window.__afkitLive)) { wanted = window.__afkitLive; writeStorage(wanted); }
  } catch (e) { }
  if (!wanted) soundApplied = true; // nothing to reapply until AFKit answers

  try {
    const wv = window.chrome && window.chrome.webview;
    if (wv) {
      wv.addEventListener('message', (e) => {
        try {
          const d = e && e.data;
          if (!d || typeof d !== 'object' || !isState(d.afkitLive)) return;
          wanted = d.afkitLive;
          writeStorage(wanted);
          soundApplied = false; streak = 0; startedAt = Date.now();
        } catch (err) { }
      });
    }
  } catch (e) { }
  post({ type: 'hello' });

  // Single-page sites (switching channel without reloading): each new channel restarts the reapplication.
  let url = location.href;
  setInterval(() => {
    try {
      if (location.href !== url) { url = location.href; if (wanted) { soundApplied = false; streak = 0; startedAt = Date.now(); } }
      applySound();
    } catch (e) { }
  }, 500);
  setInterval(() => { try { report(); } catch (e) { } }, 2000);
})();
