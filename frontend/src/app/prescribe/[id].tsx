import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import * as ImagePicker from 'expo-image-picker';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { Image, Pressable, View } from 'react-native';
import { api, filePart } from '@/api/client';
import { useAdmission, useInvalidateAdmission, usePrescriptions } from '@/api/hooks';
import type { Issue, Prescription, PrescriptionDraft, PrescriptionItemDraft, VoiceConfirmResponse, VoiceDraftResponse } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { ConfirmCard, MicButton } from '@/components/clinical';
import { Button, Card, Chip, IconButton, Notice, Row, Screen, Segmented, Spacer, StatusPill, T, TextField } from '@/components/ui';
import { parseNum } from '@/lib/format';
import { useRecorder } from '@/lib/useRecorder';
import { useTheme } from '@/theme';
import { toastError } from '@/components/Toast';

const FREQ = ['OD', 'BD', 'TDS', 'QID', 'Q4H', 'Q6H', 'Q8H', 'Q12H', 'STAT', 'PRN'];
const ROUTES = ['PO', 'IV', 'IM', 'SC', 'Neb', 'PR', 'Topical'];
const FORMS = ['Tab', 'Cap', 'Inj', 'Syrup', 'Inf', 'Neb'];
const blank = (): PrescriptionItemDraft => ({ drugName: '', form: null, strength: null, dose: null, route: null, frequency: 'BD', durationDays: null, instructions: null });

/** Prescribe by form or voice (+ prescription/report/progress images). Voice prescriptions stay Draft until Sign. */
export default function Prescribe() {
  const t = useTheme();
  const { id } = useLocalSearchParams<{ id: string }>();
  const admissionId = Number(id);
  const { user } = useAuth();
  const header = useAdmission(admissionId);
  const list = usePrescriptions(admissionId);
  const invalidate = useInvalidateAdmission();
  const rec = useRecorder(90);
  const [mode, setMode] = useState<'voice' | 'form'>('voice');
  const [items, setItems] = useState<PrescriptionItemDraft[]>([blank()]);
  const [general, setGeneral] = useState('');
  const [capture, setCapture] = useState<VoiceDraftResponse<PrescriptionDraft> | null>(null);
  const [rx, setRx] = useState<Prescription | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const myDraft = list.data?.find((p) => p.status === 'Draft' && p.consultantUserId === user?.id);
  useEffect(() => { if (myDraft && !rx && !capture) setRx(myDraft); }, [myDraft, rx, capture]);

  const upload = async (uri: string | null) => {
    if (!uri) return;
    setBusy('voice');
    try {
      const form = new FormData();
      form.append('admissionId', String(admissionId));
      form.append('audio', filePart(uri, 'capture.m4a', 'audio/mp4'));
      const res = await api.post<VoiceDraftResponse<PrescriptionDraft>>('/api/voice/prescription', form, 120000);
      setCapture(res); setItems(res.draft.items.length ? res.draft.items : [blank()]); setGeneral(res.draft.generalInstructions ?? '');
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (e) { toastError(e); } finally { setBusy(null); }
  };
  useEffect(() => { rec.onAutoStop.current = (u) => void upload(u); });

  const saveDraft = async () => {
    setBusy('save');
    try {
      if (capture) {
        const r = await api.post<VoiceConfirmResponse>(`/api/voice/${capture.captureId}/confirm`, { draft: { items, generalInstructions: general || null, clarificationQuestion: null } });
        setRx(r.prescription); setCapture(null);
      } else {
        const body = { generalInstructions: general || null, items: items.map((i) => ({ ...i, dose: i.dose ?? '', route: i.route ?? '' })) };
        setRx(await api.post<Prescription>(`/api/admissions/${admissionId}/prescriptions`, body));
      }
      void list.refetch();
    } catch (e) { toastError(e); } finally { setBusy(null); }
  };

  const attach = async (camera: boolean) => {
    if (!rx) return;
    const perm = camera ? await ImagePicker.requestCameraPermissionsAsync() : await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!perm.granted) return;
    const res = camera ? await ImagePicker.launchCameraAsync({ quality: 0.7, mediaTypes: ['images'] }) : await ImagePicker.launchImageLibraryAsync({ quality: 0.7, mediaTypes: ['images'] });
    if (res.canceled || !res.assets[0]) return;
    const a = res.assets[0];
    const form = new FormData();
    form.append('file', filePart(a.uri, a.fileName ?? 'prescription.jpg', a.mimeType ?? 'image/jpeg'));
    form.append('kind', 'Prescription');
    setBusy('attach');
    try { await api.post(`/api/prescriptions/${rx.id}/files`, form, 90000); setRx(await api.get<Prescription>(`/api/prescriptions/${rx.id}`)); }
    catch (e) { toastError(e); } finally { setBusy(null); }
  };

  const sign = async () => {
    if (!rx) return;
    setBusy('sign');
    try {
      const s = await api.post<Prescription>(`/api/prescriptions/${rx.id}/sign`);
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setRx(s); invalidate(admissionId); void list.refetch();
    } catch (e) { toastError(e); } finally { setBusy(null); }
  };

  const deleteDraft = async () => {
    if (!rx) return;
    try { await api.del(`/api/prescriptions/${rx.id}`); setRx(null); setItems([blank()]); void list.refetch(); } catch (e) { toastError(e); }
  };

  const title = header.data ? `Rx · ${header.data.bedNumber} ${header.data.patientName.split(' ')[0]}` : 'Prescribe';

  // ---------- Draft / signed prescription ----------
  if (rx) {
    return (
      <Screen>
        <Stack.Screen options={{ title }} />
        <Card accent={rx.status === 'Signed' ? t.colors.stable : t.colors.watch}>
          <Row><T v="heading" style={{ flex: 1 }}>{rx.status === 'Signed' ? 'Signed prescription' : 'Draft prescription'}</T><StatusPill status={rx.status === 'Signed' ? 'Given' : 'Delayed'} kind="dose" /></Row>
          <T v="caption" color="muted">{rx.source === 'Voice' ? 'From voice · ' : ''}{rx.consultantName}</T>
          {rx.items.map((i) => <Row key={i.id} gap={8} style={{ paddingVertical: 6, alignItems: 'flex-start' }}><Ionicons name="medical-outline" size={16} color={t.colors.primary} /><T style={{ flex: 1 }}>{i.summary}</T></Row>)}
          {rx.generalInstructions ? <T color="muted" style={{ marginTop: 6 }}>{rx.generalInstructions}</T> : null}
        </Card>
        <Spacer />
        <T v="label" color="muted" style={{ marginBottom: 8 }}>ATTACHMENTS</T>
        <Row wrap gap={8}>
          {rx.files.map((f) => f.url ? <Image key={f.id} source={{ uri: f.url }} style={{ width: 90, height: 90, borderRadius: 10 }} /> : null)}
          {rx.status === 'Draft' ? (
            <>
              <Pressable onPress={() => void attach(true)} style={{ width: 90, height: 90, borderRadius: 10, borderWidth: 1.5, borderStyle: 'dashed', borderColor: t.colors.border, alignItems: 'center', justifyContent: 'center' }}>
                <Ionicons name="camera-outline" size={24} color={t.colors.primary} /><T v="caption" color="primary">Photo</T>
              </Pressable>
              <Pressable onPress={() => void attach(false)} style={{ width: 90, height: 90, borderRadius: 10, borderWidth: 1.5, borderStyle: 'dashed', borderColor: t.colors.border, alignItems: 'center', justifyContent: 'center' }}>
                <Ionicons name="images-outline" size={24} color={t.colors.primary} /><T v="caption" color="primary">Gallery</T>
              </Pressable>
            </>
          ) : null}
        </Row>
        <Spacer h={20} />
        {rx.status === 'Draft' ? (
          <>
            <Button title="Sign prescription" icon="ribbon-outline" size="lg" loading={busy === 'sign'} onPress={() => void sign()} />
            <T v="caption" color="muted" style={{ textAlign: 'center', marginTop: 6 }}>Signing creates the nurses' dose tasks (Asia/Dhaka schedule).</T>
            <Spacer />
            <Button title="Delete draft" variant="ghost" icon="trash-outline" onPress={() => void deleteDraft()} />
          </>
        ) : (
          <>
            <Notice tone="success" title="Signed" message={`${rx.doseTasksCreated || 'Dose'} tasks created for the nurses.`} />
            <Button title="Back to patient" size="lg" onPress={() => router.back()} />
            <Spacer h={8} />
            <Button title="New prescription" variant="secondary" onPress={() => { setRx(null); setItems([blank()]); setGeneral(''); }} />
          </>
        )}
      </Screen>
    );
  }

  // ---------- Compose ----------
  return (
    <Screen>
      <Stack.Screen options={{ title }} />
      {!capture ? <Segmented value={mode} onChange={setMode} options={[{ key: 'voice', label: 'Speak' }, { key: 'form', label: 'Form' }]} /> : null}
      <Spacer />
      {mode === 'voice' && !capture ? (
        <View style={{ alignItems: 'center' }}>
          <MicButton recording={rec.recording} seconds={rec.seconds} busy={busy === 'voice'} onPress={() => void (rec.recording ? rec.stop().then(upload) : rec.start())} />
          {rec.error ? <Notice tone="critical" message={rec.error} /> : null}
          <Card style={{ marginTop: 20, alignSelf: 'stretch' }}>
            <T v="label" color="muted">EXAMPLE</T>
            <T style={{ marginTop: 6 }}>"Inj. Ceftriaxone 1 gram IV every 12 hours for 5 days, Tab. Paracetamol 500 as needed for fever, Omeprazole 20 twice daily before food"</T>
          </Card>
        </View>
      ) : (
        <>
          {capture ? (
            <Card style={{ marginBottom: 12 }}>
              <T v="label" color="primary">TRANSCRIPT</T><T style={{ marginTop: 4 }} selectable>{capture.transcript}</T>
            </Card>
          ) : null}
          {capture?.clarificationQuestion ? <Notice tone="warning" icon="help-circle" title="Clarify" message={capture.clarificationQuestion} /> : null}
          {items.map((it, i) => <ItemEditor key={i} item={it} index={i} issues={(capture?.warnings ?? []).filter((w) => w.field === `items[${i}]`)}
            onChange={(n) => setItems(items.map((x, k) => (k === i ? n : x)))} onRemove={() => setItems(items.filter((_, k) => k !== i))} />)}
          <Button title="Add medicine" icon="add" variant="secondary" onPress={() => setItems([...items, blank()])} />
          <Spacer />
          <TextField label="General instructions" value={general} onChangeText={setGeneral} multiline placeholder="Monitoring, diet, investigations…" />
          <Button title="Save as draft" icon="save-outline" size="lg" loading={busy === 'save'} disabled={!items.length} onPress={() => void saveDraft()} />
          {capture ? <Button title="Discard voice draft" variant="ghost" onPress={() => { void api.post(`/api/voice/${capture.captureId}/discard`).catch(() => undefined); setCapture(null); setItems([blank()]); }} /> : null}
        </>
      )}
    </Screen>
  );
}

function ItemEditor({ item, index, onChange, onRemove, issues }: { item: PrescriptionItemDraft; index: number; onChange: (i: PrescriptionItemDraft) => void; onRemove: () => void; issues: Issue[] }) {
  return (
    <ConfirmCard title={item.drugName || `Medicine ${index + 1}`} icon="medical" issues={issues}>
      <Row><View style={{ flex: 1 }} /><IconButton icon="trash-outline" label="Remove medicine" onPress={onRemove} /></Row>
      <TextField label="Drug (generic)" value={item.drugName} onChangeText={(s) => onChange({ ...item, drugName: s })} />
      <Row wrap gap={6} style={{ marginBottom: 10 }}>{FORMS.map((f) => <Chip key={f} label={f} selected={item.form === f} onPress={() => onChange({ ...item, form: item.form === f ? null : f })} />)}</Row>
      <Row gap={8}>
        <TextField label="Strength" value={item.strength ?? ''} onChangeText={(s) => onChange({ ...item, strength: s || null })} style={{ flex: 1 }} />
        <TextField label="Dose *" value={item.dose ?? ''} onChangeText={(s) => onChange({ ...item, dose: s || null })} style={{ flex: 1 }} />
      </Row>
      <T v="label" color="muted" style={{ marginBottom: 6 }}>Route *</T>
      <Row wrap gap={6} style={{ marginBottom: 10 }}>{ROUTES.map((r) => <Chip key={r} label={r} selected={item.route === r} onPress={() => onChange({ ...item, route: r })} />)}</Row>
      <T v="label" color="muted" style={{ marginBottom: 6 }}>Frequency</T>
      <Row wrap gap={6} style={{ marginBottom: 10 }}>{FREQ.map((f) => <Chip key={f} label={f} selected={item.frequency === f} onPress={() => onChange({ ...item, frequency: f })} />)}</Row>
      <Row gap={8}>
        <TextField label="Days" keyboardType="number-pad" value={item.durationDays == null ? '' : String(item.durationDays)} onChangeText={(s) => onChange({ ...item, durationDays: parseNum(s) })} style={{ flex: 1 }} />
        <TextField label="Instructions" value={item.instructions ?? ''} onChangeText={(s) => onChange({ ...item, instructions: s || null })} style={{ flex: 2 }} placeholder="before meal / at night" />
      </Row>
    </ConfirmCard>
  );
}
