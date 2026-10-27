import * as Haptics from '@/lib/haptics';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { api, filePart } from '@/api/client';
import { useAdmission, useInvalidateAdmission } from '@/api/hooks';
import type { RoundNoteDraft, VoiceDraftResponse } from '@/api/types';
import { MicButton } from '@/components/clinical';
import { Button, Card, Notice, Screen, Segmented, Spacer, T, TextField } from '@/components/ui';
import { useRecorder } from '@/lib/useRecorder';
import { toastError } from '@/components/Toast';

/** Consultant round note by voice (AI drafts concise English, consultant edits & confirms) or typed. */
export default function RoundNote() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const admissionId = Number(id);
  const header = useAdmission(admissionId);
  const invalidate = useInvalidateAdmission();
  const rec = useRecorder(90);
  const [mode, setMode] = useState<'voice' | 'form'>('voice');
  const [capture, setCapture] = useState<VoiceDraftResponse<RoundNoteDraft> | null>(null);
  const [note, setNote] = useState('');
  const [plan, setPlan] = useState('');
  const [busy, setBusy] = useState(false);

  const upload = async (uri: string | null) => {
    if (!uri) return;
    setBusy(true);
    try {
      const form = new FormData();
      form.append('admissionId', String(admissionId));
      form.append('audio', filePart(uri, 'capture.m4a', 'audio/mp4'));
      const r = await api.post<VoiceDraftResponse<RoundNoteDraft>>('/api/voice/round-note', form, 120000);
      setCapture(r); setNote(r.draft.note); setPlan(r.draft.plan ?? '');
    } catch (e) { toastError(e); } finally { setBusy(false); }
  };
  useEffect(() => { rec.onAutoStop.current = (u) => void upload(u); });

  const save = async () => {
    setBusy(true);
    try {
      if (capture) await api.post(`/api/voice/${capture.captureId}/confirm`, { draft: { note, plan: plan || null, clarificationQuestion: null } });
      else await api.post(`/api/admissions/${admissionId}/notes`, { noteType: 'ConsultantRound', text: plan ? `${note}\nPlan: ${plan}` : note });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      invalidate(admissionId); router.back();
    } catch (e) { toastError(e); } finally { setBusy(false); }
  };

  const editing = mode === 'form' || capture;
  return (
    <Screen>
      <Stack.Screen options={{ title: header.data ? `Round · ${header.data.bedNumber}` : 'Round note' }} />
      {!capture ? <Segmented value={mode} onChange={setMode} options={[{ key: 'voice', label: 'Speak' }, { key: 'form', label: 'Type' }]} /> : null}
      <Spacer />
      {!editing ? (
        <View style={{ alignItems: 'center' }}>
          <MicButton recording={rec.recording} seconds={rec.seconds} busy={busy} onPress={() => void (rec.recording ? rec.stop().then(upload) : rec.start())} />
          {rec.error ? <Notice tone="critical" message={rec.error} /> : null}
        </View>
      ) : (
        <>
          {capture ? <Card style={{ marginBottom: 12 }}><T v="label" color="primary">TRANSCRIPT</T><T selectable style={{ marginTop: 4 }}>{capture.transcript}</T></Card> : null}
          {capture?.clarificationQuestion ? <Notice tone="warning" title="Clarify" message={capture.clarificationQuestion} /> : null}
          <TextField label="Assessment / findings" value={note} onChangeText={setNote} multiline />
          <TextField label="Plan" value={plan} onChangeText={setPlan} multiline />
          <Button title="Confirm round note" icon="checkmark-circle" size="lg" loading={busy} disabled={!note.trim()} onPress={() => void save()} />
          {capture ? <Button title="Discard" variant="ghost" onPress={() => { void api.post(`/api/voice/${capture.captureId}/discard`).catch(() => undefined); setCapture(null); }} /> : null}
        </>
      )}
    </Screen>
  );
}
