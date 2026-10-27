import { useState } from 'react';
import { View } from 'react-native';
import { api } from '@/api/client';
import { useAdminBeds, useAdminWards, useQueryClient, useShifts, useStaff } from '@/api/hooks';
import type { Role, Staff } from '@/api/types';
import { BottomSheet } from '@/components/clinical';
import { Button, Card, Chip, EmptyState, Notice, QueryView, Row, Screen, Segmented, Spacer, T, TextField } from '@/components/ui';
import { dhakaDateKey } from '@/lib/format';
import { toastError } from '@/components/Toast';

const ROLES: Role[] = ['Nurse', 'Consultant', 'Receptionist', 'HospitalAdmin', 'Patient'];
const SHIFTS = ['Morning', 'Evening', 'Night'];

export default function StaffScreen() {
  const [tab, setTab] = useState<'staff' | 'shifts'>('staff');
  return (
    <Screen>
      <Segmented value={tab} onChange={setTab} options={[{ key: 'staff', label: 'Staff' }, { key: 'shifts', label: 'Nurse shifts' }]} />
      <Spacer />
      {tab === 'staff' ? <StaffList /> : <Shifts />}
    </Screen>
  );
}

function StaffList() {
  const [role, setRole] = useState<Role>('Nurse');
  const q = useStaff(role);
  const wards = useAdminWards();
  const qc = useQueryClient();
  const [add, setAdd] = useState(false);
  const [form, setForm] = useState({ name: '', phone: '', designation: '', password: '' });
  const [mapFor, setMapFor] = useState<Staff | null>(null);
  const [mapSel, setMapSel] = useState<number[]>([]);
  const refresh = () => void qc.invalidateQueries({ queryKey: ['admin'] });

  const create = async () => {
   
    try { await api.post('/api/admin/staff', { ...form, role, designation: form.designation || null, patientId: null }); setAdd(false); setForm({ name: '', phone: '', designation: '', password: '' }); refresh(); }
    catch (e) { toastError(e); }
  };
  const toggle = async (s: Staff) => {
    try { await api.put(`/api/admin/staff/${s.id}`, { name: s.name, designation: s.designation, isActive: !s.isActive }); refresh(); } catch (e) { toastError(e); }
  };
  const saveMap = async () => {
    if (!mapFor) return;
    try { await api.put(`/api/admin/consultants/${mapFor.id}/wards`, { wardIds: mapSel }); setMapFor(null); refresh(); } catch (e) { toastError(e); }
  };

  return (
    <>
      <Row wrap gap={6} style={{ marginBottom: 10 }}>{ROLES.map((r) => <Chip key={r} label={r} selected={role === r} onPress={() => setRole(r)} />)}</Row>
      {role !== 'Patient' ? <Button title={`Add ${role}`} icon="person-add-outline" onPress={() => { setAdd(true); }} style={{ marginBottom: 10 }} /> : <Notice message="Patient logins are created from the web admin page (/admin), linked to a registered patient." />}
      <QueryView q={q} empty={(l) => l.length === 0 ? <EmptyState icon="people-outline" title={`No ${role.toLowerCase()} users`} /> : null}>
        {(list) => list.map((s) => (
          <Card key={s.id} style={{ marginBottom: 8, opacity: s.isActive ? 1 : 0.5 }}>
            <Row>
              <View style={{ flex: 1 }}>
                <T v="bodyStrong">{s.name}</T>
                <T v="caption" color="muted">{s.phone} · {s.designation ?? s.role}</T>
                {s.role === 'Consultant' ? <T v="caption" color="primary">{wards.data?.filter((w) => s.wardIds.includes(w.id)).map((w) => w.name).join(', ') || 'No wards'}</T> : null}
              </View>
              {s.role === 'Consultant' ? <Button title="Wards" size="sm" variant="secondary" onPress={() => { setMapFor(s); setMapSel(s.wardIds); }} /> : null}
              <Button title={s.isActive ? 'Deactivate' : 'Activate'} size="sm" variant="ghost" onPress={() => void toggle(s)} />
            </Row>
          </Card>
        ))}
      </QueryView>

      <BottomSheet visible={add} onClose={() => setAdd(false)} title={`New ${role}`}>
        <TextField label="Name" value={form.name} onChangeText={(v) => setForm({ ...form, name: v })} />
        <TextField label="Phone (login)" value={form.phone} keyboardType="phone-pad" onChangeText={(v) => setForm({ ...form, phone: v })} />
        <TextField label="Designation" value={form.designation} onChangeText={(v) => setForm({ ...form, designation: v })} />
        <TextField label="Initial password" value={form.password} secureTextEntry onChangeText={(v) => setForm({ ...form, password: v })} />
        <Button title="Create" size="lg" disabled={!form.name || !form.phone || form.password.length < 6} onPress={() => void create()} />
      </BottomSheet>

      <BottomSheet visible={mapFor != null} onClose={() => setMapFor(null)} title={`Wards · ${mapFor?.name ?? ''}`}>
        <T color="muted" style={{ marginBottom: 10 }}>The consultant sees these wards in the priority list and receives their alerts.</T>
        <Row wrap gap={6}>{wards.data?.map((w) => <Chip key={w.id} label={w.name} selected={mapSel.includes(w.id)} onPress={() => setMapSel(mapSel.includes(w.id) ? mapSel.filter((x) => x !== w.id) : [...mapSel, w.id])} />)}</Row>
        <Spacer h={16} />
        <Button title="Save mapping" size="lg" onPress={() => void saveMap()} />
      </BottomSheet>
    </>
  );
}

function Shifts() {
  const wards = useAdminWards();
  const nurses = useStaff('Nurse');
  const qc = useQueryClient();
  const [date, setDate] = useState(dhakaDateKey());
  const [wardId, setWardId] = useState<number | null>(null);
  const ward = wardId ?? wards.data?.[0]?.id ?? null;
  const q = useShifts(date, ward);
  const beds = useAdminBeds(ward);
  const [assign, setAssign] = useState<{ nurseUserId: number | null; shiftType: string; bedIds: number[] } | null>(null);
  const shiftDay = (d: number) => dhakaDateKey(new Date(Date.now() + d * 864e5));

  const save = async () => {
    if (!assign?.nurseUserId || !ward) return;
    try { await api.post('/api/admin/shifts', { nurseUserId: assign.nurseUserId, wardId: ward, shiftDate: date, shiftType: assign.shiftType, bedIds: assign.bedIds }); setAssign(null); void qc.invalidateQueries({ queryKey: ['admin', 'shifts'] }); }
    catch (e) { toastError(e); }
  };
  const remove = async (id: number) => { try { await api.del(`/api/admin/shifts/${id}`); void q.refetch(); } catch (e) { toastError(e); } };

  return (
    <>
      {wards.data ? <Segmented value={String(ward)} onChange={(k) => setWardId(Number(k))} options={wards.data.map((w) => ({ key: String(w.id), label: w.name.replace(' Ward', '') }))} /> : null}
      <Row gap={6} style={{ marginVertical: 8 }}>{[-1, 0, 1].map((d) => <Chip key={d} label={d === 0 ? 'Today' : d < 0 ? 'Yesterday' : 'Tomorrow'} selected={date === shiftDay(d)} onPress={() => setDate(shiftDay(d))} />)}</Row>
      <Button title="Assign nurse" icon="add" onPress={() => { setAssign({ nurseUserId: nurses.data?.[0]?.id ?? null, shiftType: 'Morning', bedIds: [] }); }} style={{ marginBottom: 10 }} />
      <QueryView q={q}>
        {(list) => (
          <>
            {SHIFTS.map((s) => (
              <Card key={s} style={{ marginBottom: 10 }}>
                <T v="heading">{s} <T v="caption" color="faint">{s === 'Morning' ? '08–14' : s === 'Evening' ? '14–20' : '20–08'}</T></T>
                {list.filter((x) => x.shiftType === s).map((x) => (
                  <Row key={x.id} style={{ paddingVertical: 8 }}>
                    <View style={{ flex: 1 }}><T v="bodyStrong">{x.nurseName}</T><T v="caption" color="muted">{x.bedNumbers.join(', ') || 'no beds'}</T></View>
                    <Button title="Remove" size="sm" variant="ghost" onPress={() => void remove(x.id)} />
                  </Row>
                ))}
                {list.every((x) => x.shiftType !== s) ? <T color="faint">Nobody assigned</T> : null}
              </Card>
            ))}
          </>
        )}
      </QueryView>

      <BottomSheet visible={assign != null} onClose={() => setAssign(null)} title={`Assign · ${date}`}>
        <T v="label" color="muted" style={{ marginBottom: 6 }}>Nurse</T>
        <Row wrap gap={6} style={{ marginBottom: 12 }}>{nurses.data?.filter((n) => n.isActive).map((n) => <Chip key={n.id} label={n.name} selected={assign?.nurseUserId === n.id} onPress={() => assign && setAssign({ ...assign, nurseUserId: n.id })} />)}</Row>
        <T v="label" color="muted" style={{ marginBottom: 6 }}>Shift</T>
        <Row gap={6} style={{ marginBottom: 12 }}>{SHIFTS.map((s) => <Chip key={s} label={s} selected={assign?.shiftType === s} onPress={() => assign && setAssign({ ...assign, shiftType: s })} />)}</Row>
        <T v="label" color="muted" style={{ marginBottom: 6 }}>Beds</T>
        <Row wrap gap={6} style={{ marginBottom: 16 }}>{beds.data?.map((b) => <Chip key={b.id} label={b.bedNumber} selected={assign?.bedIds.includes(b.id)}
          onPress={() => assign && setAssign({ ...assign, bedIds: assign.bedIds.includes(b.id) ? assign.bedIds.filter((x) => x !== b.id) : [...assign.bedIds, b.id] })} />)}</Row>
        <Button title="Save shift" size="lg" disabled={!assign?.nurseUserId} onPress={() => void save()} />
      </BottomSheet>
    </>
  );
}
