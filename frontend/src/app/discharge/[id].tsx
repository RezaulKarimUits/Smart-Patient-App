import * as Haptics from '@/lib/haptics';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { api, ApiError } from '@/api/client';
import { useAdmission, useInvalidateAdmission } from '@/api/hooks';
import type { AdmissionHeader, DischargeDraft } from '@/api/types';
import { Button, Card, Notice, Screen, Spacer, T, TextField } from '@/components/ui';
import { time } from '@/lib/format';
import { toastError } from '@/components/Toast';

/** AI drafts the discharge summary from the stay; the consultant edits and confirms. Bed → Cleaning. */
export default function Discharge() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const admissionId = Number(id);
  const header = useAdmission(admissionId);
  const invalidate = useInvalidateAdmission();
  const [summary, setSummary] = useState('');
  const [draftInfo, setDraftInfo] = useState<DischargeDraft | null>(null);
  const [busy, setBusy] = useState<'draft' | 'save' | null>(null);
  const [aiUnavailable, setAiUnavailable] = useState(false);
  const [done, setDone] = useState<AdmissionHeader | null>(null);

  const draft = async () => {
    setBusy('draft');
    try {
      const d = await api.post<DischargeDraft>(`/api/admissions/${admissionId}/discharge/draft`, {}, 90000);
      setDraftInfo(d); setSummary(d.summaryText);
    } catch (e) {
      if (e instanceof ApiError && e.status === 503) setAiUnavailable(true);
      toastError(e);
    } finally { setBusy(null); }
  };

  const confirm = async () => {
    setBusy('save');
    try {
      const h = await api.post<AdmissionHeader>(`/api/admissions/${admissionId}/discharge`, { summary });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      invalidate(admissionId); setDone(h);
    } catch (e) { toastError(e); } finally { setBusy(null); }
  };

  if (done) {
    return (
      <Screen>
        <Notice tone="success" title={`${done.patientName} discharged`} message={`Bed ${done.bedNumber ?? ''} is now Cleaning. The summary is visible in the patient's app.`} />
        <Button title="Back to patients" size="lg" onPress={() => router.dismissTo('/(consultant)')} />
      </Screen>
    );
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: header.data ? `Discharge · ${header.data.bedNumber}` : 'Discharge' }} />
      {header.data ? (
        <Card style={{ marginBottom: 12 }}>
          <T v="heading">{header.data.patientName}</T>
          <T color="muted">{header.data.provisionalDiagnosis} · day {header.data.dayOfStay}</T>
        </Card>
      ) : null}
      {aiUnavailable ? <Notice tone="warning" message="AI drafting is not available — write the summary below." /> : null}
      {!summary && !aiUnavailable ? (
        <>
          <Button title="Draft summary with AI" icon="sparkles-outline" size="lg" loading={busy === 'draft'} onPress={() => void draft()} />
          <Spacer h={8} />
          <Button title="Write it myself" variant="ghost" onPress={() => setAiUnavailable(true)} />
        </>
      ) : (
        <>
          {draftInfo ? <T v="caption" color="faint" style={{ marginBottom: 6 }}>AI draft · {draftInfo.model} · {time(draftInfo.generatedAt)} — review and edit before confirming.</T> : null}
          <TextField multiline value={summary} onChangeText={setSummary} placeholder="Presenting problem, hospital course, key results, condition at discharge, medicines, follow-up…"
            inputStyle={{ minHeight: 320 }} />
          <Button title="Confirm discharge" icon="exit-outline" size="lg" variant="danger" loading={busy === 'save'} disabled={summary.trim().length < 20} onPress={() => void confirm()} />
        </>
      )}
    </Screen>
  );
}
