import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '@/api/client';
import { useInvalidateAdmission, usePending } from '@/api/hooks';
import type { PendingEntry } from '@/api/types';
import { Button, Card, EmptyState, QueryView, Row, Screen, StatusPill, T } from '@/components/ui';
import { relative } from '@/lib/format';
import { useRealtime } from '@/realtime/RealtimeProvider';
import { useTheme } from '@/theme';
import { toastError } from '@/components/Toast';

const key = (e: PendingEntry) => `${e.kind}:${e.id}`;

/** Pending vitals, results and notes with bulk sign-off. Signed items show "Reviewed by Dr X". */
export default function SignOff() {
  const t = useTheme();
  const q = usePending();
  const invalidate = useInvalidateAdmission();
  const { notify } = useRealtime();
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);

  const all = q.data?.flatMap((p) => p.entries) ?? [];
  const toggle = (k: string) => setSel((s) => { const n = new Set(s); if (n.has(k)) n.delete(k); else n.add(k); return n; });
  const sign = async (entries: PendingEntry[]) => {
    if (!entries.length) return;
    setBusy(true);
    try {
      const r = await api.post<{ vitals: number; labs: number; notes: number; signedOffByName: string }>('/api/signoff', {
        vitalIds: entries.filter((e) => e.kind === 'vital').map((e) => e.id), labIds: entries.filter((e) => e.kind === 'lab').map((e) => e.id), noteIds: entries.filter((e) => e.kind === 'note').map((e) => e.id),
      });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      notify({ kind: 'success', title: `Reviewed by ${r.signedOffByName}`, message: `${r.vitals} vitals · ${r.labs} results · ${r.notes} notes` });
      setSel(new Set()); invalidate(); void q.refetch();
    } catch (e) { toastError(e, 'Sign-off failed'); } finally { setBusy(false); }
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}
      footer={sel.size ? (
        <View style={{ padding: 12, backgroundColor: t.colors.surface, borderTopWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border }}>
          <Button title={`Sign off ${sel.size} selected`} icon="checkmark-done" size="lg" loading={busy} onPress={() => void sign(all.filter((e) => sel.has(key(e))))} />
        </View>
      ) : null}>
      <QueryView q={q} empty={(l) => l.length === 0 ? <EmptyState icon="checkmark-done-circle-outline" title="Everything reviewed" message="New vitals, results and nursing notes will appear here." /> : null}>
        {(list) => list.map((p, i) => (
          <Card key={p.admissionId} delay={i * 30} style={{ marginBottom: 12 }}>
            <Row>
              <Pressable onPress={() => router.push(`/patient/${p.admissionId}`)} style={{ flex: 1 }}>
                <T v="heading">{p.bedNumber} · {p.patientName}</T>
                <T v="caption" color="muted">{p.wardName} · {p.entries.length} pending</T>
              </Pressable>
              {p.flag !== 'Stable' ? <StatusPill status={p.flag} /> : null}
            </Row>
            {p.entries.slice(0, 12).map((e) => {
              const on = sel.has(key(e));
              return (
                <Pressable key={key(e)} onPress={() => toggle(key(e))} style={{ flexDirection: 'row', gap: 10, paddingVertical: 10, borderTopWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border, minHeight: 48, alignItems: 'center' }}>
                  <Ionicons name={on ? 'checkbox' : 'square-outline'} size={24} color={on ? t.colors.primary : t.colors.textFaint} />
                  <View style={{ flex: 1 }}>
                    <T>{e.summary}</T>
                    <T v="caption" color="faint">{e.kind} · {e.byName} · {relative(e.at)}{e.source !== 'Form' ? ` · ${e.source.toLowerCase()}` : ''}</T>
                  </View>
                  {e.flag && e.flag !== 'Normal' && e.flag !== 'Low' ? <StatusPill status={e.flag === 'Critical' || e.flag === 'High' ? 'Critical' : 'Watch'} /> : null}
                </Pressable>
              );
            })}
            {p.entries.length > 12 ? <T v="caption" color="muted">+{p.entries.length - 12} more</T> : null}
            <Button title={`Sign off all ${p.entries.length}`} variant="secondary" size="sm" icon="checkmark-done" style={{ marginTop: 8, alignSelf: 'flex-start' }} onPress={() => void sign(p.entries)} />
          </Card>
        ))}
      </QueryView>
    </Screen>
  );
}
