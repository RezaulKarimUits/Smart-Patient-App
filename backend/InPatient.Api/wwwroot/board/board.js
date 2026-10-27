// Live bed board: grid per ward, colour-coded by status, NEWS2 + Watch/Critical flag, realtime via SignalR (polling fallback).
(async () => {
  const { api, toast, esc, dhakaTime } = IP;
  const app = document.getElementById('app');
  const modal = document.getElementById('modal');
  const modalBody = document.getElementById('modalBody');
  const me = await IP.requireLogin(app, ['Receptionist', 'HospitalAdmin', 'Nurse', 'Consultant'], 'Live bed board');
  const canFrontDesk = ['Receptionist', 'HospitalAdmin'].includes(me.role);

  let board = null;
  let wardFilter = 'all';
  let connected = false;

  app.innerHTML = `
    <div class="topbar">
      <div class="brand"><div class="brand-mark">IP</div><div><div id="hname">Bed board</div><div class="faint" style="font-size:12px;font-weight:500">${esc(me.name)} · ${esc(me.role)}</div></div></div>
      <span class="live" id="live"><i></i><span>connecting…</span></span>
      <div class="grow"></div>
      <div class="legend">
        ${['Free', 'Reserved', 'Occupied', 'Cleaning', 'Blocked'].map((s) => `<span class="pill ${s}">${s}</span>`).join('')}
        <span class="pill Watch">Watch</span><span class="pill Critical">Critical</span>
      </div>
      <button class="btn ghost sm" id="logout">Sign out</button>
    </div>
    <div class="page">
      <div class="wardtabs" id="tabs"></div>
      <div id="wards"><div class="empty">Loading beds…</div></div>
    </div>`;
  document.getElementById('logout').onclick = IP.logout;

  async function load() {
    try {
      board = await api('/api/board');
      document.getElementById('hname').textContent = board.hospitalName;
      render();
    } catch (e) {
      document.getElementById('wards').innerHTML = `<div class="empty">Could not load the board: ${esc(e.message)} <br><br><button class="btn" onclick="location.reload()">Retry</button></div>`;
    }
  }

  function render() {
    const tabs = document.getElementById('tabs');
    tabs.innerHTML = [`<button class="wardtab ${wardFilter === 'all' ? 'active' : ''}" data-w="all">All wards</button>`]
      .concat(board.wards.map((w) => `<button class="wardtab ${wardFilter == w.id ? 'active' : ''}" data-w="${w.id}">${esc(w.name)} · ${w.occupancy.free} free</button>`)).join('');
    tabs.querySelectorAll('.wardtab').forEach((b) => b.onclick = () => { wardFilter = b.dataset.w; render(); });

    const wards = board.wards.filter((w) => wardFilter === 'all' || wardFilter == w.id);
    document.getElementById('wards').innerHTML = wards.map((w) => `
      <section class="ward">
        <div class="ward-head">
          <h2>${esc(w.name)}</h2>
          <div class="stats">
            <span class="pill Free">${w.occupancy.free} free</span><span class="pill Reserved">${w.occupancy.reserved} reserved</span>
            <span class="pill Occupied">${w.occupancy.occupied} occupied</span><span class="pill Cleaning">${w.occupancy.cleaning} cleaning</span>
            <span class="pill Blocked">${w.occupancy.blocked} blocked</span>
            <span class="faint">${w.occupancy.occupancyPct}% occupancy</span>
          </div>
          <div class="grow"></div>
          ${canFrontDesk ? `<a class="btn secondary sm" href="/print/beds.html?wardId=${w.id}" target="_blank">Print bed QR labels</a>` : ''}
        </div>
        <div class="beds">${w.beds.map(tileHtml).join('') || '<div class="empty">No beds</div>'}</div>
      </section>`).join('');
    document.querySelectorAll('.bed').forEach((el) => el.onclick = () => openBed(Number(el.dataset.id)));
    updateLive();
  }

  function tileHtml(b) {
    const o = b.occupant;
    const flag = o ? `flag-${o.flag}` : '';
    let body;
    if (o) {
      body = `<div class="name" title="${esc(o.patientName)}">${esc(o.patientName)}</div>
        <div class="meta">${o.ageYears}y ${esc(o.sex)} · ${esc(o.consultantName)}</div>
        <div class="row" style="margin-top:6px;gap:6px">
          ${o.awaitingScan ? '<span class="pill Reserved">Awaiting scan</span>' : ''}
          ${o.flag !== 'Stable' ? `<span class="pill ${o.flag}">${o.flag}</span>` : ''}
          ${o.activeAlerts ? `<span class="faint" style="font-size:12px">${o.activeAlerts} alert${o.activeAlerts > 1 ? 's' : ''}</span>` : ''}
        </div>`;
    } else {
      body = `<div class="meta" style="margin-top:8px">${esc(b.lastNote || '')}</div><div class="meta">since ${dhakaTime(b.statusChangedAt, true)}</div>`;
    }
    const news = o && o.news2Score != null ? `<span class="news2 ${o.news2Band}" title="NEWS2">${o.news2Score}</span>` : '';
    return `<div class="bed ${b.status} ${flag}" data-id="${b.id}" id="bed-${b.id}">
      <div class="top"><span class="num">${esc(b.bedNumber)}</span>${news || `<span class="pill ${b.status}">${b.status}</span>`}</div>${body}</div>`;
  }

  function findBed(id) { for (const w of board.wards) { const b = w.beds.find((x) => x.id === id); if (b) return { w, b }; } return {}; }

  function applyTile(tile) {
    if (!board) return;
    const ward = board.wards.find((w) => w.id === tile.wardId);
    if (!ward) return;
    const i = ward.beds.findIndex((b) => b.id === tile.id);
    if (i >= 0) ward.beds[i] = tile; else ward.beds.push(tile);
    const statuses = ward.beds.map((b) => b.status);
    const c = (s) => statuses.filter((x) => x === s).length;
    ward.occupancy = { ...ward.occupancy, free: c('Free'), reserved: c('Reserved'), occupied: c('Occupied'), cleaning: c('Cleaning'), blocked: c('Blocked'),
      occupancyPct: Math.round(1000 * (c('Occupied') + c('Reserved')) / statuses.length) / 10 };
    render();
    document.getElementById(`bed-${tile.id}`)?.classList.add('flash');
  }

  // ---------- Bed actions ----------
  function openModal(html) { modalBody.innerHTML = html; modal.classList.add('open'); }
  function closeModal() { modal.classList.remove('open'); }
  modal.addEventListener('click', (e) => { if (e.target === modal) closeModal(); });

  async function openBed(id) {
    const { w, b } = findBed(id);
    if (!b) return;
    const o = b.occupant;
    const actions = [];
    if (canFrontDesk) {
      if (b.status === 'Free') actions.push(['admit', 'Admit patient here', ''], ['reserve', 'Reserve', 'secondary'], ['block', 'Block bed', 'secondary']);
      if (b.status === 'Reserved' && !o) actions.push(['release', 'Release reservation', 'secondary']);
      if (o) actions.push(['wristband', 'Print wristband', ''], ['transfer', 'Transfer to another bed', 'secondary']);
      actions.push(['qr', 'Show bed QR', 'secondary']);
    }
    if (b.status === 'Cleaning' && me.role !== 'Consultant') actions.push(['clean', 'Mark cleaned (Free)', '']);
    if (b.status === 'Blocked' && canFrontDesk) actions.push(['unblock', 'Unblock', '']);

    openModal(`
      <div class="row"><h2>${esc(b.bedNumber)}</h2><span class="pill ${b.status}">${b.status}</span><div class="grow"></div><button class="btn ghost sm" id="x">Close</button></div>
      <p class="muted">${esc(w.name)} · since ${dhakaTime(b.statusChangedAt, true)} ${b.lastNote ? '· ' + esc(b.lastNote) : ''}</p>
      ${o ? `<div class="card" style="box-shadow:none">
        <div class="row"><b style="font-size:17px">${esc(o.patientName)}</b><div class="grow"></div>${o.news2Score != null ? `<span class="news2 ${o.news2Band}">${o.news2Score}</span>` : '<span class="faint">NEWS2 n/a</span>'}</div>
        <div class="muted">${esc(o.mrn)} · ${o.ageYears}y ${esc(o.sex)}</div>
        <div class="muted">${esc(o.diagnosis || '')}</div>
        <div class="muted">Consultant: ${esc(o.consultantName)}</div>
        <div class="row" style="margin-top:8px"><span class="pill ${o.flag}">${o.flag}</span>${o.awaitingScan ? '<span class="pill Reserved">Awaiting nurse scan</span>' : ''}</div>
      </div>` : ''}
      <div class="stack" style="margin-top:14px">${actions.map(([k, label, cls]) => `<button class="btn ${cls}" data-a="${k}">${label}</button>`).join('') || '<p class="faint">No actions for your role.</p>'}</div>`);
    modalBody.querySelector('#x').onclick = closeModal;
    modalBody.querySelectorAll('[data-a]').forEach((btn) => btn.onclick = () => action(btn.dataset.a, b, w));
  }

  async function action(kind, b, w) {
    try {
      if (kind === 'admit') return admitFlow(b, w);
      if (kind === 'wristband') { window.open(`/print/wristband.html?id=${b.occupant.admissionId}`, '_blank'); return; }
      if (kind === 'transfer') return transferFlow(b);
      if (kind === 'qr') {
        const qr = await api(`/api/beds/${b.id}/qr`);
        openModal(`<h2>${esc(qr.label)}</h2><img class="qr" src="data:image/png;base64,${qr.pngBase64}" alt="Bed QR"><p class="faint" style="text-align:center">Scan with the InPatient app</p>
          <div class="row"><div class="grow"></div><button class="btn secondary" id="x">Close</button></div>`);
        modalBody.querySelector('#x').onclick = closeModal;
        return;
      }
      let tile;
      if (kind === 'reserve') tile = await api(`/api/beds/${b.id}/reserve`, { method: 'POST', body: { note: prompt('Reservation note (optional)') || null } });
      if (kind === 'release') tile = await api(`/api/beds/${b.id}/status`, { method: 'POST', body: { status: 'Free' } });
      if (kind === 'clean') tile = await api(`/api/beds/${b.id}/status`, { method: 'POST', body: { status: 'Free', note: 'Cleaned and ready' } });
      if (kind === 'unblock') tile = await api(`/api/beds/${b.id}/status`, { method: 'POST', body: { status: 'Free' } });
      if (kind === 'block') {
        const note = prompt('Reason for blocking this bed');
        if (!note) return;
        tile = await api(`/api/beds/${b.id}/status`, { method: 'POST', body: { status: 'Blocked', note } });
      }
      if (tile) { applyTile(tile); closeModal(); toast(`${tile.bedNumber} is now ${tile.status}`); }
    } catch (e) { toast(e.message, true); }
  }

  async function transferFlow(b) {
    const free = board.wards.flatMap((w) => w.beds.filter((x) => x.status === 'Free').map((x) => ({ ...x, wardName: w.name })));
    openModal(`<h2>Transfer ${esc(b.occupant.patientName)}</h2><p class="muted">From ${esc(b.bedNumber)}. The old bed goes to Cleaning.</p>
      <div class="field"><label>New bed</label><select id="nb">${free.map((x) => `<option value="${x.id}">${esc(x.wardName)} · ${esc(x.bedNumber)}</option>`).join('')}</select></div>
      <div class="field"><label>Reason</label><input id="reason" placeholder="e.g. needs ICU care"></div>
      <div class="row"><div class="grow"></div><button class="btn secondary" id="x">Cancel</button><button class="btn" id="go" ${free.length ? '' : 'disabled'}>Transfer</button></div>`);
    modalBody.querySelector('#x').onclick = closeModal;
    modalBody.querySelector('#go').onclick = async () => {
      try {
        const h = await api(`/api/admissions/${b.occupant.admissionId}/transfer`, { method: 'POST', body: { bedId: Number(modalBody.querySelector('#nb').value), reason: modalBody.querySelector('#reason').value || null } });
        closeModal(); toast(`Moved to ${h.wardName} ${h.bedNumber}`); load();
      } catch (e) { toast(e.message, true); }
    };
  }

  async function admitFlow(bed, ward) {
    let selected = null;
    const consultants = await api(`/api/wards/${ward.id}/consultants`);
    openModal(`
      <div class="row"><h2>Admit to ${esc(bed.bedNumber)}</h2><div class="grow"></div><button class="btn ghost sm" id="x">Cancel</button></div>
      <p class="muted">${esc(ward.name)}</p>
      <div class="field"><label>Find patient (name, MRN or phone)</label><input id="q" placeholder="Start typing…"></div>
      <div id="results"></div>
      <details id="newp"><summary style="cursor:pointer;font-weight:600;margin:8px 0">Register a new patient</summary>
        <div class="field"><label>Full name</label><input id="n_name"></div>
        <div class="grid2">
          <div class="field"><label>Date of birth</label><input id="n_dob" type="date"></div>
          <div class="field"><label>Sex</label><select id="n_sex"><option value="M">Male</option><option value="F">Female</option><option value="O">Other</option></select></div>
          <div class="field"><label>Phone</label><input id="n_phone" inputmode="tel"></div>
          <div class="field"><label>Blood group</label><select id="n_blood"><option value="">Unknown</option>${['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'].map((g) => `<option>${g}</option>`).join('')}</select></div>
        </div>
        <div class="field"><label>Guardian / attendant</label><input id="n_guardian"></div>
        <button class="btn secondary" id="reg">Register patient</button>
      </details>
      <hr style="border:none;border-top:1px solid var(--border);margin:14px 0">
      <div class="field"><label>Consultant</label><select id="cons">${consultants.map((c) => `<option value="${c.id}">${esc(c.name)} — ${esc(c.designation || '')}</option>`).join('')}</select></div>
      <div class="field"><label>Provisional diagnosis</label><input id="dx" placeholder="e.g. Dengue fever"></div>
      <button class="btn" id="admit" style="width:100%" disabled>Admit & reserve ${esc(bed.bedNumber)}</button>`);
    const $ = (s) => modalBody.querySelector(s);
    $('#x').onclick = closeModal;
    const pick = (p) => { selected = p; $('#admit').disabled = false; $('#results').innerHTML = `<div class="result sel"><b>${esc(p.name)}</b> · ${esc(p.mrn)} · ${p.ageYears}y ${esc(p.sex)}</div>`; };
    let timer;
    $('#q').oninput = () => {
      clearTimeout(timer);
      timer = setTimeout(async () => {
        const q = $('#q').value.trim();
        if (q.length < 2) { $('#results').innerHTML = ''; return; }
        const list = await api(`/api/patients?q=${encodeURIComponent(q)}`);
        $('#results').innerHTML = list.length ? list.map((p, i) => `<div class="result" data-i="${i}"><b>${esc(p.name)}</b> · ${esc(p.mrn)} · ${p.ageYears}y ${esc(p.sex)} ${p.activeAdmissionId ? '<span class="pill Occupied">admitted</span>' : ''}</div>`).join('')
          : '<p class="faint">No match — register a new patient below.</p>';
        $('#results').querySelectorAll('.result').forEach((el) => el.onclick = () => {
          const p = list[Number(el.dataset.i)];
          if (p.activeAdmissionId) return toast('Patient already has an active admission', true);
          pick(p);
        });
      }, 250);
    };
    $('#reg').onclick = async () => {
      try {
        const p = await api('/api/patients', { method: 'POST', body: {
          name: $('#n_name').value, dateOfBirth: $('#n_dob').value, sex: $('#n_sex').value, phone: $('#n_phone').value || null,
          guardian: $('#n_guardian').value || null, bloodGroup: $('#n_blood').value || null } });
        $('#newp').open = false; pick(p); toast(`Registered ${p.mrn}`);
      } catch (e) { toast(e.message, true); }
    };
    $('#admit').onclick = async () => {
      try {
        const res = await api('/api/admissions', { method: 'POST', body: { patientId: selected.id, bedId: bed.id, consultantUserId: Number($('#cons').value), provisionalDiagnosis: $('#dx').value || null } });
        closeModal();
        toast(`${res.admission.patientName} admitted — ${bed.bedNumber} reserved`);
        window.open(`/print/wristband.html?id=${res.admission.id}`, '_blank');
        load();
      } catch (e) { toast(e.message, true); }
    };
  }

  // ---------- Realtime ----------
  function updateLive() {
    const el = document.getElementById('live');
    if (!el) return;
    el.className = 'live' + (connected ? ' on' : '');
    el.querySelector('span').textContent = connected ? 'Live' : 'Polling';
  }

  const conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/ward', { accessTokenFactory: () => IP.session()?.token })
    .withAutomaticReconnect()
    .build();
  conn.on('bedChanged', applyTile);
  conn.on('alertRaised', () => load());
  conn.on('admissionUpdated', () => load());
  conn.onreconnecting(() => { connected = false; updateLive(); });
  conn.onreconnected(() => { connected = true; updateLive(); load(); });
  conn.onclose(() => { connected = false; updateLive(); });
  try { await conn.start(); connected = true; } catch { connected = false; }

  await load();
  setInterval(() => { if (!connected) load(); }, 15000); // polling fallback
  setInterval(() => { if (connected) load(); }, 120000); // periodic resync
})();
