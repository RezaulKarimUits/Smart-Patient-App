import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import * as ImagePicker from 'expo-image-picker';
import { useState } from 'react';
import { Image, Modal, Pressable, ScrollView, StyleSheet, View, useWindowDimensions } from 'react-native';
import { api, errorMessage, filePart } from '@/api/client';
import { useAiProgress, useDoses, useFiles, useInvalidateAdmission, useJourney, useLabs, useTimeline, useVitals } from '@/api/hooks';
import type { AdmissionHeader, DoseTask, FileItem } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { BottomSheet, TrendChart } from '@/components/clinical';
import { Button, Card, Chip, EmptyState, ErrorState, IconButton, News2Badge, Notice, QueryView, Row, SkeletonList, StatusPill, T, TextField, type IconName } from '@/components/ui';
import { dateTime, dayMonth, dueLabel, fmtLab, num, time } from '@/lib/format';
import { requestScan } from '@/lib/scan';
import { useRealtime } from '@/realtime/RealtimeProvider';
import { useTheme } from '@/theme';
import { toast, toastError } from '@/components/Toast';

// ---------------- Timeline ----------------
const TL_ICON: Record<string, IconName> = { vitals: 'pulse', lab: 'flask', note: 'document-text', dose: 'medkit', prescription: 'reader', file: 'image', alert: 'warning', bed: 'bed' };

export function TimelineTab({ id }: { id: number }) {
  const t = useTheme();
  const q = useTimeline(id);
  const [filter, setFilter] = useState<string>('all');
  return (
    <QueryView q={q} empty={(l) => l.length === 0 ? <EmptyState icon="time-outline" title="Nothing recorded yet" /> : null}>
      {(items) => (
        <>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 6, marginBottom: 12 }}>
            {['all', 'vitals', 'lab', 'dose', 'note', 'alert', 'prescription', 'file', 'bed'].map((f) => <Chip key={f} label={f === 'all' ? 'All' : f[0].toUpperCase() + f.slice(1)} selected={filter === f} onPress={() => setFilter(f)} />)}
          </ScrollView>
          {items.filter((i) => filter === 'all' || i.type === filter).map((i, k, arr) => {
            const tone = i.severity === 'Critical' || i.severity === 'High' || i.severity === 'Missed' ? t.colors.critical
              : i.severity === 'Warning' || i.severity === 'Medium' || i.severity === 'LowMedium' || i.severity === 'Low' && i.type === 'lab' ? t.colors.watch : t.colors.primary;
            const showDay = k === 0 || dayMonth(arr[k - 1].at) !== dayMonth(i.at);
            return (
              <View key={`${i.type}-${i.refId}`}>
                {showDay ? <T v="label" color="muted" style={{ marginTop: k ? 14 : 0, marginBottom: 6 }}>{dayMonth(i.at)}</T> : null}
                <Row gap={12} style={{ alignItems: 'flex-start', paddingVertical: 8 }}>
                  <View style={{ width: 34, height: 34, borderRadius: 17, backgroundColor: t.colors.surfaceAlt, alignItems: 'center', justifyContent: 'center' }}>
                    <Ionicons name={TL_ICON[i.type] ?? 'ellipse'} size={16} color={tone} />
                  </View>
                  <View style={{ flex: 1, gap: 2 }}>
                    <Row><T v="bodyStrong" style={{ flex: 1 }}>{i.title}</T><T v="caption" color="faint">{time(i.at)}</T></Row>
                    {i.detail ? <T color="muted" numberOfLines={4}>{i.detail}</T> : null}
                    <Row gap={6} wrap>
                      {i.actor ? <T v="caption" color="faint">{i.actor}</T> : null}
                      {i.source === 'Voice' ? <T v="caption" color="primary">· voice</T> : null}
                      {i.source === 'Photo' ? <T v="caption" color="primary">· photo</T> : null}
                      {i.signedOff ? <T v="caption" color="stable">· Reviewed by {i.signedOffByName}</T> : i.signedOff === false ? <T v="caption" color="watch">· awaiting review</T> : null}
                    </Row>
                  </View>
                </Row>
              </View>
            );
          })}
        </>
      )}
    </QueryView>
  );
}

// ---------------- Vitals & trends ----------------
export function VitalsTab({ a }: { a: AdmissionHeader }) {
  const t = useTheme();
  const q = useVitals(a.id);
  return (
    <QueryView q={q} skeleton={<SkeletonList rows={3} height={170} />} empty={(l) => l.length === 0 ? <EmptyState icon="pulse-outline" title="No vitals yet" /> : null}>
      {(v) => {
        const last = v[v.length - 1];
        const pts = (f: (x: (typeof v)[number]) => number | null) => v.map((x) => ({ x: x.recordedAt, y: f(x) }));
        return (
          <>
            <Card style={{ marginBottom: 12 }}>
              <Row>
                <T v="label" color="muted" style={{ flex: 1 }}>LATEST · {dateTime(last.recordedAt)}</T>
                {a.news2Applicable ? <News2Badge score={last.news2Score} band={last.news2Band} size="sm" /> : null}
              </Row>
              <Row wrap gap={18} style={{ marginTop: 10 }}>
                <Stat label="BP" value={last.systolic ? `${last.systolic}/${last.diastolic ?? '?'}` : '—'} />
                <Stat label="Pulse" value={num(last.pulse)} />
                <Stat label="RR" value={num(last.respRate)} />
                <Stat label="Temp °C" value={num(last.temperatureC)} />
                <Stat label="SpO2" value={last.spO2 ? `${last.spO2}%${last.onOxygen ? ' O₂' : ''}` : '—'} />
              </Row>
              {last.signedOffByName ? <T v="caption" color="stable" style={{ marginTop: 8 }}>Reviewed by {last.signedOffByName}</T> : null}
            </Card>
            {a.news2Applicable ? <TrendChart title="NEWS2" points={pts((x) => x.news2Score)} normal={[0, 4]} color={t.colors.critical} /> : <Notice message="NEWS2 is not used under 16 years — vitals shown without a score." />}
            <TrendChart title="Pulse" unit="/min" points={pts((x) => x.pulse)} normal={[51, 90]} />
            <TrendChart title="Systolic BP" unit="mmHg" points={pts((x) => x.systolic)} normal={[111, 219]} color={t.colors.info} />
            <TrendChart title="SpO2" unit="%" points={pts((x) => x.spO2)} normal={[96, 100]} color={t.colors.stable} />
            <TrendChart title="Respiration rate" unit="/min" points={pts((x) => x.respRate)} normal={[12, 20]} color={t.colors.watch} />
            <TrendChart title="Temperature" unit="°C" points={pts((x) => x.temperatureC)} normal={[36.1, 38]} decimals={1} color="#9333EA" />
          </>
        );
      }}
    </QueryView>
  );
}
function Stat({ label, value }: { label: string; value: string }) {
  return <View><T v="caption" color="muted">{label}</T><T v="title">{value}</T></View>;
}

// ---------------- Labs (compare across dates) ----------------
export function LabsTab({ id }: { id: number }) {
  const t = useTheme();
  const q = useLabs(id);
  const [chart, setChart] = useState<string | null>(null);
  return (
    <QueryView q={q} empty={(l) => l.results.length === 0 ? <EmptyState icon="flask-outline" title="No results yet" message="Record results or photograph a report." /> : null}>
      {({ table, results }) => (
        <>
          <ScrollView horizontal showsHorizontalScrollIndicator>
            <View>
              <Row gap={0} style={{ borderBottomWidth: 1, borderColor: t.colors.border }}>
                <View style={{ width: 130, padding: 8 }}><T v="label" color="muted">Test</T></View>
                {table.dates.map((d) => <View key={d} style={{ width: 86, padding: 8 }}><T v="label" color="muted">{dayMonth(d + 'T06:00:00Z')}</T></View>)}
              </Row>
              {table.rows.map((r) => (
                <Pressable key={r.testName} onPress={() => setChart(chart === r.testName ? null : r.testName)}>
                  <Row gap={0} style={{ borderBottomWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border, minHeight: 52 }}>
                    <View style={{ width: 130, padding: 8 }}>
                      <Row gap={4}>
                        <T v="bodyStrong" numberOfLines={1} style={{ flexShrink: 1 }}>{r.testName}</T>
                        {r.trend === 'down' ? <Ionicons name="arrow-down" size={14} color={t.colors.watch} /> : r.trend === 'up' ? <Ionicons name="arrow-up" size={14} color={t.colors.watch} /> : null}
                      </Row>
                      <T v="caption" color="faint">{r.unit} {r.refLow != null ? `${fmtLab(r.testName, r.refLow)}–${fmtLab(r.testName, r.refHigh)}` : ''}</T>
                    </View>
                    {r.cells.map((c, i) => {
                      const abn = c && c.flag !== 'Normal';
                      const crit = c?.flag === 'Critical';
                      return (
                        <View key={i} style={{ width: 86, padding: 8 }}>
                          {c ? (
                            <View style={{ alignSelf: 'flex-start', paddingHorizontal: 6, paddingVertical: 2, borderRadius: 6, backgroundColor: crit ? t.colors.critical : abn ? t.colors.watchSoft : 'transparent' }}>
                              <T v="bodyStrong" color={crit ? '#fff' : abn ? t.colors.watch : undefined}>{c.value != null ? fmtLab(r.testName, c.value) : c.textValue}</T>
                            </View>
                          ) : <T color="faint">·</T>}
                        </View>
                      );
                    })}
                  </Row>
                </Pressable>
              ))}
            </View>
          </ScrollView>
          <T v="caption" color="faint" style={{ marginVertical: 8 }}>Tap a test to chart it. Amber = outside reference, red = critical.</T>
          {chart ? <TrendChart title={chart} points={results.filter((x) => x.testName === chart && x.value != null).map((x) => ({ x: x.resultAt, y: x.value }))} decimals={chart === 'Platelets' ? 0 : 1}
            normal={(() => { const r = table.rows.find((x) => x.testName === chart); return r?.refLow != null && r.refHigh != null ? [r.refLow, r.refHigh] as [number, number] : undefined; })()} /> : null}
        </>
      )}
    </QueryView>
  );
}

// ---------------- Medications ----------------
export function MedsTab({ a }: { a: AdmissionHeader }) {
  const { user } = useAuth();
  const q = useDoses(a.id);
  const invalidate = useInvalidateAdmission();
  const { notify } = useRealtime();
  const [sheet, setSheet] = useState<DoseTask | null>(null);
  const isNurse = user?.role === 'Nurse';

  const record = async (task: DoseTask, status: string, reason?: string) => {
    const token = await requestScan('wristband', `Scan ${a.patientName}'s wristband to record ${task.drugName}`);
    if (!token) return;
    try {
      await api.post(`/api/doses/${task.id}/record`, { wristbandToken: token, status, reason: reason ?? null });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      notify({ kind: 'success', title: `${task.drugName} · ${status}`, message: `${a.bedNumber} ${a.patientName}` });
      invalidate(a.id); void q.refetch(); setSheet(null);
    } catch (e) {
      toastError(e, errorMessage(e) === 'Wrong patient' ? 'Wrong patient — dose NOT recorded' : 'Dose not recorded');
    }
  };
  const prn = async (itemId: number, name: string) => {
    const token = await requestScan('wristband', `Scan the wristband to give ${name}`);
    if (!token) return;
    try { await api.post(`/api/prescription-items/${itemId}/prn-dose`, { wristbandToken: token, reason: null }); notify({ kind: 'success', title: `${name} given (PRN)` }); invalidate(a.id); void q.refetch(); }
    catch (e) { toastError(e, 'Not recorded'); }
  };

  return (
    <QueryView q={q} empty={(d) => d.doses.length === 0 && d.prnItems.length === 0 ? <EmptyState icon="medkit-outline" title="No medicines today" message="The consultant's signed prescriptions appear here." /> : null}>
      {(d) => (
        <>
          {d.doses.map((t) => (
            <Card key={t.id} style={{ marginBottom: 8 }} onPress={isNurse && ['Scheduled', 'Due', 'Delayed', 'Missed'].includes(t.status) ? () => setSheet(t) : undefined}>
              <Row>
                <View style={{ width: 54 }}><T v="heading">{time(t.dueAt)}</T></View>
                <View style={{ flex: 1 }}>
                  <T v="bodyStrong">{t.form ? `${t.form}. ` : ''}{t.drugName} {t.dose}</T>
                  <T v="caption" color="muted">{t.route} · {t.frequency}{t.instructions ? ` · ${t.instructions}` : ''}</T>
                  {t.doneByName ? <T v="caption" color="faint">{t.status} by {t.doneByName} {time(t.doneAt)}{t.reason ? ` · ${t.reason}` : ''}</T> : null}
                </View>
                <View style={{ alignItems: 'flex-end', gap: 4 }}>
                  <StatusPill status={t.status} kind="dose" />
                  {['Scheduled', 'Due', 'Delayed'].includes(t.status) ? <T v="caption" color={t.isOverdue ? 'critical' : 'muted'}>{dueLabel(t.minutesFromDue, t.status)}</T> : null}
                </View>
              </Row>
            </Card>
          ))}
          {d.prnItems.length ? <T v="label" color="muted" style={{ marginTop: 12, marginBottom: 6 }}>AS NEEDED (PRN)</T> : null}
          {d.prnItems.map((p) => (
            <Card key={p.id} style={{ marginBottom: 8 }}>
              <Row>
                <View style={{ flex: 1 }}><T v="bodyStrong">{p.summary}</T></View>
                {isNurse ? <Button title="Give" size="sm" icon="qr-code-outline" onPress={() => void prn(p.id, p.drugName)} /> : null}
              </Row>
            </Card>
          ))}
          <DoseSheet task={sheet} onClose={() => setSheet(null)} onRecord={(s, r) => sheet && void record(sheet, s, r)} />
        </>
      )}
    </QueryView>
  );
}

export function DoseSheet({ task, onClose, onRecord }: { task: DoseTask | null; onClose: () => void; onRecord: (status: string, reason?: string) => void }) {
  const t = useTheme();
  const [status, setStatus] = useState('Given');
  const [reason, setReason] = useState('');
  return (
    <BottomSheet visible={task != null} onClose={onClose} title={task ? `${task.drugName} ${task.dose}` : ''}>
      {task ? (
        <>
          <T color="muted">{task.route} · {task.frequency} · due {time(task.dueAt)} ({dueLabel(task.minutesFromDue, task.status)})</T>
          <Row wrap gap={8} style={{ marginVertical: 14 }}>
            {[['Given', t.colors.stable], ['Delayed', t.colors.watch], ['Refused', t.colors.watch], ['Withheld', t.colors.watch]].map(([s, c]) => (
              <Chip key={s} label={s} selected={status === s} tone={c} onPress={() => setStatus(s)} />
            ))}
          </Row>
          {status !== 'Given' ? <TextField label="Reason (required)" value={reason} onChangeText={setReason} placeholder="e.g. vomiting, NPO for endoscopy" /> : null}
          <Button title="Scan wristband & confirm" icon="qr-code-outline" size="lg" disabled={status !== 'Given' && !reason.trim()} onPress={() => { onRecord(status, reason.trim() || undefined); setReason(''); setStatus('Given'); }} />
        </>
      ) : null}
    </BottomSheet>
  );
}

// ---------------- Files (gallery via short-lived SAS URLs) ----------------
export function FilesTab({ id, canUpload }: { id: number; canUpload: boolean }) {
  const t = useTheme();
  const { width } = useWindowDimensions();
  const q = useFiles(id);
  const [open, setOpen] = useState<FileItem | null>(null);
  const [caption, setCaption] = useState('');
  const [busy, setBusy] = useState(false);
  const size = (Math.min(width, 700) - 32 - 20) / 3;

  const upload = async (fromCamera: boolean) => {
   
    const perm = fromCamera ? await ImagePicker.requestCameraPermissionsAsync() : await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!perm.granted) { toast.warning('Permission needed', 'Allow photo access to add a progress photo.'); return; }
    const res = fromCamera ? await ImagePicker.launchCameraAsync({ quality: 0.7, mediaTypes: ['images'] }) : await ImagePicker.launchImageLibraryAsync({ quality: 0.7, mediaTypes: ['images'] });
    if (res.canceled || !res.assets[0]) return;
    const asset = res.assets[0];
    const form = new FormData();
    form.append('file', filePart(asset.uri, asset.fileName ?? 'photo.jpg', asset.mimeType ?? 'image/jpeg'));
    form.append('kind', 'Progress');
    if (caption) form.append('caption', caption);
    setBusy(true);
    try { await api.post(`/api/admissions/${id}/files`, form, 90000); setCaption(''); void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success); void q.refetch(); }
    catch (e) { toastError(e); } finally { setBusy(false); }
  };

  return (
    <>
      {canUpload ? (
        <Card style={{ marginBottom: 12 }}>
          <T v="label" color="muted" style={{ marginBottom: 8 }}>ADD PROGRESS IMAGE (wound, X-ray photo…)</T>
          <TextField placeholder="Caption, e.g. Wound day 3" value={caption} onChangeText={setCaption} />
          <Row gap={8}>
            <Button title="Camera" icon="camera-outline" onPress={() => void upload(true)} loading={busy} style={{ flex: 1 }} />
            <Button title="Gallery" icon="images-outline" variant="secondary" onPress={() => void upload(false)} disabled={busy} style={{ flex: 1 }} />
          </Row>
        </Card>
      ) : null}
      <QueryView q={q} empty={(l) => l.length === 0 ? <EmptyState icon="images-outline" title="No images yet" /> : null}>
        {(files) => (
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 10 }}>
            {files.map((f) => (
              <Pressable key={f.id} onPress={() => setOpen(f)} style={{ width: size }}>
                {f.url && f.contentType.startsWith('image/')
                  ? <Image source={{ uri: f.url }} style={{ width: size, height: size, borderRadius: 12, backgroundColor: t.colors.surfaceAlt }} />
                  : <View style={{ width: size, height: size, borderRadius: 12, backgroundColor: t.colors.surfaceAlt, alignItems: 'center', justifyContent: 'center' }}><Ionicons name="document-outline" size={28} color={t.colors.textFaint} /></View>}
                <T v="caption" numberOfLines={2} style={{ marginTop: 4 }}>{f.caption ?? f.kind}</T>
                <T v="caption" color="faint">{dayMonth(f.uploadedAt)}</T>
              </Pressable>
            ))}
          </View>
        )}
      </QueryView>
      <Modal visible={open != null} transparent animationType="fade" onRequestClose={() => setOpen(null)}>
        <View style={{ flex: 1, backgroundColor: 'rgba(0,0,0,0.92)', justifyContent: 'center' }}>
          {open?.url ? <Image source={{ uri: open.url }} style={{ width: '100%', height: '75%' }} resizeMode="contain" /> : <ErrorState message="Link expired or storage unavailable" />}
          <View style={{ padding: 20 }}>
            <T color="#fff" v="bodyStrong">{open?.caption ?? open?.kind}</T>
            <T color="#ccc" v="caption">{open?.uploadedByName} · {dateTime(open?.uploadedAt)} · link expires in ≤ 10 min</T>
          </View>
          <View style={{ position: 'absolute', top: 40, right: 12 }}><IconButton icon="close" label="Close" color="#fff" onPress={() => setOpen(null)} /></View>
        </View>
      </Modal>
    </>
  );
}

// ---------------- AI progress comparison ----------------
export function AiTab({ a }: { a: AdmissionHeader }) {
  const t = useTheme();
  const [enabled, setEnabled] = useState(false);
  const q = useAiProgress(a.id, enabled);
  const journey = useJourney(a.id);
  const dir = (d: string) => d === 'improving' ? { icon: 'trending-up' as IconName, c: t.colors.stable } : d === 'worsening' ? { icon: 'trending-down' as IconName, c: t.colors.critical } : { icon: 'remove' as IconName, c: t.colors.textMuted };
  return (
    <>
      {!enabled ? (
        <Card>
          <T v="heading">AI progress comparison</T>
          <T color="muted" style={{ marginVertical: 8 }}>Compares the latest vitals and results with previous ones. Directions are computed by coded rules; the AI writes the summary. Never a diagnosis.</T>
          <Button title="Compare progress" icon="sparkles-outline" onPress={() => setEnabled(true)} />
        </Card>
      ) : q.isLoading ? <SkeletonList rows={3} height={90} /> : q.error ? (
        <ErrorState message={errorMessage(q.error)} onRetry={() => void q.refetch()} />
      ) : q.data ? (
        <>
          <Card accent={q.data.overallTrajectory === 'worsening' ? t.colors.critical : q.data.overallTrajectory === 'improving' ? t.colors.stable : t.colors.watch}>
            <Row><T v="label" color="muted" style={{ flex: 1 }}>OVERALL · {q.data.overallTrajectory.toUpperCase()}</T><T v="caption" color="faint">{q.data.cached ? 'cached · ' : ''}{time(q.data.generatedAt)}</T></Row>
            <T style={{ marginTop: 6 }}>{q.data.summary}</T>
            {q.data.watchFor.length ? <View style={{ marginTop: 10, gap: 4 }}>{q.data.watchFor.map((w, i) => <Row key={i} gap={6} style={{ alignItems: 'flex-start' }}><Ionicons name="eye-outline" size={15} color={t.colors.watch} /><T style={{ flex: 1 }}>{w}</T></Row>)}</View> : null}
          </Card>
          {q.data.parameters.map((p) => {
            const d = dir(p.direction);
            return (
              <Card key={`${p.group}-${p.name}`} style={{ marginTop: 8 }}>
                <Row>
                  <Ionicons name={d.icon} size={20} color={d.c} />
                  <T v="bodyStrong" style={{ flex: 1 }}>{p.name}</T>
                  <T v="bodyStrong" color={d.c}>{p.previous != null ? `${fmtLab(p.name, p.previous)} → ` : ''}{fmtLab(p.name, p.latest)} {p.unit}</T>
                </Row>
                {p.comment ? <T color="muted" style={{ marginTop: 4 }}>{p.comment}</T> : null}
              </Card>
            );
          })}
          <T v="caption" color="faint" style={{ marginTop: 10, textAlign: 'center' }}>{q.data.disclaimer} · {q.data.model}</T>
          <Button title="Refresh" variant="ghost" icon="refresh" onPress={() => void api.post(`/api/admissions/${a.id}/ai-progress?refresh=true`, {}, 60000).then(() => q.refetch())} />
        </>
      ) : null}
      {journey.data && journey.data.stays.length ? (
        <Card style={{ marginTop: 16 }}>
          <T v="label" color="muted" style={{ marginBottom: 8 }}>WARD JOURNEY</T>
          {journey.data.stays.map((s) => (
            <Row key={s.stayId} style={{ paddingVertical: 6 }}>
              <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: s.endAt ? t.colors.textFaint : t.colors.primary }} />
              <T v="bodyStrong" style={{ flex: 1 }}>{s.wardName} · {s.bedNumber}</T>
              <T v="caption" color="muted">{dateTime(s.startAt)} → {s.endAt ? dateTime(s.endAt) : 'now'}</T>
            </Row>
          ))}
        </Card>
      ) : null}
    </>
  );
}
