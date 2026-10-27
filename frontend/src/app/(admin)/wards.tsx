import { useState } from 'react';
import { Linking } from 'react-native';
import { API_URL, api, getToken } from '@/api/client';
import { useAdminWards, useQueryClient } from '@/api/hooks';
import type { AdminWard } from '@/api/types';
import { BottomSheet } from '@/components/clinical';
import { Button, Card, Chip, QueryView, Row, Screen, T, TextField } from '@/components/ui';
import { toastError } from '@/components/Toast';

const TYPES = ['Medicine', 'Surgery', 'Paediatrics', 'ICU', 'Gynae', 'Other'];

export default function AdminWards() {
  const q = useAdminWards();
  const qc = useQueryClient();
  const [edit, setEdit] = useState<Partial<AdminWard> | null>(null);
  const [bedsFor, setBedsFor] = useState<AdminWard | null>(null);
  const [count, setCount] = useState('2');
  const [busy, setBusy] = useState(false);

  const refresh = () => { void qc.invalidateQueries({ queryKey: ['admin'] }); void qc.invalidateQueries({ queryKey: ['wards'] }); void qc.invalidateQueries({ queryKey: ['board'] }); };

  const save = async () => {
    if (!edit) return;
    setBusy(true);
    try {
      const body = { name: edit.name, code: edit.code, type: edit.type ?? 'Other', floor: edit.floor ?? null, isActive: edit.id ? edit.isActive : null };
      await (edit.id ? api.put(`/api/admin/wards/${edit.id}`, body) : api.post('/api/admin/wards', body));
      setEdit(null); refresh();
    } catch (e) { toastError(e); } finally { setBusy(false); }
  };
  const addBeds = async () => {
    if (!bedsFor) return;
    setBusy(true);
    try { await api.post(`/api/admin/wards/${bedsFor.id}/beds`, { count: Number(count) || 1 }); setBedsFor(null); refresh(); }
    catch (e) { toastError(e); } finally { setBusy(false); }
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <Button title="Add ward" icon="add" onPress={() => { setEdit({ type: 'Medicine', isActive: true }); }} style={{ marginBottom: 12 }} />
      <QueryView q={q}>
        {(wards) => wards.map((w, i) => (
          <Card key={w.id} delay={i * 30} style={{ marginBottom: 10, opacity: w.isActive ? 1 : 0.55 }}>
            <Row><T v="heading" style={{ flex: 1 }}>{w.name}</T><T v="label" color="muted">{w.code} · {w.type}</T></Row>
            <T v="caption" color="muted">{w.floor ?? ''} · {w.beds} beds · {w.occupancy.occupancyPct}% occupied{w.isActive ? '' : ' · inactive'}</T>
            <Row gap={8} style={{ marginTop: 10 }} wrap>
              <Button title="Add beds" size="sm" variant="secondary" icon="bed-outline" onPress={() => { setBedsFor(w); }} />
              <Button title="Edit" size="sm" variant="secondary" onPress={() => { setEdit(w); }} />
              <Button title="QR labels" size="sm" variant="ghost" icon="print-outline" onPress={() => void Linking.openURL(`${API_URL}/print/beds.html?wardId=${w.id}#token=${getToken() ?? ''}`)} />
            </Row>
          </Card>
        ))}
      </QueryView>

      <BottomSheet visible={edit != null} onClose={() => setEdit(null)} title={edit?.id ? 'Edit ward' : 'New ward'}>
        <TextField label="Name" value={edit?.name ?? ''} onChangeText={(s) => setEdit({ ...edit, name: s })} />
        <TextField label="Code (bed prefix, e.g. M)" value={edit?.code ?? ''} autoCapitalize="characters" onChangeText={(s) => setEdit({ ...edit, code: s })} />
        <TextField label="Floor" value={edit?.floor ?? ''} onChangeText={(s) => setEdit({ ...edit, floor: s })} />
        <Row wrap gap={6} style={{ marginBottom: 12 }}>{TYPES.map((x) => <Chip key={x} label={x} selected={edit?.type === x} onPress={() => setEdit({ ...edit, type: x })} />)}</Row>
        {edit?.id ? <Row gap={6} style={{ marginBottom: 12 }}><Chip label="Active" selected={edit.isActive} onPress={() => setEdit({ ...edit, isActive: true })} /><Chip label="Inactive" selected={!edit.isActive} onPress={() => setEdit({ ...edit, isActive: false })} /></Row> : null}
        <Button title="Save" size="lg" loading={busy} disabled={!edit?.name || !edit?.code} onPress={() => void save()} />
      </BottomSheet>

      <BottomSheet visible={bedsFor != null} onClose={() => setBedsFor(null)} title={`Add beds · ${bedsFor?.name ?? ''}`}>
        <TextField label="How many" keyboardType="number-pad" value={count} onChangeText={setCount} />
        <T v="caption" color="muted" style={{ marginBottom: 12 }}>Numbered {bedsFor?.code}-NN after the last bed. Each gets a random QR token.</T>
        <Button title="Add beds" size="lg" loading={busy} onPress={() => void addBeds()} />
      </BottomSheet>
    </Screen>
  );
}
