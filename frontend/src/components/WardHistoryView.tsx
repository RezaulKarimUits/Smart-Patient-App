import { useMemo, useState } from 'react';
import { StyleSheet, View, useWindowDimensions } from 'react-native';
import Svg, { G, Rect, Text as SvgText } from 'react-native-svg';
import { useWardHistory, useWards } from '@/api/hooks';
import { CountTile } from './clinical';
import { Chip, EmptyState, QueryView, Row, Screen, SectionHeader, Segmented, StatusPill, T } from './ui';
import { dateTime, dhakaDateKey } from '@/lib/format';
import { useTheme } from '@/theme';

const RANGES = { '7': 7, '14': 14, '30': 30 } as const;
const EVENT_KIND: Record<string, 'Free' | 'Reserved' | 'Occupied' | 'Cleaning' | 'Blocked'> = {
  Reserved: 'Reserved', Admitted: 'Occupied', TransferredIn: 'Occupied', TransferredOut: 'Cleaning', Discharged: 'Cleaning', Cleaned: 'Free', Blocked: 'Blocked', Unblocked: 'Free', Released: 'Free',
};

/** Ward history: timeline of bed events plus admissions/transfers/discharges and occupancy/LOS from vw_WardHistory. */
export function WardHistoryView() {
  const t = useTheme();
  const { width } = useWindowDimensions();
  const wards = useWards();
  const [wardId, setWardId] = useState<number | null>(null);
  const [range, setRange] = useState<keyof typeof RANGES>('7');
  const [type, setType] = useState<string>('All');
  const ward = wardId ?? wards.data?.[0]?.id ?? null;
  const to = dhakaDateKey();
  const from = dhakaDateKey(new Date(Date.now() - (RANGES[range] - 1) * 864e5));
  const q = useWardHistory(ward, from, to);
  const chartW = Math.min(width - 32, 700);

  const events = useMemo(() => (q.data?.events ?? []).filter((e) => type === 'All' || e.eventType === type), [q.data, type]);
  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      {wards.data ? <Segmented value={String(ward)} onChange={(k) => setWardId(Number(k))} options={wards.data.map((w) => ({ key: String(w.id), label: w.name.replace(' Ward', '') }))} /> : null}
      <Row gap={6} style={{ marginVertical: 8 }}>{(Object.keys(RANGES) as (keyof typeof RANGES)[]).map((r) => <Chip key={r} label={`${r} days`} selected={range === r} onPress={() => setRange(r)} />)}</Row>
      <QueryView q={q} empty={(h) => h.days.length === 0 && h.events.length === 0 ? <EmptyState icon="stats-chart-outline" title="No activity in this range" /> : null}>
        {(h) => {
          const max = Math.max(1, ...h.days.map((d) => Math.max(d.admissionsIn + d.transfersIn, d.discharges + d.transfersOut, d.occupiedBedDays)));
          const bw = Math.max(14, Math.min(40, (chartW - 30) / Math.max(1, h.days.length) - 8));
          const ch = 120;
          return (
            <>
              <Row wrap gap={10}>
                <CountTile label="Admissions" value={h.totals.admissionsIn} icon="log-in-outline" tone={t.colors.stable} />
                <CountTile label="Discharges" value={h.totals.discharges} icon="log-out-outline" tone={t.colors.watch} />
              </Row>
              <Row wrap gap={10} style={{ marginTop: 10 }}>
                <CountTile label="Transfers in / out" value={`${h.totals.transfersIn} / ${h.totals.transfersOut}`} icon="swap-horizontal-outline" />
                <CountTile label="Avg LOS (days)" value={h.totals.avgLengthOfStayDays ?? '—'} icon="time-outline" />
              </Row>
              <Row wrap gap={10} style={{ marginTop: 10 }}>
                <CountTile label="Occupied bed-days" value={h.totals.occupiedBedDays} icon="bed-outline" />
                <CountTile label={`Avg occupancy · now ${h.occupancyNow.occupancyPct}%`} value={`${h.totals.avgOccupancyPct}%`} icon="pie-chart-outline" />
              </Row>
              <SectionHeader title="Daily movement" />
              <View style={{ backgroundColor: t.colors.surface, borderRadius: 16, padding: 12, borderWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border }}>
                <Svg width={chartW - 24} height={ch + 24}>
                  {h.days.map((d, i) => {
                    const x = 4 + i * (bw + 8);
                    const a = ((d.admissionsIn + d.transfersIn) / max) * ch, o = ((d.discharges + d.transfersOut) / max) * ch, b = (d.occupiedBedDays / max) * ch;
                    return (
                      <G key={d.day}>
                        <Rect x={x} y={ch - b} width={bw} height={b} fill={t.colors.primarySoft} rx={4} />
                        <Rect x={x + bw * 0.15} y={ch - a} width={bw * 0.3} height={a} fill={t.colors.stable} rx={2} />
                        <Rect x={x + bw * 0.55} y={ch - o} width={bw * 0.3} height={o} fill={t.colors.watch} rx={2} />
                        <SvgText x={x + bw / 2} y={ch + 16} fontSize={9} fill={t.colors.textFaint} textAnchor="middle">{d.day.slice(8)}</SvgText>
                      </G>
                    );
                  })}
                </Svg>
                <Row gap={12} wrap>
                  <Legend color={t.colors.stable} label="In" /><Legend color={t.colors.watch} label="Out" /><Legend color={t.colors.primarySoft} label="Bed-days" />
                </Row>
              </View>
              <SectionHeader title="Timeline" count={events.length} />
              <Row wrap gap={6} style={{ marginBottom: 8 }}>
                {['All', 'Admitted', 'TransferredIn', 'TransferredOut', 'Discharged', 'Cleaned', 'Reserved', 'Blocked'].map((x) => <Chip key={x} label={x} selected={type === x} onPress={() => setType(x)} />)}
              </Row>
              {events.map((e) => (
                <Row key={e.id} gap={10} style={{ paddingVertical: 10, borderBottomWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border, alignItems: 'flex-start' }}>
                  <T v="caption" color="faint" style={{ width: 86 }}>{dateTime(e.at)}</T>
                  <View style={{ flex: 1, gap: 2 }}>
                    <Row gap={6}><StatusPill status={EVENT_KIND[e.eventType] ?? 'Blocked'} kind="bed" /><T v="bodyStrong">{e.eventType} · {e.bedNumber}</T></Row>
                    {e.patientName ? <T>{e.patientName}</T> : null}
                    <T v="caption" color="muted">{[e.note, e.byUserName].filter(Boolean).join(' · ')}</T>
                  </View>
                </Row>
              ))}
            </>
          );
        }}
      </QueryView>
    </Screen>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return <Row gap={4}><View style={{ width: 10, height: 10, borderRadius: 3, backgroundColor: color }} /><T v="caption" color="muted">{label}</T></Row>;
}
