import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { View } from 'react-native';
import { usePriority } from '@/api/hooks';
import { useAuth } from '@/auth/AuthContext';
import { CountTile } from '@/components/clinical';
import { Card, EmptyState, News2Badge, QueryView, Row, Screen, SectionHeader, StatusPill, T } from '@/components/ui';
import { flagColors, useTheme } from '@/theme';

/** Priority list across my wards: open critical alerts first, then NEWS2, then trend alerts. */
export default function Priority() {
  const t = useTheme();
  const { user } = useAuth();
  const q = usePriority();
  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <QueryView q={q} empty={(l) => l.length === 0 ? <EmptyState icon="people-outline" title="No patients on your wards" /> : null}>
        {(list) => {
          const crit = list.filter((p) => p.patient.flag === 'Critical').length;
          const watch = list.filter((p) => p.patient.flag === 'Watch').length;
          const sign = list.reduce((a, p) => a + p.pendingSignOff, 0);
          return (
            <>
              <T v="title">{user?.name}</T>
              <T color="muted">{list.length} patients across your wards</T>
              <Row gap={10} style={{ marginTop: 14 }}>
                <CountTile label="Critical" value={crit} icon="warning-outline" tone={t.colors.critical} />
                <CountTile label="Watch" value={watch} icon="eye-outline" tone={t.colors.watch} />
                <CountTile label="To sign off" value={sign} icon="checkmark-done-outline" tone={t.colors.info} onPress={() => router.push('/(consultant)/signoff')} />
              </Row>
              <SectionHeader title="Priority" count={list.length} />
              {list.map((p, i) => {
                const a = p.patient;
                const f = flagColors(t, a.flag);
                return (
                  <Card key={a.id} delay={i * 35} style={{ marginBottom: 10 }} accent={a.flag !== 'Stable' ? f.fg : undefined} onPress={() => router.push(`/patient/${a.id}`)}>
                    <Row gap={12}>
                      <News2Badge score={a.news2Score} band={a.news2Band} applicable={a.news2Applicable} />
                      <View style={{ flex: 1, gap: 2 }}>
                        <Row><T v="bodyStrong" style={{ flex: 1 }} numberOfLines={1}>{a.patientName}</T><T v="caption" color="faint">#{p.rank}</T></Row>
                        <T v="caption" color="muted" numberOfLines={1}>{a.bedNumber} · {a.wardName} · {a.ageYears}y {a.sex} · day {a.dayOfStay}</T>
                        <Row gap={6} wrap style={{ marginTop: 3 }}>
                          {a.flag !== 'Stable' ? <StatusPill status={a.flag} /> : null}
                          <T v="caption" color={a.flag === 'Critical' ? 'critical' : a.flag === 'Watch' ? 'watch' : 'muted'}>{p.reason}</T>
                          {p.pendingSignOff ? <T v="caption" color="primary">· {p.pendingSignOff} to review</T> : null}
                        </Row>
                      </View>
                      <Ionicons name="chevron-forward" size={18} color={t.colors.textFaint} />
                    </Row>
                    {p.activeAlerts.slice(0, 2).map((al) => (
                      <T key={al.id} v="caption" color={al.severity === 'Critical' ? 'critical' : 'watch'} numberOfLines={2} style={{ marginTop: 6 }}>• {al.aiExplanation ?? al.ruleDetail}</T>
                    ))}
                  </Card>
                );
              })}
            </>
          );
        }}
      </QueryView>
    </Screen>
  );
}
