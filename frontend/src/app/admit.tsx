import { useQuery } from '@tanstack/react-query';
import * as Haptics from '@/lib/haptics';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { View } from 'react-native';
import { api } from '@/api/client';
import { useBoard, useInvalidateAdmission } from '@/api/hooks';
import type { AdmitResponse, Patient, StaffBrief } from '@/api/types';
import { Button, Card, Chip, Notice, Row, Screen, Segmented, Spacer, StatusPill, T, TextField } from '@/components/ui';
import { useTheme } from '@/theme';
import { toast, toastError } from '@/components/Toast';

type Step = 'patient' | 'bed' | 'details';

/** Admit: find or register patient → choose ward + free bed (becomes Reserved) → consultant → wristband. */
export default function Admit() {
  const t = useTheme();
  const params = useLocalSearchParams<{ bedId?: string; patientId?: string; patientName?: string }>();
  const board = useBoard();
  const invalidate = useInvalidateAdmission();
  const [step, setStep] = useState<Step>('patient');
  const [patient, setPatient] = useState<Patient | null>(null);
  const [bedId, setBedId] = useState<number | null>(params.bedId ? Number(params.bedId) : null);
  const [wardId, setWardId] = useState<number | null>(null);
  const [consultant, setConsultant] = useState<number | null>(null);
  const [dx, setDx] = useState('');
  const [busy, setBusy] = useState(false);

  // patient search / register
  const [q, setQ] = useState('');
  const [reg, setReg] = useState(false);
  const [form, setForm] = useState({ name: '', dob: '', sex: 'M', phone: '', guardian: '', blood: '' });
  const search = useQuery<Patient[]>({ queryKey: ['patients', q], queryFn: () => api.get(`/api/patients?q=${encodeURIComponent(q)}`), enabled: q.trim().length >= 2 });

  useEffect(() => {
    if (params.patientId && !patient) api.get<Patient[]>(`/api/patients?q=${encodeURIComponent(params.patientName ?? '')}`).then((l) => { const p = l.find((x) => x.id === Number(params.patientId)); if (p) { setPatient(p); setStep('bed'); } }).catch(() => undefined);
  }, [params.patientId, params.patientName, patient]);

  const selectedBed = useMemo(() => board.data?.wards.flatMap((w) => w.beds).find((b) => b.id === bedId) ?? null, [board.data, bedId]);
  useEffect(() => { if (selectedBed && !wardId) setWardId(selectedBed.wardId); }, [selectedBed, wardId]);
  const ward = board.data?.wards.find((w) => w.id === wardId) ?? board.data?.wards[0];
  const consultants = useQuery<StaffBrief[]>({ queryKey: ['wardConsultants', ward?.id], queryFn: () => api.get(`/api/wards/${ward!.id}/consultants`), enabled: !!ward });
  useEffect(() => { if (consultants.data?.length && !consultant) setConsultant(consultants.data[0].id); }, [consultants.data, consultant]);

  const register = async () => {
    setBusy(true);
    try {
      const p = await api.post<Patient>('/api/patients', { name: form.name, dateOfBirth: form.dob, sex: form.sex, phone: form.phone || null, guardian: form.guardian || null, bloodGroup: form.blood || null });
      setPatient(p); setStep('bed');
    } catch (e) { toastError(e); } finally { setBusy(false); }
  };

  const admit = async () => {
    if (!patient || !bedId || !consultant) return;
    setBusy(true);
    try {
      const res = await api.post<AdmitResponse>('/api/admissions', { patientId: patient.id, bedId, consultantUserId: consultant, provisionalDiagnosis: dx || null });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      invalidate();
      router.replace(`/wristband/${res.admission.id}`);
    } catch (e) { toastError(e); } finally { setBusy(false); }
  };

  return (
    <Screen>
      <Segmented<Step> value={step} onChange={(s) => { if (s === 'patient' || patient) setStep(s); }}
        options={[{ key: 'patient', label: '1 · Patient' }, { key: 'bed', label: '2 · Bed' }, { key: 'details', label: '3 · Consultant' }]} />
      <Spacer />

      {step === 'patient' ? (
        <>
          {patient ? <Notice tone="success" title={patient.name} message={`${patient.mrn} · ${patient.ageYears}y ${patient.sex}`} /> : null}
          {!reg ? (
            <>
              <TextField label="Find patient" placeholder="Name, MRN or phone" value={q} onChangeText={setQ} autoCorrect={false} />
              {search.data?.map((p) => (
                <Card key={p.id} style={{ marginBottom: 8 }} onPress={() => {
                  if (p.activeAdmissionId) { toast.warning('Already admitted', `${p.name} already has an active admission`); return; }
                  setPatient(p); setStep('bed');
                }}>
                  <Row><T v="bodyStrong" style={{ flex: 1 }}>{p.name}</T>{p.activeAdmissionId ? <StatusPill status="Occupied" kind="bed" /> : null}</Row>
                  <T v="caption" color="muted">{p.mrn} · {p.ageYears}y {p.sex}{p.phone ? ` · ${p.phone}` : ''}</T>
                </Card>
              ))}
              {q.length >= 2 && search.data?.length === 0 ? <T color="muted" style={{ marginBottom: 8 }}>No match.</T> : null}
              <Button title="Register new patient" icon="person-add-outline" variant="secondary" onPress={() => setReg(true)} />
            </>
          ) : (
            <Card>
              <TextField label="Full name" value={form.name} onChangeText={(v) => setForm({ ...form, name: v })} />
              <TextField label="Date of birth (YYYY-MM-DD)" value={form.dob} onChangeText={(v) => setForm({ ...form, dob: v })} placeholder="1985-04-21" keyboardType="numbers-and-punctuation" />
              <T v="label" color="muted" style={{ marginBottom: 6 }}>Sex</T>
              <Row gap={8} style={{ marginBottom: 12 }}>{[['M', 'Male'], ['F', 'Female'], ['O', 'Other']].map(([k, l]) => <Chip key={k} label={l} selected={form.sex === k} onPress={() => setForm({ ...form, sex: k })} />)}</Row>
              <TextField label="Phone" value={form.phone} onChangeText={(v) => setForm({ ...form, phone: v })} keyboardType="phone-pad" />
              <TextField label="Guardian / attendant" value={form.guardian} onChangeText={(v) => setForm({ ...form, guardian: v })} />
              <T v="label" color="muted" style={{ marginBottom: 6 }}>Blood group</T>
              <Row wrap gap={6} style={{ marginBottom: 12 }}>{['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'].map((g) => <Chip key={g} label={g} selected={form.blood === g} onPress={() => setForm({ ...form, blood: form.blood === g ? '' : g })} />)}</Row>
              <Row gap={8}>
                <Button title="Cancel" variant="secondary" onPress={() => setReg(false)} style={{ flex: 1 }} />
                <Button title="Register" onPress={() => void register()} loading={busy} disabled={!form.name || !/^\d{4}-\d{2}-\d{2}$/.test(form.dob)} style={{ flex: 2 }} />
              </Row>
            </Card>
          )}
        </>
      ) : null}

      {step === 'bed' && board.data ? (
        <>
          <Segmented value={String(ward?.id ?? '')} onChange={(k) => { setWardId(Number(k)); setBedId(null); setConsultant(null); }}
            options={board.data.wards.map((w) => ({ key: String(w.id), label: `${w.name.replace(' Ward', '')} · ${w.occupancy.free}` }))} />
          <T v="label" color="muted" style={{ marginVertical: 10 }}>FREE BEDS</T>
          <Row wrap gap={8}>
            {ward?.beds.filter((b) => b.status === 'Free' || (b.status === 'Reserved' && !b.occupant)).map((b) => (
              <Chip key={b.id} label={b.bedNumber + (b.status === 'Reserved' ? ' (res.)' : '')} icon="bed-outline" selected={bedId === b.id} tone={t.colors.stable} onPress={() => setBedId(b.id)} />
            ))}
          </Row>
          {ward && ward.beds.every((b) => b.status !== 'Free') ? <Notice tone="warning" message="No free beds in this ward." /> : null}
          <Spacer h={16} />
          <Button title={bedId ? `Continue with ${selectedBed?.bedNumber}` : 'Choose a bed'} disabled={!bedId} onPress={() => setStep('details')} size="lg" />
        </>
      ) : null}

      {step === 'details' ? (
        <>
          <Card style={{ marginBottom: 12 }}>
            <T v="bodyStrong">{patient?.name}</T>
            <T color="muted">{ward?.name} · bed {selectedBed?.bedNumber}</T>
          </Card>
          <T v="label" color="muted" style={{ marginBottom: 8 }}>CONSULTANT</T>
          <View style={{ gap: 8, marginBottom: 12 }}>
            {consultants.data?.map((c) => <Chip key={c.id} label={`${c.name}${c.designation ? ' — ' + c.designation : ''}`} selected={consultant === c.id} onPress={() => setConsultant(c.id)} />)}
          </View>
          <TextField label="Provisional diagnosis" value={dx} onChangeText={setDx} placeholder="e.g. Dengue fever" />
          <Button title="Admit & reserve bed" icon="checkmark-circle-outline" size="lg" onPress={() => void admit()} loading={busy} disabled={!patient || !bedId || !consultant} />
          <T v="caption" color="muted" style={{ marginTop: 8, textAlign: 'center' }}>The bed stays Reserved until the nurse scans the bed and the wristband.</T>
        </>
      ) : null}
    </Screen>
  );
}
