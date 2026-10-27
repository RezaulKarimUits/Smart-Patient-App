import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { View } from 'react-native';
import { api } from '@/api/client';
import { useInvalidateAdmission, useNurseHome } from '@/api/hooks';
import type { NurseBed } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { Badge, Button, Card, EmptyState, News2Badge, Notice, QueryView, Row, Screen, StatusPill, T } from '@/components/ui';
import { relative, time } from '@/lib/format';
import { flagColors, useTheme } from '@/theme';

/** "My beds" this shift: Critical/Watch first, badges for due doses, alerts and vitals re-check tasks. */
export default function MyBeds() {
  const { user } = useAuth();
  const q = useNurseHome();
  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <QueryView q={q} empty={(h) => h.beds.length === 0 ? <EmptyState icon="calendar-outline" title="No beds assigned this shift" message="Ask the ward in-charge to assign your beds." /> : null}>
        {(home) => (
          <>
            <Row style={{ marginBottom: 12 }}>
              <View style={{ flex: 1 }}>
                <T v="title">{user?.name.split(' ')[0]}'s beds</T>
                <T color="muted">{home.shift} shift · {time(home.shiftStart)}–{time(home.shiftEnd)} · {home.beds.filter((b) => b.occupant).length} patients</T>
              </View>
            </Row>
            {home.beds.map((b, i) => <BedCard key={b.bedId} b={b} delay={i * 40} />)}
          </>
        )}
      </QueryView>
    </Screen>
  );
}

function BedCard({ b, delay }: { b: NurseBed; delay: number }) {
  const t = useTheme();
  const invalidate = useInvalidateAdmission();
  const o = b.occupant;
  if (!o) {
    return (
      <Card delay={delay} style={{ marginBottom: 10, opacity: 0.9 }} onPress={() => router.push(`/bed/${b.bedId}`)}>
        <Row><T v="heading" style={{ width: 70 }}>{b.bedNumber}</T><StatusPill status={b.bedStatus} kind="bed" /><View style={{ flex: 1 }} /><T v="caption" color="faint">{b.wardName}</T></Row>
      </Card>
    );
  }
  const f = flagColors(t, o.flag);
  return (
    <Card delay={delay} style={{ marginBottom: 10 }} accent={o.flag !== 'Stable' ? f.fg : undefined} onPress={() => router.push(`/patient/${o.admissionId}`)}>
      <Row gap={12}>
        <View style={{ alignItems: 'center', width: 56 }}>
          <T v="heading">{b.bedNumber}</T>
          {o.news2Score != null ? <News2Badge score={o.news2Score} band={o.news2Band} size="sm" /> : null}
        </View>
        <View style={{ flex: 1, gap: 2 }}>
          <T v="bodyStrong" numberOfLines={1}>{o.patientName}</T>
          <T v="caption" color="muted" numberOfLines={1}>{o.ageYears}y {o.sex} · {o.diagnosis ?? ''}</T>
          <Row gap={6} wrap style={{ marginTop: 4 }}>
            {o.flag !== 'Stable' ? <StatusPill status={o.flag} /> : null}
            {o.awaitingScan ? <StatusPill status="Reserved" kind="bed" /> : null}
            {b.overdueDoses ? <StatusPill status="Missed" kind="dose" /> : null}
            <T v="caption" color="faint">{b.lastVitalsAt ? `vitals ${relative(b.lastVitalsAt)}` : 'no vitals yet'}</T>
          </Row>
        </View>
        <View style={{ alignItems: 'flex-end', gap: 6 }}>
          {b.dueDoses ? <Row gap={4}><Ionicons name="medkit" size={16} color={t.colors.info} /><Badge count={b.dueDoses} color={t.colors.info} /></Row> : b.nextDoseAt ? <T v="caption" color="muted">next {time(b.nextDoseAt)}</T> : null}
          {b.openAlerts ? <Row gap={4}><Ionicons name="warning" size={16} color={t.colors.critical} /><Badge count={b.openAlerts} /></Row> : null}
        </View>
      </Row>
      {o.awaitingScan ? <Notice tone="info" icon="qr-code-outline" message="New admission: scan the bed QR, then the wristband, to confirm." /> : null}
      {b.careTasks.map((c) => (
        <Row key={c.id} gap={8} style={{ marginTop: 8, padding: 10, borderRadius: 12, backgroundColor: c.isOverdue ? t.colors.criticalSoft : t.colors.watchSoft }}>
          <Ionicons name="alarm-outline" size={18} color={c.isOverdue ? t.colors.critical : t.colors.watch} />
          <T style={{ flex: 1 }}>{c.detail} · due {time(c.dueAt)}</T>
          <Button title="Record vitals" size="sm" onPress={() => router.push(`/record/${o.admissionId}`)} />
          <Button title="Done" size="sm" variant="ghost" onPress={() => void api.post(`/api/care-tasks/${c.id}/done`).then(() => invalidate(o.admissionId))} />
        </Row>
      ))}
    </Card>
  );
}
