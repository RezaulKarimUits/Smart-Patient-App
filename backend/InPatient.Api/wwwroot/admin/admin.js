// Web admin: SuperAdmin (hospitals + hospital admins) and HospitalAdmin (wards/beds, staff, shifts, ward history, occupancy).
(async () => {
  const { api, toast, esc, dhakaTime } = IP;
  const app = document.getElementById('app');
  const modal = document.getElementById('modal');
  const modalBody = document.getElementById('modalBody');
  const me = await IP.requireLogin(app, ['SuperAdmin', 'HospitalAdmin'], 'Administration');
  const openModal = (html) => { modalBody.innerHTML = html; modal.classList.add('open'); };
  const closeModal = () => modal.classList.remove('open');
  modal.addEventListener('click', (e) => { if (e.target === modal) closeModal(); });
  const todayDhaka = () => new Date(Date.now() + 6 * 3600e3).toISOString().slice(0, 10);

  app.innerHTML = `
    <div class="topbar">
      <div class="brand"><div class="brand-mark">IP</div><div>${me.role === 'SuperAdmin' ? 'InPatient platform' : esc(me.hospitalName)}
        <div class="faint" style="font-size:12px;font-weight:500">${esc(me.name)} · ${esc(me.role)}</div></div></div>
      <div class="grow"></div>
      ${me.role === 'HospitalAdmin' ? '<a class="btn secondary sm" href="/board/">Bed board</a>' : ''}
      <button class="btn ghost sm" id="logout">Sign out</button>
    </div>
    <div class="page"><div class="tabs" id="tabs"></div><div id="view"></div></div>`;
  document.getElementById('logout').onclick = IP.logout;
  const view = document.getElementById('view');

  const tabs = me.role === 'SuperAdmin'
    ? [['hospitals', 'Hospitals', superHospitals]]
    : [['occupancy', 'Occupancy', occupancy], ['wards', 'Wards & beds', wards], ['staff', 'Staff', staff], ['shifts', 'Nurse shifts', shifts], ['history', 'Ward history', history]];
  let current = tabs[0][0];
  function renderTabs() {
    document.getElementById('tabs').innerHTML = tabs.map(([k, l]) => `<button class="tab ${k === current ? 'active' : ''}" data-k="${k}">${l}</button>`).join('');
    document.querySelectorAll('.tab').forEach((b) => b.onclick = () => { current = b.dataset.k; renderTabs(); show(); });
  }
  async function show() {
    view.innerHTML = '<div class="empty">Loading…</div>';
    try { await tabs.find((t) => t[0] === current)[2](); } catch (e) { view.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
  }
  renderTabs(); show();

  // ---------------- SuperAdmin ----------------
  async function superHospitals() {
    const list = await api('/api/super/hospitals');
    view.innerHTML = `
      <div class="row" style="margin-bottom:14px"><h2>Hospitals</h2><div class="grow"></div><button class="btn" id="add">Add hospital</button></div>
      <div class="grid">${list.map((h) => `
        <div class="card">
          <div class="row"><b style="font-size:17px">${esc(h.name)}</b><div class="grow"></div><span class="pill ${h.isActive ? 'Free' : 'Blocked'}">${h.isActive ? 'Active' : 'Inactive'}</span></div>
          <div class="muted">${esc(h.code)} · ${esc(h.address || '')}</div>
          <div class="row" style="margin:10px 0;gap:16px"><span>${h.wards} wards</span><span>${h.beds} beds</span><span>${h.users} users</span><span>${h.activeAdmissions} in-patients</span></div>
          <div class="row"><button class="btn secondary sm" data-admins="${h.id}">Admins</button><button class="btn ghost sm" data-toggle="${h.id}">${h.isActive ? 'Deactivate' : 'Activate'}</button></div>
        </div>`).join('')}</div>`;
    view.querySelector('#add').onclick = () => {
      openModal(`<h2>New hospital</h2>
        <div class="field"><label>Name</label><input id="n"></div><div class="grid2"><div class="field"><label>Code</label><input id="c" placeholder="e.g. SYL"></div>
        <div class="field"><label>Phone</label><input id="p"></div></div><div class="field"><label>Address</label><input id="a"></div>
        <div class="row"><div class="grow"></div><button class="btn secondary" id="x">Cancel</button><button class="btn" id="s">Create</button></div>`);
      modalBody.querySelector('#x').onclick = closeModal;
      modalBody.querySelector('#s').onclick = async () => {
        try { await api('/api/super/hospitals', { method: 'POST', body: { name: v('#n'), code: v('#c'), phone: v('#p'), address: v('#a') } }); closeModal(); toast('Hospital created'); show(); }
        catch (e) { toast(e.message, true); }
      };
    };
    view.querySelectorAll('[data-toggle]').forEach((b) => b.onclick = async () => {
      const h = list.find((x) => x.id == b.dataset.toggle);
      try { await api(`/api/super/hospitals/${h.id}`, { method: 'PUT', body: { name: h.name, code: h.code, address: h.address, phone: h.phone, isActive: !h.isActive } }); show(); }
      catch (e) { toast(e.message, true); }
    });
    view.querySelectorAll('[data-admins]').forEach((b) => b.onclick = async () => {
      const id = b.dataset.admins;
      const admins = await api(`/api/super/hospitals/${id}/admins`);
      openModal(`<h2>Hospital admins</h2>
        <table><tr><th>Name</th><th>Phone</th></tr>${admins.map((a) => `<tr><td>${esc(a.name)}</td><td>${esc(a.phone)}</td></tr>`).join('') || '<tr><td colspan=2 class="faint">None yet</td></tr>'}</table>
        <h3 style="margin-top:16px">Add admin</h3>
        <div class="field"><label>Name</label><input id="n"></div><div class="grid2"><div class="field"><label>Phone</label><input id="p"></div><div class="field"><label>Password</label><input id="w" type="password"></div></div>
        <div class="row"><div class="grow"></div><button class="btn secondary" id="x">Close</button><button class="btn" id="s">Add admin</button></div>`);
      modalBody.querySelector('#x').onclick = closeModal;
      modalBody.querySelector('#s').onclick = async () => {
        try { await api(`/api/super/hospitals/${id}/admins`, { method: 'POST', body: { name: v('#n'), phone: v('#p'), password: v('#w') } }); closeModal(); toast('Admin created'); show(); }
        catch (e) { toast(e.message, true); }
      };
    });
  }
  const v = (s) => modalBody.querySelector(s).value;

  // ---------------- HospitalAdmin ----------------
  async function occupancy() {
    const wardList = await api('/api/wards');
    const tot = wardList.reduce((a, w) => { for (const k of ['totalBeds', 'free', 'occupied', 'reserved', 'cleaning', 'blocked']) a[k] = (a[k] || 0) + w.occupancy[k]; a.watch = (a.watch || 0) + w.watchCount; a.crit = (a.crit || 0) + w.criticalCount; return a; }, {});
    view.innerHTML = `
      <div class="kpis">
        ${kpi(tot.totalBeds, 'Beds')}${kpi(tot.occupied, 'Occupied')}${kpi(tot.free, 'Free')}${kpi(tot.reserved, 'Reserved')}${kpi(tot.cleaning, 'Cleaning')}
        ${kpi(tot.blocked, 'Blocked')}${kpi(tot.watch, 'Watch patients')}${kpi(tot.crit, 'Critical patients')}
      </div>
      <div class="grid">${wardList.map((w) => `
        <div class="card"><div class="row"><b>${esc(w.name)}</b><div class="grow"></div><span class="faint">${w.occupancy.occupancyPct}%</span></div>
          <div class="bar" style="margin:10px 0"><i style="width:${w.occupancy.occupancyPct}%"></i></div>
          <div class="stats row wrap" style="gap:6px"><span class="pill Free">${w.occupancy.free} free</span><span class="pill Occupied">${w.occupancy.occupied} occ</span>
          <span class="pill Reserved">${w.occupancy.reserved} res</span><span class="pill Cleaning">${w.occupancy.cleaning} clean</span><span class="pill Blocked">${w.occupancy.blocked} blk</span></div>
          <div class="muted" style="margin-top:8px;font-size:13px">${w.consultants.map((c) => esc(c.name)).join(', ') || 'No consultant mapped'}</div>
          ${w.criticalCount || w.watchCount ? `<div class="row" style="margin-top:8px">${w.criticalCount ? `<span class="pill Critical">${w.criticalCount} critical</span>` : ''}${w.watchCount ? `<span class="pill Watch">${w.watchCount} watch</span>` : ''}</div>` : ''}
        </div>`).join('')}</div>`;
  }
  const kpi = (val, label) => `<div class="card kpi"><div class="v">${val ?? 0}</div><div class="l">${label}</div></div>`;

  async function wards() {
    const list = await api('/api/admin/wards');
    view.innerHTML = `<div class="row" style="margin-bottom:14px"><h2>Wards & beds</h2><div class="grow"></div><button class="btn" id="add">Add ward</button></div>
      <div class="card scroll"><table><tr><th>Ward</th><th>Code</th><th>Type</th><th>Floor</th><th>Beds</th><th>Occupancy</th><th></th></tr>
      ${list.map((w) => `<tr><td><b>${esc(w.name)}</b>${w.isActive ? '' : ' <span class="pill Blocked">inactive</span>'}</td><td>${esc(w.code)}</td><td>${esc(w.type)}</td><td>${esc(w.floor || '')}</td>
        <td>${w.beds}</td><td>${w.occupancy.occupancyPct}%</td>
        <td class="row" style="justify-content:flex-end"><button class="btn secondary sm" data-beds="${w.id}">Add beds</button><a class="btn ghost sm" target="_blank" href="/print/beds.html?wardId=${w.id}">QR labels</a><button class="btn ghost sm" data-edit="${w.id}">Edit</button></td></tr>`).join('')}
      </table></div>`;
    const form = (w = {}) => {
      openModal(`<h2>${w.id ? 'Edit ward' : 'New ward'}</h2>
        <div class="field"><label>Name</label><input id="n" value="${esc(w.name || '')}"></div>
        <div class="grid2"><div class="field"><label>Code (bed prefix)</label><input id="c" value="${esc(w.code || '')}"></div>
        <div class="field"><label>Type</label><select id="t">${['Medicine', 'Surgery', 'Paediatrics', 'ICU', 'Gynae', 'Other'].map((t) => `<option ${t === w.type ? 'selected' : ''}>${t}</option>`).join('')}</select></div></div>
        <div class="grid2"><div class="field"><label>Floor</label><input id="f" value="${esc(w.floor || '')}"></div>
        ${w.id ? `<div class="field"><label>Status</label><select id="a"><option value="1" ${w.isActive ? 'selected' : ''}>Active</option><option value="0" ${w.isActive ? '' : 'selected'}>Inactive</option></select></div>` : ''}</div>
        <div class="row"><div class="grow"></div><button class="btn secondary" id="x">Cancel</button><button class="btn" id="s">Save</button></div>`);
      modalBody.querySelector('#x').onclick = closeModal;
      modalBody.querySelector('#s').onclick = async () => {
        const body = { name: v('#n'), code: v('#c'), type: v('#t'), floor: v('#f'), isActive: w.id ? v('#a') === '1' : null };
        try { await api(w.id ? `/api/admin/wards/${w.id}` : '/api/admin/wards', { method: w.id ? 'PUT' : 'POST', body }); closeModal(); toast('Saved'); show(); }
        catch (e) { toast(e.message, true); }
      };
    };
    view.querySelector('#add').onclick = () => form();
    view.querySelectorAll('[data-edit]').forEach((b) => b.onclick = () => form(list.find((w) => w.id == b.dataset.edit)));
    view.querySelectorAll('[data-beds]').forEach((b) => b.onclick = () => {
      openModal(`<h2>Add beds</h2><div class="field"><label>How many</label><input id="c" type="number" min="1" max="50" value="2"></div>
        <div class="row"><div class="grow"></div><button class="btn secondary" id="x">Cancel</button><button class="btn" id="s">Add</button></div>`);
      modalBody.querySelector('#x').onclick = closeModal;
      modalBody.querySelector('#s').onclick = async () => {
        try { const r = await api(`/api/admin/wards/${b.dataset.beds}/beds`, { method: 'POST', body: { count: Number(v('#c')) } }); closeModal(); toast(`Added ${r.map((x) => x.bedNumber).join(', ')}`); show(); }
        catch (e) { toast(e.message, true); }
      };
    });
  }

  async function staff() {
    const [list, wardList] = await Promise.all([api('/api/admin/staff'), api('/api/admin/wards')]);
    const roles = ['HospitalAdmin', 'Receptionist', 'Nurse', 'Consultant', 'Patient'];
    view.innerHTML = `<div class="row" style="margin-bottom:14px"><h2>Staff</h2><div class="grow"></div><button class="btn" id="add">Add user</button></div>
      <div class="card scroll"><table><tr><th>Name</th><th>Phone</th><th>Role</th><th>Designation</th><th>Wards</th><th></th></tr>
      ${list.map((u) => `<tr style="${u.isActive ? '' : 'opacity:.5'}"><td><b>${esc(u.name)}</b></td><td>${esc(u.phone)}</td><td>${esc(u.role)}</td><td>${esc(u.designation || '')}</td>
        <td>${u.role === 'Consultant' ? wardList.filter((w) => u.wardIds.includes(w.id)).map((w) => esc(w.code)).join(', ') || '—' : ''}</td>
        <td class="row" style="justify-content:flex-end">
          ${u.role === 'Consultant' ? `<button class="btn secondary sm" data-map="${u.id}">Wards</button>` : ''}
          <button class="btn ghost sm" data-pw="${u.id}">Reset password</button>
          <button class="btn ghost sm" data-act="${u.id}">${u.isActive ? 'Deactivate' : 'Activate'}</button></td></tr>`).join('')}
      </table></div>`;
    view.querySelector('#add').onclick = async () => {
      openModal(`<h2>New user</h2>
        <div class="field"><label>Name</label><input id="n"></div>
        <div class="grid2"><div class="field"><label>Phone (login)</label><input id="p"></div><div class="field"><label>Role</label><select id="r">${roles.map((r) => `<option>${r}</option>`).join('')}</select></div></div>
        <div class="grid2"><div class="field"><label>Designation</label><input id="d"></div><div class="field"><label>Initial password</label><input id="w" type="password"></div></div>
        <div class="field" id="pf" style="display:none"><label>Patient (search name/MRN)</label><input id="pq"><select id="pid" style="margin-top:6px"></select></div>
        <div class="row"><div class="grow"></div><button class="btn secondary" id="x">Cancel</button><button class="btn" id="s">Create</button></div>`);
      const $ = (s) => modalBody.querySelector(s);
      $('#x').onclick = closeModal;
      $('#r').onchange = () => { $('#pf').style.display = $('#r').value === 'Patient' ? 'block' : 'none'; };
      $('#pq').oninput = async () => {
        const res = await api(`/api/patients?q=${encodeURIComponent($('#pq').value)}`);
        $('#pid').innerHTML = res.map((p) => `<option value="${p.id}">${esc(p.name)} · ${esc(p.mrn)}</option>`).join('');
      };
      $('#s').onclick = async () => {
        try {
          await api('/api/admin/staff', { method: 'POST', body: { name: $('#n').value, phone: $('#p').value, role: $('#r').value, designation: $('#d').value || null,
            password: $('#w').value, patientId: $('#r').value === 'Patient' ? Number($('#pid').value) : null } });
          closeModal(); toast('User created'); show();
        } catch (e) { toast(e.message, true); }
      };
    };
    view.querySelectorAll('[data-act]').forEach((b) => b.onclick = async () => {
      const u = list.find((x) => x.id == b.dataset.act);
      try { await api(`/api/admin/staff/${u.id}`, { method: 'PUT', body: { name: u.name, designation: u.designation, isActive: !u.isActive } }); show(); }
      catch (e) { toast(e.message, true); }
    });
    view.querySelectorAll('[data-pw]').forEach((b) => b.onclick = async () => {
      const pw = prompt('New password (min 6 characters)');
      if (!pw) return;
      try { await api(`/api/admin/staff/${b.dataset.pw}/reset-password`, { method: 'POST', body: { password: pw } }); toast('Password reset'); }
      catch (e) { toast(e.message, true); }
    });
    view.querySelectorAll('[data-map]').forEach((b) => b.onclick = () => {
      const u = list.find((x) => x.id == b.dataset.map);
      const sel = new Set(u.wardIds);
      openModal(`<h2>Wards for ${esc(u.name)}</h2><p class="muted">The consultant sees these wards' patients in the priority list and receives their alerts.</p>
        <div class="chips" id="ch">${wardList.map((w) => `<span class="chip ${sel.has(w.id) ? 'on' : ''}" data-w="${w.id}">${esc(w.name)}</span>`).join('')}</div>
        <div class="row" style="margin-top:16px"><div class="grow"></div><button class="btn secondary" id="x">Cancel</button><button class="btn" id="s">Save</button></div>`);
      modalBody.querySelectorAll('.chip').forEach((c) => c.onclick = () => { const id = Number(c.dataset.w); sel.has(id) ? sel.delete(id) : sel.add(id); c.classList.toggle('on'); });
      modalBody.querySelector('#x').onclick = closeModal;
      modalBody.querySelector('#s').onclick = async () => {
        try { await api(`/api/admin/consultants/${u.id}/wards`, { method: 'PUT', body: { wardIds: [...sel] } }); closeModal(); toast('Mapping saved'); show(); }
        catch (e) { toast(e.message, true); }
      };
    });
  }

  let shiftDate = todayDhaka();
  let shiftWard = null;
  async function shifts() {
    const [wardList, nurses] = await Promise.all([api('/api/admin/wards'), api('/api/admin/staff?role=Nurse')]);
    shiftWard = shiftWard || wardList[0]?.id;
    const list = await api(`/api/admin/shifts?date=${shiftDate}&wardId=${shiftWard}`);
    const bedsOfWard = await api(`/api/admin/wards/${shiftWard}/beds`);
    view.innerHTML = `
      <div class="row wrap" style="margin-bottom:14px"><h2>Nurse shifts</h2><div class="grow"></div>
        <input type="date" id="d" value="${shiftDate}" style="width:auto"><select id="w" style="width:auto">${wardList.map((w) => `<option value="${w.id}" ${w.id == shiftWard ? 'selected' : ''}>${esc(w.name)}</option>`).join('')}</select>
        <button class="btn" id="add">Assign shift</button></div>
      ${['Morning', 'Evening', 'Night'].map((s) => `<div class="card" style="margin-bottom:12px"><h3>${s} <span class="faint" style="font-weight:500;font-size:13px">${{ Morning: '08:00–14:00', Evening: '14:00–20:00', Night: '20:00–08:00' }[s]}</span></h3>
        ${list.filter((x) => x.shiftType === s).map((x) => `<div class="row" style="padding:8px 0;border-bottom:1px solid var(--border)"><b>${esc(x.nurseName)}</b>
          <span class="muted grow">${x.bedNumbers.join(', ') || 'no beds'}</span><button class="btn ghost sm" data-del="${x.id}">Remove</button></div>`).join('') || '<p class="faint">Nobody assigned</p>'}</div>`).join('')}`;
    view.querySelector('#d').onchange = (e) => { shiftDate = e.target.value; show(); };
    view.querySelector('#w').onchange = (e) => { shiftWard = Number(e.target.value); show(); };
    view.querySelectorAll('[data-del]').forEach((b) => b.onclick = async () => { try { await api(`/api/admin/shifts/${b.dataset.del}`, { method: 'DELETE' }); show(); } catch (e) { toast(e.message, true); } });
    view.querySelector('#add').onclick = () => {
      const sel = new Set();
      openModal(`<h2>Assign shift · ${shiftDate}</h2>
        <div class="grid2"><div class="field"><label>Nurse</label><select id="n">${nurses.filter((n) => n.isActive).map((n) => `<option value="${n.id}">${esc(n.name)}</option>`).join('')}</select></div>
        <div class="field"><label>Shift</label><select id="s"><option>Morning</option><option>Evening</option><option>Night</option></select></div></div>
        <label>Beds</label><div class="chips">${bedsOfWard.map((b) => `<span class="chip" data-b="${b.id}">${esc(b.bedNumber)}</span>`).join('')}</div>
        <div class="row" style="margin-top:16px"><div class="grow"></div><button class="btn secondary" id="x">Cancel</button><button class="btn" id="go">Save</button></div>`);
      modalBody.querySelectorAll('.chip').forEach((c) => c.onclick = () => { const id = Number(c.dataset.b); sel.has(id) ? sel.delete(id) : sel.add(id); c.classList.toggle('on'); });
      modalBody.querySelector('#x').onclick = closeModal;
      modalBody.querySelector('#go').onclick = async () => {
        try {
          await api('/api/admin/shifts', { method: 'POST', body: { nurseUserId: Number(v('#n')), wardId: shiftWard, shiftDate, shiftType: v('#s'), bedIds: [...sel] } });
          closeModal(); toast('Shift saved'); show();
        } catch (e) { toast(e.message, true); }
      };
    };
  }

  let histWard = null;
  let histFrom = null;
  let histTo = null;
  async function history() {
    const wardList = await api('/api/wards');
    histWard = histWard || wardList[0]?.id;
    histTo = histTo || todayDhaka();
    histFrom = histFrom || new Date(new Date(histTo).getTime() - 6 * 864e5).toISOString().slice(0, 10);
    const h = await api(`/api/wards/${histWard}/history?from=${histFrom}&to=${histTo}`);
    const max = Math.max(1, ...h.days.map((d) => Math.max(d.admissionsIn + d.transfersIn, d.discharges + d.transfersOut, d.occupiedBedDays)));
    const bw = 44, ch = 160;
    view.innerHTML = `
      <div class="row wrap" style="margin-bottom:14px"><h2>Ward history</h2><div class="grow"></div>
        <select id="w" style="width:auto">${wardList.map((w) => `<option value="${w.id}" ${w.id == histWard ? 'selected' : ''}>${esc(w.name)}</option>`).join('')}</select>
        <input type="date" id="f" value="${histFrom}" style="width:auto"><input type="date" id="t" value="${histTo}" style="width:auto"></div>
      <div class="kpis">${kpi(h.totals.admissionsIn, 'Admissions')}${kpi(h.totals.transfersIn, 'Transfers in')}${kpi(h.totals.transfersOut, 'Transfers out')}
        ${kpi(h.totals.discharges, 'Discharges')}${kpi(h.totals.occupiedBedDays, 'Occupied bed-days')}${kpi(h.totals.avgLengthOfStayDays ?? '—', 'Avg length of stay (days)')}
        ${kpi(h.totals.avgOccupancyPct + '%', 'Avg occupancy')}${kpi(h.occupancyNow.occupancyPct + '%', 'Occupancy now')}</div>
      <div class="card scroll" style="margin-bottom:14px">
        <h3 style="margin-bottom:8px">Daily movement</h3>
        <svg width="${Math.max(400, h.days.length * (bw + 16) + 40)}" height="${ch + 40}" role="img" aria-label="Daily admissions, discharges and occupied bed-days">
          ${h.days.map((d, i) => {
            const x = 30 + i * (bw + 16);
            const a = (d.admissionsIn + d.transfersIn) / max * ch, o = (d.discharges + d.transfersOut) / max * ch, b = d.occupiedBedDays / max * ch;
            return `<rect x="${x}" y="${ch - b}" width="${bw}" height="${b}" fill="var(--primary-soft)" rx="4"><title>${d.occupiedBedDays} bed-days</title></rect>
              <rect x="${x + 6}" y="${ch - a}" width="14" height="${a}" fill="var(--bed-free)" rx="3"><title>${d.admissionsIn} admitted, ${d.transfersIn} transferred in</title></rect>
              <rect x="${x + 24}" y="${ch - o}" width="14" height="${o}" fill="var(--bed-cleaning)" rx="3"><title>${d.discharges} discharged, ${d.transfersOut} transferred out</title></rect>
              <text x="${x + bw / 2}" y="${ch + 16}" text-anchor="middle">${d.day.slice(5)}</text>`;
          }).join('')}
        </svg>
        <div class="row" style="gap:14px;font-size:13px"><span><span class="pill Free">■</span> In (admitted + transfers)</span><span><span class="pill Cleaning">■</span> Out (discharges + transfers)</span><span><span class="pill Occupied">■</span> Occupied bed-days</span></div>
      </div>
      <div class="card scroll" style="margin-bottom:14px"><table><tr><th>Day</th><th>Admitted</th><th>Transfer in</th><th>Transfer out</th><th>Discharged</th><th>Bed-days</th><th>Avg LOS</th></tr>
        ${h.days.slice().reverse().map((d) => `<tr><td>${d.day}</td><td>${d.admissionsIn}</td><td>${d.transfersIn}</td><td>${d.transfersOut}</td><td>${d.discharges}</td><td>${d.occupiedBedDays}</td><td>${d.avgLengthOfStayDays ?? ''}</td></tr>`).join('')}</table></div>
      <div class="card"><h3>Timeline</h3>${h.events.map((e) => `<div class="ev"><time>${dhakaTime(e.at, true)}</time><span class="pill ${evClass(e.eventType)}">${e.eventType}</span>
        <span><b>${esc(e.bedNumber)}</b> ${esc(e.patientName || '')} <span class="faint">${esc(e.note || '')}${e.byUserName ? ' · ' + esc(e.byUserName) : ''}</span></span></div>`).join('') || '<p class="faint">No events in this range</p>'}</div>`;
    view.querySelector('#w').onchange = (e) => { histWard = Number(e.target.value); show(); };
    view.querySelector('#f').onchange = (e) => { histFrom = e.target.value; show(); };
    view.querySelector('#t').onchange = (e) => { histTo = e.target.value; show(); };
  }
  const evClass = (t) => ({ Reserved: 'Reserved', Admitted: 'Occupied', TransferredIn: 'Occupied', TransferredOut: 'Cleaning', Discharged: 'Cleaning', Cleaned: 'Free', Blocked: 'Blocked', Unblocked: 'Free', Released: 'Free' }[t] || '');
})();
