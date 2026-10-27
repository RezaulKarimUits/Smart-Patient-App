import { useQuery } from '@tanstack/react-query';
import * as Haptics from '@/lib/haptics';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { api, errorMessage } from '@/api/client';
import { useAdmission, useAdmissionAlerts, useInvalidateAdmission } from '@/api/hooks';
import type { Alert, PendingPatient } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { BottomSheet, PatientHeader } from '@/components/clinical';
import { Button, Card, ErrorState, Notice, Row, Segmented, Skeleton, StatusPill, T } from '@/components/ui';
import { AiTab, FilesTab, LabsTab, MedsTab, TimelineTab, VitalsTab } from '@/features/patient/tabs';
import { relative } from '@/lib/format';
import { useRealtime } from '@/realtime/RealtimeProvider';
import { useTheme } from '@/theme';
import { toastError } from '@/components/Toast';

type Tab = 'timeline' | 'vitals' | 'labs' | 'meds' | 'files' | 'ai';

export default function PatientScreen() {
  const t = useTheme();
  const { id, tab: initialTab } = useLocalSearchParams<{ id: string; tab?: Tab }>();
  const admissionId = Number(id);
  const { user } = useAuth();
  const role = user?.role;
  const header = useAdmission(admissionId);
  const alerts = useAdmissionAlerts(admissionId);
  const invalidate = useInvalidateAdmission();
  const { notify } = useRealtime();
  const [tab, setTab] = useState<Tab>(initialTab ?? 'timeline');
  const [signOpen, setSignOpen] = useState(false);
  const pending = useQuery<PendingPatient[]>({ queryKey: ['pending', admissionId], queryFn: () => api.get(`/api/signoff/pending?admissionId=${admissionId}`), enabled: role === 'Consultant' });
  const pendingEntries = pending.data?.[0]?.entries ?? [];
  const active = (alerts.data ?? []).filter((a) => a.status !== 'Resolved');

  const ack = async (a: Alert, resolve = false) => {
    try {
      await api.post(`/api/alerts/${a.id}/${resolve ? 'resolve' : 'ack'}`);
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      invalidate(admissionId); void alerts.refetch();
    } catch (e) { toastError(e, 'Failed'); }
  };

  const signAll = async () => {
    try {
      const body = { vitalIds: pendingEntries.filter((e) => e.kind === 'vital').map((e) => e.id), labIds: pendingEntries.filter((e) => e.kind === 'lab').map((e) => e.id), noteIds: pendingEntries.filter((e) => e.kind === 'note').map((e) => e.id) };
      const r = await api.post<{ vitals: number; labs: number; notes: number; signedOffByName: string }>('/api/signoff', body);
      notify({ kind: 'success', title: `Reviewed by ${r.signedOffByName}`, message: `${r.vitals} vitals · ${r.labs} results · ${r.notes} notes` });
      setSignOpen(false); invalidate(admissionId); void pending.refetch();
    } catch (e) { toastError(e, 'Sign-off failed'); }
  };

  if (header.error && !header.data) return <SafeAreaView style={{ flex: 1, backgroundColor: t.colors.background }}><ErrorState message={errorMessage(header.error)} onRetry={() => void header.refetch()} /></SafeAreaView>;
  const a = header.data;
  const discharged = a?.status === 'Discharged';
  const clinical = role === 'Nurse' || role === 'Consultant';

  return (
    <SafeAreaView edges={['bottom']} style={{ flex: 1, backgroundColor: t.colors.background }}>
      <Stack.Screen options={{ title: a ? `${a.bedNumber ?? ''} · ${a.patientName.split(' ')[0]}` : 'Patient' }} />
      {a ? <PatientHeader a={a} /> : <View style={{ padding: 16, gap: 8 }}><Skeleton height={52} width={52} /><Skeleton height={20} /><Skeleton height={14} width="60%" /></View>}
      <ScrollView contentContainerStyle={{ padding: 16, paddingBottom: 140 }}
        refreshControl={<RefreshControl refreshing={header.isRefetching} onRefresh={() => { invalidate(admissionId); void header.refetch(); }} tintColor={t.colors.primary} />}>
        {active.map((al) => (
          <Card key={al.id} accent={al.severity === 'Critical' ? t.colors.critical : t.colors.watch} style={{ marginBottom: 10 }}>
            <Row>
              <StatusPill status={al.severity} kind="severity" />
              <T v="label" color="muted" style={{ flex: 1 }}>{al.alertType}{al.escalationLevel ? ` · escalated L${al.escalationLevel}` : ''}</T>
              <T v="caption" color="faint">{relative(al.createdAt)}</T>
            </Row>
            <T v="bodyStrong" style={{ marginTop: 6 }}>{al.ruleDetail}</T>
            {al.aiExplanation ? <Notice tone="info" icon="sparkles-outline" title="AI explanation" message={al.aiExplanation} /> : null}
            <Row gap={8} style={{ marginTop: 6 }}>
              {al.status === 'Open' && clinical ? <Button title="Acknowledge" size="sm" icon="checkmark" onPress={() => void ack(al)} /> : null}
              {al.status === 'Acknowledged' ? <T v="caption" color="muted" style={{ flex: 1 }}>Acknowledged by {al.ackByName}</T> : null}
              {role === 'Consultant' ? <Button title="Resolve" size="sm" variant="secondary" onPress={() => void ack(al, true)} /> : null}
            </Row>
          </Card>
        ))}

        {role === 'Consultant' && pendingEntries.length > 0 ? (
          <Card onPress={() => setSignOpen(true)} style={{ marginBottom: 12 }} accent={t.colors.info}>
            <Row><T v="bodyStrong" style={{ flex: 1 }}>{pendingEntries.length} entries awaiting your review</T><T v="label" color="primary">Review →</T></Row>
          </Card>
        ) : null}

        <Segmented<Tab> value={tab} onChange={setTab} options={[
          { key: 'timeline', label: 'Timeline' }, { key: 'vitals', label: 'Vitals & trends' }, { key: 'labs', label: 'Labs' },
          { key: 'meds', label: 'Medications' }, { key: 'files', label: 'Files' }, { key: 'ai', label: 'AI summary' },
        ]} />
        <View style={{ height: 12 }} />
        {a ? (
          <>
            {tab === 'timeline' ? <TimelineTab id={admissionId} /> : null}
            {tab === 'vitals' ? <VitalsTab a={a} /> : null}
            {tab === 'labs' ? <LabsTab id={admissionId} /> : null}
            {tab === 'meds' ? <MedsTab a={a} /> : null}
            {tab === 'files' ? <FilesTab id={admissionId} canUpload={clinical && !discharged} /> : null}
            {tab === 'ai' ? (clinical ? <AiTab a={a} /> : <Notice message="AI summaries are available to nurses and consultants." />) : null}
          </>
        ) : null}
      </ScrollView>

      {a && !discharged && clinical ? (
        <View style={{ position: 'absolute', left: 0, right: 0, bottom: 0, padding: 12, paddingBottom: 20, backgroundColor: t.colors.surface, borderTopWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border, ...t.shadow.raised }}>
          {role === 'Nurse' ? (
            <Row gap={8}>
              <Button title="Record" icon="mic" size="lg" style={{ flex: 2 }} onPress={() => router.push(`/record/${admissionId}`)} />
              <Button title="Report photo" icon="camera-outline" variant="secondary" size="lg" style={{ flex: 1.4 }} onPress={() => router.push(`/report/${admissionId}`)} />
            </Row>
          ) : (
            <Row gap={8}>
              <Button title="Prescribe" icon="create-outline" size="lg" style={{ flex: 1.3 }} onPress={() => router.push(`/prescribe/${admissionId}`)} />
              <Button title="Round note" icon="mic-outline" variant="secondary" size="lg" style={{ flex: 1 }} onPress={() => router.push(`/round-note/${admissionId}`)} />
              <Button title="Discharge" icon="exit-outline" variant="secondary" size="lg" style={{ flex: 1 }} onPress={() => router.push(`/discharge/${admissionId}`)} />
            </Row>
          )}
        </View>
      ) : null}

      <BottomSheet visible={signOpen} onClose={() => setSignOpen(false)} title="Review & sign off">
        {pendingEntries.map((e) => (
          <View key={`${e.kind}${e.id}`} style={{ paddingVertical: 10, borderBottomWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border }}>
            <Row><T v="label" color="primary" style={{ width: 56 }}>{e.kind.toUpperCase()}</T><T style={{ flex: 1 }}>{e.summary}</T></Row>
            <T v="caption" color="faint">{e.byName} · {relative(e.at)} · {e.source}</T>
          </View>
        ))}
        <View style={{ height: 16 }} />
        <Button title={`Sign off all (${pendingEntries.length})`} icon="checkmark-done" size="lg" onPress={() => void signAll()} />
      </BottomSheet>
    </SafeAreaView>
  );
}
