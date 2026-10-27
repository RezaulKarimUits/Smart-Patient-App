import { Ionicons } from '@expo/vector-icons';
import { useState } from 'react';
import { Image, Modal, Pressable, View } from 'react-native';
import { useMyAdmissions, useMyStay } from '@/api/hooks';
import { useAuth } from '@/auth/AuthContext';
import { Card, Chip, EmptyState, IconButton, InfoRow, QueryView, Row, Screen, SectionHeader, SkeletonList, StatusPill, T } from '@/components/ui';
import { dateTime, dayMonth, fmtLab, time } from '@/lib/format';
import { useTheme } from '@/theme';

/** Patient (read-only): my ward, bed and consultant, today's medicines (given/pending), signed reports and images, discharge summary. */
export default function MyStay() {
  const t = useTheme();
  const { user } = useAuth();
  const list = useMyAdmissions();
  const [sel, setSel] = useState<number | null>(null);
  const admissionId = sel ?? list.data?.[0]?.id;
  const q = useMyStay(admissionId);
  const [img, setImg] = useState<string | null>(null);

  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => { void list.refetch(); void q.refetch(); }}>
      <T v="title">আসসালামু আলাইকুম, {user?.name.split(' ')[0]}</T>
      <T color="muted">Your hospital stay</T>
      {list.data && list.data.length > 1 ? (
        <Row wrap gap={6} style={{ marginTop: 10 }}>{list.data.map((a) => <Chip key={a.id} label={`${dayMonth(a.admittedAt)} · ${a.status}`} selected={a.id === admissionId} onPress={() => setSel(a.id)} />)}</Row>
      ) : null}
      {list.data?.length === 0 ? <EmptyState icon="home-outline" title="No admissions" /> : null}
      {admissionId ? (
        <QueryView q={q} skeleton={<View style={{ marginTop: 16 }}><SkeletonList rows={3} height={120} /></View>}>
          {(s) => {
            const given = s.todaysMedicines.filter((m) => m.status === 'Given').length;
            return (
              <>
                <Card style={{ marginTop: 14 }} accent={t.colors.primary}>
                  <InfoRow icon="business-outline" label="Hospital" value={s.hospitalName} />
                  <InfoRow icon="bed-outline" label="Ward · bed" value={`${s.admission.wardName ?? '—'} · ${s.admission.bedNumber ?? '—'}`} />
                  <InfoRow icon="person-outline" label="Consultant" value={<View><T v="bodyStrong">{s.admission.consultantName}</T>{s.consultantDesignation ? <T v="caption" color="muted">{s.consultantDesignation}</T> : null}</View>} />
                  <InfoRow icon="calendar-outline" label="Admitted" value={`${dateTime(s.admission.admittedAt)} · day ${s.admission.dayOfStay}`} />
                  {s.admission.status === 'Discharged' ? <InfoRow icon="exit-outline" label="Discharged" value={dateTime(s.admission.dischargedAt)} /> : null}
                </Card>

                {s.dischargeSummary ? (
                  <>
                    <SectionHeader title="Discharge summary" />
                    <Card><T selectable>{s.dischargeSummary}</T></Card>
                  </>
                ) : null}

                {s.admission.status === 'Active' ? (
                  <>
                    <SectionHeader title={`Today's medicines · ${given}/${s.todaysMedicines.length} given`} />
                    {s.todaysMedicines.length === 0 ? <T color="muted">No medicines scheduled today.</T> : s.todaysMedicines.map((m) => (
                      <Row key={m.doseId} style={{ paddingVertical: 10, borderBottomWidth: 1, borderColor: t.colors.border }}>
                        <T v="heading" style={{ width: 58 }}>{time(m.dueAt)}</T>
                        <View style={{ flex: 1 }}><T v="bodyStrong">{m.medicine}</T><T v="caption" color="muted">{m.dose} · {m.route}</T></View>
                        <StatusPill status={m.status === 'Pending' ? 'Scheduled' : m.status} kind="dose" />
                      </Row>
                    ))}
                  </>
                ) : null}

                <SectionHeader title="Reports (reviewed by your doctor)" />
                {s.signedReports.length === 0 ? <T color="muted">No reviewed reports yet.</T> : s.signedReports.slice(0, 30).map((l) => (
                  <Row key={l.id} style={{ paddingVertical: 8, borderBottomWidth: 1, borderColor: t.colors.border }}>
                    <View style={{ flex: 1 }}><T v="bodyStrong">{l.testName}</T><T v="caption" color="faint">{dayMonth(l.resultAt)} · reviewed by {l.signedOffByName}</T></View>
                    <T v="bodyStrong" color={l.flag === 'Normal' ? undefined : 'watch'}>{fmtLab(l.testName, l.value)} {l.unit}</T>
                  </Row>
                ))}

                {s.files.length ? (
                  <>
                    <SectionHeader title="Images" />
                    <Row wrap gap={10}>
                      {s.files.filter((f) => f.url).map((f) => (
                        <Pressable key={f.id} onPress={() => setImg(f.url)} style={{ width: 100 }}>
                          <Image source={{ uri: f.url! }} style={{ width: 100, height: 100, borderRadius: 12 }} />
                          <T v="caption" numberOfLines={2}>{f.caption ?? f.kind}</T>
                        </Pressable>
                      ))}
                    </Row>
                  </>
                ) : null}
                <Row gap={6} style={{ marginTop: 24, justifyContent: 'center' }}>
                  <Ionicons name="lock-closed-outline" size={14} color={t.colors.textFaint} />
                  <T v="caption" color="faint">Read-only. Ask your nurse or doctor about any question.</T>
                </Row>
              </>
            );
          }}
        </QueryView>
      ) : null}
      <Modal visible={img != null} transparent animationType="fade" onRequestClose={() => setImg(null)}>
        <View style={{ flex: 1, backgroundColor: 'rgba(0,0,0,0.92)', justifyContent: 'center' }}>
          {img ? <Image source={{ uri: img }} style={{ width: '100%', height: '80%' }} resizeMode="contain" /> : null}
          <View style={{ position: 'absolute', top: 40, right: 12 }}><IconButton icon="close" label="Close" color="#fff" onPress={() => setImg(null)} /></View>
        </View>
      </Modal>
    </Screen>
  );
}
