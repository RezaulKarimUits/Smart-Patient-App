import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { View } from 'react-native';
import { useWards } from '@/api/hooks';
import { useAuth } from '@/auth/AuthContext';
import { CountTile } from '@/components/clinical';
import { Button, Card, EmptyState, QueryView, Row, Screen, SectionHeader, SkeletonList, StatusPill, T } from '@/components/ui';
import { useTheme } from '@/theme';

/** Reception home: ward summary cards → bed board → admit. */
export default function ReceptionHome() {
  const t = useTheme();
  const { user } = useAuth();
  const q = useWards();
  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <T v="title">Good day, {user?.name.split(' ')[0]}</T>
      <T color="muted">{user?.hospitalName}</T>
      <QueryView q={q} skeleton={<View style={{ marginTop: 16 }}><SkeletonList rows={4} height={120} /></View>}
        empty={(w) => w.length === 0 ? <EmptyState title="No wards yet" message="Ask the hospital admin to add wards and beds." /> : null}>
        {(wards) => {
          const free = wards.reduce((a, w) => a + w.occupancy.free, 0);
          const occ = wards.reduce((a, w) => a + w.occupancy.occupied, 0);
          const res = wards.reduce((a, w) => a + w.occupancy.reserved, 0);
          const cleaning = wards.reduce((a, w) => a + w.occupancy.cleaning, 0);
          return (
            <>
              <Row wrap gap={10} style={{ marginTop: 16 }}>
                <CountTile label="Free beds" value={free} icon="bed-outline" tone={t.colors.stable} onPress={() => router.push('/(reception)/board')} />
                <CountTile label="Occupied" value={occ} icon="person-outline" />
              </Row>
              <Row wrap gap={10} style={{ marginTop: 10 }}>
                <CountTile label="Reserved (awaiting scan)" value={res} icon="time-outline" tone={t.colors.info} />
                <CountTile label="Cleaning" value={cleaning} icon="sparkles-outline" tone={t.colors.bed.Cleaning.fg} />
              </Row>
              <SectionHeader title="Wards" />
              {wards.map((w, i) => (
                <Card key={w.id} delay={i * 50} style={{ marginBottom: 12 }} onPress={() => router.push({ pathname: '/(reception)/board', params: { ward: String(w.id) } })}>
                  <Row>
                    <View style={{ flex: 1 }}>
                      <T v="heading">{w.name}</T>
                      <T v="caption" color="muted">{w.type} · {w.floor ?? ''} · {w.consultants.map((c) => c.name).join(', ')}</T>
                    </View>
                    <T v="display" color={w.occupancy.free > 0 ? 'stable' : 'muted'}>{w.occupancy.free}</T>
                    <T v="caption" color="muted"> free</T>
                  </Row>
                  <View style={{ height: 8, borderRadius: 4, backgroundColor: t.colors.surfaceAlt, overflow: 'hidden', marginVertical: 10, flexDirection: 'row' }}>
                    {(['Occupied', 'Reserved', 'Cleaning', 'Blocked'] as const).map((s) => {
                      const n = s === 'Occupied' ? w.occupancy.occupied : s === 'Reserved' ? w.occupancy.reserved : s === 'Cleaning' ? w.occupancy.cleaning : w.occupancy.blocked;
                      return <View key={s} style={{ width: `${(100 * n) / Math.max(1, w.occupancy.totalBeds)}%`, backgroundColor: t.colors.bed[s].fg }} />;
                    })}
                  </View>
                  <Row gap={6} wrap>
                    <StatusPill status="Free" kind="bed" />
                    <T v="caption" color="muted">{w.occupancy.occupied}/{w.occupancy.totalBeds} occupied · {w.occupancy.occupancyPct}%</T>
                    {w.criticalCount ? <StatusPill status="Critical" /> : null}
                    {w.watchCount ? <StatusPill status="Watch" /> : null}
                    <View style={{ flex: 1 }} />
                    <Ionicons name="chevron-forward" size={18} color={t.colors.textFaint} />
                  </Row>
                </Card>
              ))}
              <Button title="Admit a patient" icon="person-add-outline" size="lg" onPress={() => router.push('/admit')} style={{ marginTop: 8 }} />
            </>
          );
        }}
      </QueryView>
    </Screen>
  );
}
