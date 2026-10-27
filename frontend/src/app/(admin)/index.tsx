import { router } from 'expo-router';
import { View } from 'react-native';
import { useWards } from '@/api/hooks';
import { useAuth } from '@/auth/AuthContext';
import { CountTile } from '@/components/clinical';
import { Card, EmptyState, QueryView, Row, Screen, SectionHeader, StatusPill, T } from '@/components/ui';
import { useTheme } from '@/theme';

export default function AdminHome() {
  const t = useTheme();
  const { user } = useAuth();
  const q = useWards();
  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <T v="title">{user?.hospitalName}</T>
      <T color="muted">Live occupancy</T>
      <QueryView q={q} empty={(l) => l.length === 0 ? <EmptyState title="No wards yet" action="Add a ward" onAction={() => router.push('/(admin)/wards')} /> : null}>
        {(wards) => {
          const sum = (k: 'totalBeds' | 'occupied' | 'free' | 'cleaning' | 'reserved' | 'blocked') => wards.reduce((a, w) => a + w.occupancy[k], 0);
          const total = sum('totalBeds');
          return (
            <>
              <Row gap={10} style={{ marginTop: 14 }}>
                <CountTile label="Occupancy" value={`${total ? Math.round((100 * (sum('occupied') + sum('reserved'))) / total) : 0}%`} icon="pie-chart-outline" />
                <CountTile label="Free beds" value={sum('free')} icon="bed-outline" tone={t.colors.stable} />
              </Row>
              <Row gap={10} style={{ marginTop: 10 }}>
                <CountTile label="Critical patients" value={wards.reduce((a, w) => a + w.criticalCount, 0)} icon="warning-outline" tone={t.colors.critical} />
                <CountTile label="Watch patients" value={wards.reduce((a, w) => a + w.watchCount, 0)} icon="eye-outline" tone={t.colors.watch} />
              </Row>
              <SectionHeader title="Wards" />
              {wards.map((w, i) => (
                <Card key={w.id} delay={i * 40} style={{ marginBottom: 10 }}>
                  <Row><T v="heading" style={{ flex: 1 }}>{w.name}</T><T v="heading" color="primary">{w.occupancy.occupancyPct}%</T></Row>
                  <View style={{ height: 8, borderRadius: 4, backgroundColor: t.colors.surfaceAlt, overflow: 'hidden', marginVertical: 8, flexDirection: 'row' }}>
                    {(['Occupied', 'Reserved', 'Cleaning', 'Blocked'] as const).map((s) => {
                      const n = s === 'Occupied' ? w.occupancy.occupied : s === 'Reserved' ? w.occupancy.reserved : s === 'Cleaning' ? w.occupancy.cleaning : w.occupancy.blocked;
                      return <View key={s} style={{ width: `${(100 * n) / Math.max(1, w.occupancy.totalBeds)}%`, backgroundColor: t.colors.bed[s].fg }} />;
                    })}
                  </View>
                  <Row wrap gap={6}>
                    <T v="caption" color="muted">{w.occupancy.occupied} occ · {w.occupancy.free} free · {w.occupancy.cleaning} cleaning · {w.occupancy.blocked} blocked</T>
                    {w.criticalCount ? <StatusPill status="Critical" /> : null}
                    {w.watchCount ? <StatusPill status="Watch" /> : null}
                  </Row>
                  <T v="caption" color="faint" style={{ marginTop: 4 }}>{w.consultants.map((c) => c.name).join(', ') || 'No consultant mapped'}</T>
                </Card>
              ))}
            </>
          );
        }}
      </QueryView>
    </Screen>
  );
}
