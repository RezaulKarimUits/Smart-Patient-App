import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { Pressable, View } from 'react-native';
import Animated, { FadeIn } from 'react-native-reanimated';
import { api, filePart } from '@/api/client';
import { useAdmission, useDoses, useInvalidateAdmission, usePrescriptions } from '@/api/hooks';
import type { ActiveItem, DoseTask, Issue, Monitoring, NurseDraft, VitalsInput, VoiceConfirmResponse, VoiceDraftResponse } from '@/api/types';
import { ConfirmCard, MicButton } from '@/components/clinical';
import { Button, Card, Chip, News2Badge, Notice, Row, Screen, Segmented, Spacer, StatusPill, T, TextField } from '@/components/ui';
import { emptyNurseDraft, NurseDraftEditor } from '@/features/record/NurseDraftEditor';
import { dueLabel, time } from '@/lib/format';
import { requestScan } from '@/lib/scan';
import { useRecorder } from '@/lib/useRecorder';
import { useTheme } from '@/theme';
import { toast, toastError } from '@/components/Toast';

type Phase = 'capture' | 'processing' | 'confirm' | 'saving' | 'done';

/** Nurse entry: speak (English) or type → review editable draft → confirm (wristband scan if a dose is included). */
export default function RecordEntry() {
  const t = useTheme();
  const { id } = useLocalSearchParams<{ id: string }>();
  const admissionId = Number(id);
  const header = useAdmission(admissionId);
  const rx = usePrescriptions(admissionId);
  const doses = useDoses(admissionId);
  const invalidate = useInvalidateAdmission();
  const rec = useRecorder(90);
  const [mode, setMode] = useState<'voice' | 'form'>('voice');
  const [phase, setPhase] = useState<Phase>('capture');
  const [capture, setCapture] = useState<VoiceDraftResponse<NurseDraft> | null>(null);
  const [draft, setDraft] = useState<NurseDraft>(emptyNurseDraft());
  const [showTranscript, setShowTranscript] = useState(false);
  const [result, setResult] = useState<{ monitoring: Monitoring | null; news2?: number | null; band?: string | null; saved: string[] } | null>(null);
  const [formDoses, setFormDoses] = useState<Record<number, { status: string; reason: string }>>({});

  const items: ActiveItem[] = (rx.data ?? []).filter((p) => p.status === 'Signed').flatMap((p) => p.items.filter((i) => i.isActive))
    .map((i) => ({ id: i.id, drugName: i.drugName, form: i.form, strength: i.strength, dose: i.dose, route: i.route, frequency: i.frequency }));
  const openDoses = (doses.data?.doses ?? []).filter((d) => ['Scheduled', 'Due', 'Delayed', 'Missed'].includes(d.status) && Math.abs(d.minutesFromDue) <= 180);

  // ---------- voice ----------
  const upload = async (uri: string | null) => {
    if (!uri) return;
    setPhase('processing');
    try {
      const form = new FormData();
      form.append('admissionId', String(admissionId));
      form.append('audio', filePart(uri, 'capture.m4a', 'audio/mp4'));
      const res = await api.post<VoiceDraftResponse<NurseDraft>>('/api/voice/nurse-entry', form, 120000);
      setCapture(res); setDraft(res.draft); setPhase('confirm');
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (e) { toastError(e); setPhase('capture'); }
  };
  useEffect(() => { rec.onAutoStop.current = (u) => void upload(u); });
  const toggle = async () => { if (rec.recording) await upload(await rec.stop()); else await rec.start(); };

  const confirm = async () => {
    if (!capture) return;
    let wristbandToken: string | null = null;
    if (draft.medications.length > 0) {
      wristbandToken = await requestScan('wristband', `Scan ${header.data?.patientName ?? 'the patient'}'s wristband to confirm the dose`);
      if (!wristbandToken) return;
    }
    setPhase('saving');
    try {
      const r = await api.post<VoiceConfirmResponse>(`/api/voice/${capture.captureId}/confirm`, { draft, wristbandToken });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      const saved = [r.nurse?.vitalId ? 'Vitals' : null, r.nurse?.doses.length ? `${r.nurse.doses.length} dose(s)` : null,
        r.nurse?.labIds.length ? `${r.nurse.labIds.length} result(s)` : null, r.nurse?.noteId ? 'Note' : null].filter(Boolean) as string[];
      setResult({ monitoring: r.monitoring, news2: r.nurse?.news2Score, band: r.nurse?.news2Band, saved });
      invalidate(admissionId); setPhase('done');
    } catch (e) {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      toastError(e); setPhase('confirm');
    }
  };

  const discard = async () => {
    if (capture) { try { await api.post(`/api/voice/${capture.captureId}/discard`); } catch { /* already gone */ } }
    setCapture(null); setDraft(emptyNurseDraft()); setPhase('capture');
  };

  // ---------- typed form ----------
  const saveForm = async () => {
   
    const chosen = Object.entries(formDoses);
    let token: string | null = null;
    if (chosen.length) { token = await requestScan('wristband', 'Scan the wristband to confirm the doses'); if (!token) return; }
    setPhase('saving');
    const saved: string[] = [];
    let monitoring: Monitoring | null = null;
    let news2: number | null | undefined; let band: string | null | undefined;
    try {
      const v = draft.vitals;
      if ([v.systolic, v.diastolic, v.pulse, v.respRate, v.temperature, v.spo2].some((x) => x != null)) {
        const body: VitalsInput = { systolic: v.systolic, diastolic: v.diastolic, pulse: v.pulse, respRate: v.respRate, temperature: v.temperature,
          temperatureUnit: (v.temperatureUnit as 'C' | 'F' | null) ?? 'C', spO2: v.spo2, onOxygen: v.onOxygen, consciousness: v.consciousness };
        const r = await api.post<{ vital: { news2Score: number | null; news2Band: string | null }; monitoring: Monitoring }>(`/api/admissions/${admissionId}/vitals`, body);
        monitoring = r.monitoring; news2 = r.vital.news2Score; band = r.vital.news2Band; saved.push('Vitals');
      }
      for (const [taskId, d] of chosen) {
        await api.post(`/api/doses/${taskId}/record`, { wristbandToken: token, status: d.status, reason: d.reason || null });
      }
      if (chosen.length) saved.push(`${chosen.length} dose(s)`);
      const labs = draft.labResults.filter((l) => l.testName && l.value != null);
      if (labs.length) { const r = await api.post<{ monitoring: Monitoring }>(`/api/admissions/${admissionId}/labs`, labs); monitoring = r.monitoring; saved.push(`${labs.length} result(s)`); }
      if (draft.note?.trim()) { await api.post(`/api/admissions/${admissionId}/notes`, { noteType: 'Nursing', text: draft.note }); saved.push('Note'); }
      if (!saved.length) { toast.warning('Nothing to save yet', 'Speak or fill in at least one field.'); setPhase('capture'); return; }
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setResult({ monitoring, news2, band, saved }); invalidate(admissionId); setPhase('done');
    } catch (e) {
      toastError(e, saved.length ? `Partly saved (${saved.join(', ')}) — the rest failed` : undefined); setPhase('capture');
    }
  };

  const title = header.data ? `${header.data.bedNumber} · ${header.data.patientName}` : 'Record entry';
  const blockers = capture?.warnings.filter((w) => w.level === 'blocker') ?? [];

  if (phase === 'done' && result) {
    const m = result.monitoring;
    return (
      <Screen>
        <Stack.Screen options={{ title }} />
        <Animated.View entering={FadeIn}>
          <Card style={{ alignItems: 'center', gap: 10 }}>
            <Ionicons name="checkmark-circle" size={56} color={t.colors.stable} />
            <T v="title">Saved</T>
            <T color="muted">{result.saved.join(' · ')}</T>
            {result.news2 != null ? <Row gap={10}><News2Badge score={result.news2} band={result.band} size="lg" /><StatusPill status={m?.flag ?? 'Stable'} /></Row> : null}
          </Card>
          {m?.vitalsSuggestion ? <><Spacer /><Notice tone={m.flag === 'Critical' ? 'critical' : 'warning'} icon="alarm-outline" title="Next step" message={m.vitalsSuggestion} /></> : null}
          {m?.newAlerts.map((a) => <Notice key={a.id} tone={a.severity === 'Critical' ? 'critical' : 'warning'} title={`${a.severity} alert raised — consultant notified`} message={a.ruleDetail} />)}
          <Spacer h={16} />
          <Button title="Back to patient" size="lg" onPress={() => router.back()} />
          <Spacer h={8} />
          <Button title="Record another" variant="secondary" onPress={() => { setResult(null); setCapture(null); setDraft(emptyNurseDraft()); setFormDoses({}); setPhase('capture'); }} />
        </Animated.View>
      </Screen>
    );
  }

  return (
    <Screen>
      <Stack.Screen options={{ title }} />
      {phase === 'capture' || phase === 'processing' ? (
        <>
          <Segmented value={mode} onChange={setMode} options={[{ key: 'voice', label: 'Speak' }, { key: 'form', label: 'Type instead' }]} />
          <Spacer h={16} />
          {mode === 'voice' ? (
            <View style={{ alignItems: 'center' }}>
              <MicButton recording={rec.recording} seconds={rec.seconds} onPress={() => void toggle()} busy={phase === 'processing'} />
              {rec.error ? <Notice tone="critical" message={rec.error} /> : null}
              <Card style={{ marginTop: 20, alignSelf: 'stretch' }}>
                <T v="label" color="muted">SAY FOR EXAMPLE</T>
                <T style={{ marginTop: 6 }}>"{header.data?.bedNumber ?? 'M-05'}, BP 100 by 60, pulse 118, temp 102, SpO2 93, respiration 24, Ceftriaxone 1 gram given"</T>
                <T v="caption" color="faint" style={{ marginTop: 6 }}>Speak in English. Nothing is saved until you confirm.</T>
              </Card>
            </View>
          ) : (
            <>
              <NurseDraftEditor draft={draft} onChange={setDraft} items={items} showMeds={false} />
              <ConfirmCard title="Doses due" icon="medkit">
                {openDoses.length === 0 ? <T color="muted">No doses due around now.</T> : openDoses.map((d) => <FormDose key={d.id} d={d} value={formDoses[d.id]}
                  onChange={(v) => setFormDoses((s) => { const n = { ...s }; if (v) n[d.id] = v; else delete n[d.id]; return n; })} />)}
              </ConfirmCard>
              <Button title={Object.keys(formDoses).length ? 'Scan wristband & save' : 'Save'} icon={Object.keys(formDoses).length ? 'qr-code-outline' : 'checkmark'} size="lg" onPress={() => void saveForm()} />
            </>
          )}
        </>
      ) : null}

      {(phase === 'confirm' || phase === 'saving') && capture ? (
        <>
          <Pressable onPress={() => setShowTranscript((s) => !s)}>
            <Card style={{ marginBottom: 12 }}>
              <Row><Ionicons name="chatbubble-ellipses-outline" size={18} color={t.colors.primary} /><T v="label" color="primary" style={{ flex: 1 }}>Transcript</T>
                <Ionicons name={showTranscript ? 'chevron-up' : 'chevron-down'} size={18} color={t.colors.textFaint} /></Row>
              {showTranscript ? <T style={{ marginTop: 8 }} selectable>{capture.transcript}</T> : <T color="muted" numberOfLines={1} style={{ marginTop: 4 }}>{capture.transcript}</T>}
            </Card>
          </Pressable>
          {capture.clarificationQuestion ? (
            <Notice tone="warning" icon="help-circle" title="Please clarify" message={capture.clarificationQuestion}>
              <Button title="Re-record" icon="mic" size="sm" variant="secondary" onPress={() => void discard()} style={{ marginTop: 8, alignSelf: 'flex-start' }} />
            </Notice>
          ) : null}
          {capture.warnings.filter((w) => w.field === 'draft').map((w, i) => <Notice key={i} tone={w.level === 'blocker' ? 'critical' : 'warning'} message={w.message} />)}
          <NurseDraftEditor draft={draft} onChange={setDraft} issues={capture.warnings as Issue[]} items={capture.activeItems.length ? capture.activeItems : items} />
          {blockers.length ? <T v="caption" color="critical" style={{ marginBottom: 8 }}>Fix the red items above (the server re-checks on confirm).</T> : null}
          <Button title={draft.medications.length ? 'Scan wristband & confirm' : 'Confirm & save'} icon={draft.medications.length ? 'qr-code-outline' : 'checkmark-circle'} size="lg"
            loading={phase === 'saving'} onPress={() => void confirm()} />
          <Spacer h={8} />
          <Row gap={8}>
            <Button title="Discard" icon="trash-outline" variant="secondary" style={{ flex: 1 }} onPress={() => void discard()} />
            <Button title="Re-record" icon="mic-outline" variant="ghost" style={{ flex: 1 }} onPress={() => void discard()} />
          </Row>
        </>
      ) : null}
    </Screen>
  );
}

function FormDose({ d, value, onChange }: { d: DoseTask; value?: { status: string; reason: string }; onChange: (v: { status: string; reason: string } | null) => void }) {
  const t = useTheme();
  return (
    <View style={{ paddingVertical: 8, borderBottomWidth: 1, borderColor: t.colors.border }}>
      <Row><T v="bodyStrong" style={{ flex: 1 }}>{time(d.dueAt)} · {d.drugName} {d.dose} {d.route}</T><T v="caption" color={d.isOverdue ? 'critical' : 'muted'}>{dueLabel(d.minutesFromDue, d.status)}</T></Row>
      <Row wrap gap={6} style={{ marginTop: 6 }}>
        {['Given', 'Delayed', 'Refused', 'Withheld'].map((s) => (
          <Chip key={s} label={s} selected={value?.status === s} tone={s === 'Given' ? t.colors.stable : t.colors.watch}
            onPress={() => onChange(value?.status === s ? null : { status: s, reason: value?.reason ?? '' })} />
        ))}
      </Row>
      {value && value.status !== 'Given' ? <TextField label="Reason" value={value.reason} onChangeText={(r) => onChange({ ...value, reason: r })} style={{ marginTop: 8 }} /> : null}
    </View>
  );
}
