import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { errorMessage } from '@/api/client';
import { useHandover } from '@/api/hooks';
import { Button, Card, EmptyState, ErrorState, Row, Screen, SkeletonList, StatusPill, T } from '@/components/ui';
import { time } from '@/lib/format';
import { flagColors, useTheme } from '@/theme';

/** AI shift handover per patient for my beds, with the underlying facts always shown. */
export default function Handover() {
  const t = useTheme();
  const [enabled, setEnabled] = useState(false);
  const q = useHandover(enabled);
  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      {!enabled ? (
        <Card style={{ alignItems: 'center', gap: 10, paddingVertical: 28 }}>
          <Ionicons name="swap-horizontal" size={44} color={t.colors.primary} />
          <T v="title">Shift handover</T>
          <T color="muted" style={{ textAlign: 'center' }}>An AI summary per patient for the last 12 hours: vitals trend, doses given or missed, new results, open alerts and pending tasks.</T>
          <Button title="Prepare handover" icon="sparkles-outline" size="lg" onPress={() => setEnabled(true)} />
        </Card>
      ) : q.isLoading ? <SkeletonList rows={4} height={150} /> : q.error ? <ErrorState message={errorMessage(q.error)} onRetry={() => void q.refetch()} /> : q.data ? (
        q.data.patients.length === 0 ? <EmptyState title="No patients on your beds" /> : (
          <>
            <T color="muted" style={{ marginBottom: 10 }}>{q.data.shift} shift · since {time(q.data.windowStart)} · {q.data.model}</T>
            {q.data.patients.map((p, i) => {
              const f = flagColors(t, p.flag);
              return (
                <Card key={p.admissionId} delay={i * 50} style={{ marginBottom: 12 }} accent={p.flag !== 'Stable' ? f.fg : undefined} onPress={() => router.push(`/patient/${p.admissionId}`)}>
                  <Row>
                    <T v="heading" style={{ width: 64 }}>{p.bedNumber}</T>
                    <T v="bodyStrong" style={{ flex: 1 }}>{p.patientName}</T>
                    <StatusPill status={p.flag} />
                  </Row>
                  <T style={{ marginTop: 8 }}>{p.summary}</T>
                  {p.priorities.length ? (
                    <View style={{ marginTop: 8, gap: 4 }}>
                      {p.priorities.map((x, k) => <Row key={k} gap={6} style={{ alignItems: 'flex-start' }}><Ionicons name="flag-outline" size={15} color={t.colors.watch} /><T style={{ flex: 1 }}>{x}</T></Row>)}
                    </View>
                  ) : null}
                  <View style={{ marginTop: 10, padding: 10, borderRadius: 12, backgroundColor: t.colors.surfaceAlt, gap: 3 }}>
                    <Fact label="NEWS2" value={`${p.facts.news2First ?? '–'} → ${p.facts.news2Latest ?? '–'}`} />
                    <Fact label="Last vitals" value={p.facts.lastVitals ?? 'none'} />
                    <Fact label="Given" value={p.facts.dosesGiven.join('; ') || 'none'} />
                    {p.facts.dosesMissedOrRefused.length ? <Fact label="Not given" value={p.facts.dosesMissedOrRefused.join('; ')} warn /> : null}
                    {p.facts.newResults.length ? <Fact label="New results" value={p.facts.newResults.join('; ')} /> : null}
                    {p.facts.openAlerts.length ? <Fact label="Alerts" value={p.facts.openAlerts.join('; ')} warn /> : null}
                    <Fact label="Next 4 h" value={p.facts.pendingTasks.join('; ') || 'nothing scheduled'} />
                  </View>
                </Card>
              );
            })}
          </>
        )
      ) : null}
    </Screen>
  );
}

function Fact({ label, value, warn }: { label: string; value: string; warn?: boolean }) {
  return <Row gap={8} style={{ alignItems: 'flex-start' }}><T v="caption" color="muted" style={{ width: 80 }}>{label}</T><T v="caption" color={warn ? 'watch' : undefined} style={{ flex: 1 }}>{value}</T></Row>;
}
