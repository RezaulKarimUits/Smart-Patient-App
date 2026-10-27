import * as Haptics from '@/lib/haptics';
import { router } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { api, errorMessage } from '@/api/client';
import { useInvalidateAdmission, useRound } from '@/api/hooks';
import type { DoseTask, RoundPatient } from '@/api/types';
import { Button, Card, EmptyState, QueryView, Row, Screen, StatusPill, T } from '@/components/ui';
import { DoseSheet } from '@/features/patient/tabs';
import { dueLabel, time } from '@/lib/format';
import { requestScan } from '@/lib/scan';
import { useRealtime } from '@/realtime/RealtimeProvider';
import { flagColors, useTheme } from '@/theme';
import { toastError } from '@/components/Toast';

/** Medication round: due and overdue doses for my beds; recording needs the wristband scan ("Wrong patient" on mismatch). */
export default function Round() {
  const t = useTheme();
  const q = useRound();
  const invalidate = useInvalidateAdmission();
  const { notify } = useRealtime();
  const [sel, setSel] = useState<{ task: DoseTask; p: RoundPatient } | null>(null);

  const record = async (status: string, reason?: string) => {
    if (!sel) return;
    const { task, p } = sel;
    const token = await requestScan('wristband', `Scan ${p.patientName}'s wristband (${p.bedNumber}) for ${task.drugName}`);
    if (!token) return;
    try {
      await api.post(`/api/doses/${task.id}/record`, { wristbandToken: token, status, reason: reason ?? null });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      notify({ kind: 'success', title: `${task.drugName} · ${status}`, message: `${p.bedNumber} ${p.patientName}` });
      setSel(null); invalidate(p.admissionId); void q.refetch();
    } catch (e) {
      toastError(e, errorMessage(e) === 'Wrong patient' ? 'Wrong patient — dose NOT recorded' : 'Dose not recorded');
    }
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <QueryView q={q} empty={(l) => l.length === 0 ? <EmptyState icon="checkmark-done-circle-outline" title="All caught up" message="No doses due in the next hour." /> : null}>
        {(list) => list.map((p, i) => {
          const f = flagColors(t, p.flag);
          return (
            <Card key={p.admissionId} delay={i * 40} style={{ marginBottom: 12 }} accent={p.flag !== 'Stable' ? f.fg : undefined}>
              <Row>
                <T v="heading" style={{ width: 64 }}>{p.bedNumber}</T>
                <T v="bodyStrong" style={{ flex: 1 }} numberOfLines={1}>{p.patientName}</T>
                {p.flag !== 'Stable' ? <StatusPill status={p.flag} /> : null}
              </Row>
              {p.doses.map((d) => (
                <View key={d.id} style={{ marginTop: 10 }}>
                  <Card onPress={() => setSel({ task: d, p })} style={{ padding: 12, backgroundColor: d.isOverdue ? t.colors.criticalSoft : t.colors.surfaceAlt }}>
                    <Row>
                      <T v="heading" style={{ width: 56 }}>{time(d.dueAt)}</T>
                      <View style={{ flex: 1 }}>
                        <T v="bodyStrong">{d.form ? `${d.form}. ` : ''}{d.drugName} {d.dose} {d.route}</T>
                        {d.instructions ? <T v="caption" color="muted">{d.instructions}</T> : null}
                      </View>
                      <View style={{ alignItems: 'flex-end', gap: 4 }}>
                        <StatusPill status={d.status} kind="dose" />
                        <T v="caption" color={d.isOverdue ? 'critical' : 'muted'}>{dueLabel(d.minutesFromDue, d.status)}</T>
                      </View>
                    </Row>
                  </Card>
                </View>
              ))}
              <Button title="Open patient" variant="ghost" size="sm" icon="person-outline" style={{ alignSelf: 'flex-start', marginTop: 6 }}
                onPress={() => router.push(`/patient/${p.admissionId}`)} />
            </Card>
          );
        })}
      </QueryView>
      <DoseSheet task={sel?.task ?? null} onClose={() => setSel(null)} onRecord={(s, r) => void record(s, r)} />
    </Screen>
  );
}
