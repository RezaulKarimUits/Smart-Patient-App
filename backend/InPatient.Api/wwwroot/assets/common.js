// Shared helpers for the web pages: session in localStorage, fetch wrapper, toasts, login form.
const IP = (() => {
  const KEY = 'inpatient.session';
  const get = () => { try { return JSON.parse(localStorage.getItem(KEY) || 'null'); } catch { return null; } };
  const set = (s) => { try { localStorage.setItem(KEY, JSON.stringify(s)); } catch {} };
  const clear = () => { try { localStorage.removeItem(KEY); } catch {} };

  // A token can also arrive in the URL fragment (#token=...) when the mobile app opens a print page.
  const hash = new URLSearchParams(location.hash.slice(1));
  if (hash.get('token')) {
    set({ token: hash.get('token'), user: null, fromLink: true });
    history.replaceState(null, '', location.pathname + location.search);
  }

  async function api(path, opts = {}) {
    const s = get();
    const headers = { ...(opts.body && !(opts.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}), ...(opts.headers || {}) };
    if (s?.token) headers.Authorization = `Bearer ${s.token}`;
    const res = await fetch(path, { ...opts, headers, body: opts.body && !(opts.body instanceof FormData) ? JSON.stringify(opts.body) : opts.body });
    if (res.status === 401) { clear(); location.reload(); throw new Error('Signed out'); }
    const text = await res.text();
    const data = text ? (() => { try { return JSON.parse(text); } catch { return text; } })() : null;
    if (!res.ok) throw new Error(data?.error || `Request failed (${res.status})`);
    return data;
  }

  function toast(msg, isError = false) {
    const el = document.createElement('div');
    el.className = 'toast' + (isError ? ' error' : '');
    el.textContent = msg;
    document.body.appendChild(el);
    setTimeout(() => el.remove(), 3500);
  }

  const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const dhakaTime = (iso, withDate = false) => iso ? new Date(iso).toLocaleString('en-GB', {
    timeZone: 'Asia/Dhaka', hour: '2-digit', minute: '2-digit', ...(withDate ? { day: '2-digit', month: 'short' } : {})
  }) : '';
  const dhakaDate = (iso) => iso ? new Date(iso).toLocaleDateString('en-GB', { timeZone: 'Asia/Dhaka', day: '2-digit', month: 'short', year: 'numeric' }) : '';

  /** Renders a login card into `root` unless a session exists; resolves with the user. */
  async function requireLogin(root, allowedRoles, title) {
    const s = get();
    if (s?.token) {
      try {
        const me = await api('/api/auth/me');
        if (allowedRoles.includes(me.role)) { set({ ...s, user: me }); return me; }
        toast(`${me.role} cannot use this page`, true);
        clear();
      } catch { clear(); }
    }
    return new Promise((resolve) => {
      root.innerHTML = `
        <div class="login card">
          <div class="brand" style="margin-bottom:16px"><div class="brand-mark">IP</div><div>InPatient<div class="faint" style="font-weight:500;font-size:13px">${esc(title)}</div></div></div>
          <form id="loginForm">
            <div class="field"><label>Phone</label><input name="phone" inputmode="tel" autocomplete="username" placeholder="01800000002" required></div>
            <div class="field"><label>Password</label><input name="password" type="password" autocomplete="current-password" required></div>
            <div class="field"><label>Hospital code (only if asked)</label><input name="hospitalCode" placeholder="optional"></div>
            <button class="btn" style="width:100%">Sign in</button>
            <p class="faint" style="font-size:13px">Demo password for every seeded user: <b>Demo@123</b></p>
          </form>
        </div>`;
      root.querySelector('#loginForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        const f = new FormData(e.target);
        try {
          const res = await fetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ phone: f.get('phone'), password: f.get('password'), hospitalCode: f.get('hospitalCode') || null }) });
          const data = await res.json();
          if (!res.ok) throw new Error(data.error || 'Login failed');
          if (!allowedRoles.includes(data.user.role)) throw new Error(`${data.user.role} cannot use this page`);
          set({ token: data.token, user: data.user });
          resolve(data.user);
        } catch (err) { toast(err.message, true); }
      });
    });
  }

  return { api, toast, esc, dhakaTime, dhakaDate, requireLogin, session: get, logout: () => { clear(); location.reload(); } };
})();
