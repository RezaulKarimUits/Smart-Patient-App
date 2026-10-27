import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import * as ImagePicker from 'expo-image-picker';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Image, View } from 'react-native';
import { api, filePart } from '@/api/client';
import { useAdmission, useInvalidateAdmission } from '@/api/hooks';
import type { LabsSaveResponse, ReportExtract } from '@/api/types';
import { ConfirmCard } from '@/components/clinical';
import { Button, Card, IconButton, Notice, Row, Screen, Spacer, StatusPill, T, TextField } from '@/components/ui';
import { fmtLab, parseNum } from '@/lib/format';
import { useTheme } from '@/theme';
import { toast, toastError } from '@/components/Toast';

type Row_ = { testName: string; value: number | null; unit: string | null; refLow: number | null; refHigh: number | null; flag: string };

/** Photograph a lab report → AI extracts values → review/edit → confirm saves results and runs monitoring. */
export default function ReportPhoto() {
  const t = useTheme();
  const { id } = useLocalSearchParams<{ id: string }>();
  const admissionId = Number(id);
  const header = useAdmission(admissionId);
  const invalidate = useInvalidateAdmission();
  const [photo, setPhoto] = useState<string | null>(null);
  const [extract, setExtract] = useState<ReportExtract | null>(null);
  const [rows, setRows] = useState<Row_[]>([]);
  const [busy, setBusy] = useState<'extract' | 'save' | null>(null);
  const [saved, setSaved] = useState<LabsSaveResponse | null>(null);

  const pick = async (camera: boolean) => {
   
    const perm = camera ? await ImagePicker.requestCameraPermissionsAsync() : await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!perm.granted) { toast.warning('Permission needed', 'Allow camera or photo access to add the report photo.'); return; }
    const r = camera ? await ImagePicker.launchCameraAsync({ quality: 0.8, mediaTypes: ['images'] }) : await ImagePicker.launchImageLibraryAsync({ quality: 0.8, mediaTypes: ['images'] });
    if (r.canceled || !r.assets[0]) return;
    const a = r.assets[0];
    setPhoto(a.uri); setBusy('extract');
    try {
      const form = new FormData();
      form.append('image', filePart(a.uri, a.fileName ?? 'report.jpg', a.mimeType ?? 'image/jpeg'));
      const res = await api.post<ReportExtract>(`/api/admissions/${admissionId}/reports/extract`, form, 120000);
      setExtract(res);
      setRows(res.results.map((x) => ({ testName: x.testName, value: x.value, unit: x.unit, refLow: x.refLow, refHigh: x.refHigh, flag: x.flag })));
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (e) { toastError(e); } finally { setBusy(null); }
  };

  const confirm = async () => {
    if (!extract) return;
    setBusy('save');
    try {
      const r = await api.post<LabsSaveResponse>(`/api/admissions/${admissionId}/reports/confirm`, { fileId: extract.file.id,
        results: rows.filter((x) => x.testName && x.value != null).map((x) => ({ testName: x.testName, value: x.value, unit: x.unit, refLow: x.refLow, refHigh: x.refHigh })) });
      setSaved(r); invalidate(admissionId);
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (e) { toastError(e); } finally { setBusy(null); }
  };

  const title = header.data ? `Report · ${header.data.bedNumber}` : 'Report photo';
  if (saved) {
    return (
      <Screen>
        <Stack.Screen options={{ title }} />
        <Card style={{ alignItems: 'center', gap: 8 }}>
          <Ionicons name="checkmark-circle" size={52} color={t.colors.stable} />
          <T v="title">{saved.results.length} results saved</T>
          <T color="muted">Linked to the report photo for audit.</T>
        </Card>
        {saved.notes.map((n, i) => <Notice key={i} message={n} />)}
        {saved.monitoring.newAlerts.map((a) => <Notice key={a.id} tone={a.severity === 'Critical' ? 'critical' : 'warning'} title={`${a.severity} alert — consultant notified`} message={a.ruleDetail} />)}
        <Spacer />
        <Button title="Back to patient" size="lg" onPress={() => router.back()} />
      </Screen>
    );
  }

  return (
    <Screen>
      <Stack.Screen options={{ title }} />
      {!extract ? (
        <>
          {photo ? <Image source={{ uri: photo }} style={{ width: '100%', height: 260, borderRadius: 16, marginBottom: 12 }} resizeMode="cover" /> : (
            <Card style={{ alignItems: 'center', gap: 10, paddingVertical: 32 }}>
              <Ionicons name="document-text-outline" size={48} color={t.colors.primary} />
              <T v="heading">Photograph the lab report</T>
              <T color="muted" style={{ textAlign: 'center' }}>Flat, in good light, the whole results table visible. The photo is stored for audit; values are saved only after you confirm.</T>
            </Card>
          )}
          <Spacer />
          <Row gap={8}>
            <Button title="Take photo" icon="camera" size="lg" style={{ flex: 1 }} loading={busy === 'extract'} onPress={() => void pick(true)} />
            <Button title="Gallery" icon="images-outline" variant="secondary" size="lg" style={{ flex: 1 }} disabled={busy != null} onPress={() => void pick(false)} />
          </Row>
          {busy === 'extract' ? <T color="muted" style={{ textAlign: 'center', marginTop: 12 }}>Reading the report…</T> : null}
        </>
      ) : (
        <>
          <Row gap={12} style={{ marginBottom: 12 }}>
            {extract.file.url || photo ? <Image source={{ uri: extract.file.url ?? photo! }} style={{ width: 84, height: 110, borderRadius: 10 }} /> : null}
            <View style={{ flex: 1 }}>
              <T v="heading">Check the extracted values</T>
              <T color="muted">Edit anything that doesn't match the report.{extract.reportDate ? ` Report date ${extract.reportDate}.` : ''}</T>
            </View>
          </Row>
          {extract.clarificationQuestion ? <Notice tone="warning" title="Unclear" message={extract.clarificationQuestion} /> : null}
          <ConfirmCard title="Results" icon="flask" issues={extract.warnings}>
            {rows.map((r, i) => (
              <View key={i} style={{ borderBottomWidth: 1, borderColor: t.colors.border, paddingVertical: 6 }}>
                <Row gap={8}>
                  <TextField value={r.testName} onChangeText={(s) => setRows(rows.map((x, k) => k === i ? { ...x, testName: s } : x))} style={{ flex: 2 }} />
                  <TextField value={r.value == null ? '' : String(r.value)} keyboardType="decimal-pad" onChangeText={(s) => setRows(rows.map((x, k) => k === i ? { ...x, value: parseNum(s) } : x))} style={{ flex: 1.3 }} />
                  <IconButton icon="close" label="Remove row" onPress={() => setRows(rows.filter((_, k) => k !== i))} />
                </Row>
                <Row gap={8}>
                  <T v="caption" color="muted" style={{ flex: 1 }}>{r.unit ?? ''} {r.refLow != null ? `ref ${fmtLab(r.testName, r.refLow)}–${fmtLab(r.testName, r.refHigh)}` : ''}</T>
                  {r.flag !== 'Normal' ? <StatusPill status={r.flag === 'Critical' ? 'Critical' : 'Watch'} /> : null}
                </Row>
              </View>
            ))}
            <Button title="Add row" icon="add" variant="ghost" size="sm" onPress={() => setRows([...rows, { testName: '', value: null, unit: null, refLow: null, refHigh: null, flag: 'Normal' }])} />
          </ConfirmCard>
          <Button title="Confirm & save results" icon="checkmark-circle" size="lg" loading={busy === 'save'} disabled={!rows.some((r) => r.testName && r.value != null)} onPress={() => void confirm()} />
          <Button title="Retake" variant="ghost" onPress={() => { setExtract(null); setPhoto(null); }} />
        </>
      )}
    </Screen>
  );
}
