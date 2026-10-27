import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as Haptics from '@/lib/haptics';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import Animated, { FadeIn } from 'react-native-reanimated';
import { SafeAreaView } from 'react-native-safe-area-context';
import { api } from '@/api/client';
import type { AdmissionHeader, ScanResolve, ScanTokens } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { BottomSheet } from '@/components/clinical';
import { Button, Notice, Row, T, TextField } from '@/components/ui';
import { completeScan, hasPendingScan } from '@/lib/scan';
import { useTheme } from '@/theme';
import { toastError } from '@/components/Toast';

/**
 * Scanner.
 * - mode=request: another screen awaits a token (requestScan) — return it.
 * - mode=open (floating Scan button): resolve; a bed → scan wristband to confirm admission, or open the bed;
 *   a wristband → open the patient (nurses can also start a transfer by scanning a new bed).
 */
export default function Scan() {
  const t = useTheme();
  const { user } = useAuth();
  const params = useLocalSearchParams<{ mode?: string; purpose?: string; prompt?: string }>();
  const requestMode = params.mode === 'request';
  const [permission, requestPermission] = useCameraPermissions();
  const [step, setStep] = useState<{ prompt: string; bedToken?: string; wristband?: string; transfer?: boolean }>({ prompt: params.prompt ?? 'Scan a bed or wristband QR code' });
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: 'info' | 'critical' | 'success'; text: string } | null>(null);
  const [manual, setManual] = useState(false);
  const [code, setCode] = useState('');
  const [devTokens, setDevTokens] = useState<ScanTokens | null>(null);
  const lock = useRef(false);

  useEffect(() => () => { if (requestMode && hasPendingScan()) completeScan(null); }, [requestMode]);

  const close = () => { if (requestMode) completeScan(null); router.back(); };

  const handle = async (raw: string) => {
    if (lock.current || busy) return;
    lock.current = true;
    void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
    const token = raw.trim();
    try {
      if (requestMode) { completeScan(token); router.back(); return; }
      setBusy(true); setMessage(null);
      // Second scan of a two-step flow
      if (step.bedToken) {
        const a = await api.post<AdmissionHeader>('/api/scan/confirm-bed', { bedToken: step.bedToken, wristbandToken: token });
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        router.replace(`/patient/${a.id}`);
        return;
      }
      if (step.transfer && step.wristband) {
        const a = await api.post<AdmissionHeader>('/api/scan/transfer', { wristbandToken: step.wristband, bedToken: token });
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        router.replace(`/patient/${a.id}`);
        return;
      }
      const r = await api.get<ScanResolve>(`/api/scan/resolve?token=${encodeURIComponent(token)}`);
      if (r.kind === 'bed' && r.bed) {
        if (r.bed.status === 'Reserved' && r.bed.occupant && user?.role === 'Nurse') {
          setStep({ prompt: `Bed ${r.bed.bedNumber} is reserved for ${r.bed.occupant.patientName}. Now scan the patient's wristband.`, bedToken: token });
          setMessage({ tone: 'info', text: `Bed ${r.bed.bedNumber} ✓` });
        } else if (r.admission) router.replace(`/patient/${r.admission.id}`);
        else router.replace(`/bed/${r.bed.id}`);
      } else if (r.admission) {
        if (step.transfer) {
          setStep({ prompt: `Transfer ${r.admission.patientName}: scan the NEW bed's QR code`, wristband: token, transfer: true });
          setMessage({ tone: 'info', text: `Wristband ${r.admission.patientName} ✓` });
        } else router.replace(`/patient/${r.admission.id}`);
      }
    } catch (e) {
      setMessage(null);
      toastError(e, 'Scan not accepted');
    } finally {
      setBusy(false);
      setTimeout(() => { lock.current = false; }, 1200);
    }
  };

  const loadDev = async () => {
    try { setDevTokens(await api.get<ScanTokens>('/api/dev/scan-tokens')); } catch (e) { toastError(e); }
  };

  return (
    <View style={{ flex: 1, backgroundColor: '#000' }}>
      {permission?.granted ? (
        <CameraView style={StyleSheet.absoluteFill} facing="back" barcodeScannerSettings={{ barcodeTypes: ['qr'] }}
          onBarcodeScanned={busy ? undefined : (r) => void handle(r.data)} />
      ) : null}
      <SafeAreaView style={{ flex: 1 }}>
        <Row style={{ padding: 12 }}>
          <Pressable onPress={close} hitSlop={12} accessibilityLabel="Close scanner" style={{ width: 48, height: 48, borderRadius: 24, backgroundColor: 'rgba(0,0,0,0.5)', alignItems: 'center', justifyContent: 'center' }}>
            <Ionicons name="close" size={26} color="#fff" />
          </Pressable>
          <View style={{ flex: 1 }} />
          {!requestMode && user?.role === 'Nurse' && !step.bedToken ? (
            <Pressable onPress={() => setStep(step.transfer ? { prompt: 'Scan a bed or wristband QR code' } : { prompt: 'Transfer: scan the patient wristband first', transfer: true })}
              style={{ paddingHorizontal: 14, height: 44, borderRadius: 22, backgroundColor: step.transfer ? t.colors.watch : 'rgba(0,0,0,0.5)', flexDirection: 'row', alignItems: 'center', gap: 6 }}>
              <Ionicons name="swap-horizontal" size={18} color="#fff" /><T v="label" color="#fff">{step.transfer ? 'Transfer mode' : 'Transfer'}</T>
            </Pressable>
          ) : null}
        </Row>

        <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
          {permission?.granted ? (
            <View style={{ width: 260, height: 260, borderRadius: 28, borderWidth: 4, borderColor: busy ? t.colors.watch : '#fff' }} />
          ) : (
            <View style={{ alignItems: 'center', gap: 12, padding: 24 }}>
              <Ionicons name="camera-outline" size={48} color="#fff" />
              <T color="#fff" style={{ textAlign: 'center' }}>Camera access is needed to scan bed and wristband QR codes.</T>
              <Button title="Allow camera" onPress={() => void requestPermission()} />
            </View>
          )}
        </View>

        <Animated.View entering={FadeIn} style={{ margin: 16, padding: 16, borderRadius: 20, backgroundColor: t.colors.surface, gap: 10 }}>
          <T v="heading">{step.prompt}</T>
          {message ? <Notice tone={message.tone} message={message.text} /> : null}
          <Row gap={8}>
            <Button title="Type code" icon="keypad-outline" variant="secondary" size="sm" onPress={() => setManual(true)} />
            {__DEV__ ? <Button title="Dev: pick code" icon="bug-outline" variant="ghost" size="sm" onPress={() => void loadDev()} /> : null}
          </Row>
        </Animated.View>
      </SafeAreaView>

      <BottomSheet visible={manual} onClose={() => setManual(false)} title="Enter code">
        <T color="muted" style={{ marginBottom: 12 }}>Type the code printed under the QR (for a damaged label).</T>
        <TextField value={code} onChangeText={setCode} autoCapitalize="none" autoCorrect={false} placeholder="e.g. 3f9a…" />
        <Button title="Use code" onPress={() => { setManual(false); void handle(code); setCode(''); }} disabled={code.trim().length < 6} />
      </BottomSheet>

      <BottomSheet visible={devTokens != null} onClose={() => setDevTokens(null)} title="Simulate a scan (dev)">
        <T color="muted" style={{ marginBottom: 8 }}>For emulators without a camera. Development builds only.</T>
        <T v="label" color="muted" style={{ marginVertical: 6 }}>WRISTBANDS</T>
        {devTokens?.wristbands.map((w) => <DevRow key={w.token} label={w.label} onPress={() => { setDevTokens(null); void handle(w.token); }} />)}
        <T v="label" color="muted" style={{ marginVertical: 6 }}>BEDS</T>
        {devTokens?.beds.map((b) => <DevRow key={b.token} label={b.label} onPress={() => { setDevTokens(null); void handle(b.token); }} />)}
      </BottomSheet>
    </View>
  );
}

function DevRow({ label, onPress }: { label: string; onPress: () => void }) {
  const t = useTheme();
  return (
    <Pressable onPress={onPress} style={({ pressed }) => ({ minHeight: 48, justifyContent: 'center', borderBottomWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border, opacity: pressed ? 0.6 : 1 })}>
      <T>{label}</T>
    </Pressable>
  );
}
