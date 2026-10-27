import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { View, useWindowDimensions } from 'react-native';
import { useBoard } from '@/api/hooks';
import type { BedStatus } from '@/api/types';
import { BedTile } from './clinical';
import { Chip, EmptyState, QueryView, Row, Screen, Segmented, Skeleton, T } from './ui';

const FILTERS: (BedStatus | 'All' | 'Watch')[] = ['All', 'Free', 'Occupied', 'Reserved', 'Cleaning', 'Blocked', 'Watch'];

/** Live bed grid per ward, colour-coded by status, with patient name + NEWS2 badge on occupied beds. */
export function BoardView({ initialWard }: { initialWard?: number }) {
  const q = useBoard();
  const { width } = useWindowDimensions();
  const cols = width > 900 ? 5 : width > 640 ? 4 : 2;
  const [ward, setWard] = useState<string>(initialWard ? String(initialWard) : 'all');
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>('All');

  const wards = useMemo(() => q.data?.wards.filter((w) => ward === 'all' || String(w.id) === ward) ?? [], [q.data, ward]);
  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <QueryView q={q} skeleton={<View style={{ gap: 12 }}><Skeleton height={40} />{[0, 1, 2].map((i) => <Skeleton key={i} height={112} radius={16} />)}</View>}>
        {(board) => (
          <>
            <Segmented value={ward} onChange={setWard}
              options={[{ key: 'all', label: 'All wards' }, ...board.wards.map((w) => ({ key: String(w.id), label: `${w.name.replace(' Ward', '')} · ${w.occupancy.free} free` }))]} />
            <Row wrap gap={6} style={{ marginVertical: 10 }}>
              {FILTERS.map((f) => <Chip key={f} label={f} selected={filter === f} onPress={() => setFilter(f)} />)}
            </Row>
            {wards.map((w) => {
              const beds = w.beds.filter((b) => filter === 'All' || (filter === 'Watch' ? b.occupant && b.occupant.flag !== 'Stable' : b.status === filter));
              const rows: (typeof beds)[] = [];
              for (let i = 0; i < beds.length; i += cols) rows.push(beds.slice(i, i + cols));
              return (
                <View key={w.id} style={{ marginBottom: 22 }}>
                  <Row style={{ marginBottom: 8 }}>
                    <T v="heading" style={{ flex: 1 }}>{w.name}</T>
                    <T v="caption" color="muted">{w.occupancy.occupied} occ · {w.occupancy.free} free · {w.occupancy.occupancyPct}%</T>
                  </Row>
                  {rows.length === 0 ? <T color="faint">No beds match.</T> : rows.map((r, i) => (
                    <View key={i} style={{ flexDirection: 'row', gap: 10, marginBottom: 10 }}>
                      {r.map((b) => <BedTile key={b.id} bed={b} onPress={() => router.push(b.occupant ? `/patient/${b.occupant.admissionId}` : `/bed/${b.id}`)} />)}
                      {Array.from({ length: cols - r.length }).map((_, k) => <View key={`f${k}`} style={{ flex: 1 }} />)}
                    </View>
                  ))}
                </View>
              );
            })}
            {wards.length === 0 ? <EmptyState icon="bed-outline" title="No wards" /> : null}
          </>
        )}
      </QueryView>
    </Screen>
  );
}
