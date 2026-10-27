import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import { router } from 'expo-router';
import { useEffect, type ReactNode } from 'react';
import { Modal, Pressable, ScrollView, StyleSheet, Text, View, useWindowDimensions } from 'react-native';
import Animated, { Easing, FadeIn, FadeOut, SlideInDown, SlideOutDown, SlideInUp, SlideOutUp, useAnimatedStyle, useSharedValue, withRepeat, withTiming, cancelAnimation } from 'react-native-reanimated';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import Svg, { Circle, Line, Path, Rect, Text as SvgText } from 'react-native-svg';
import type { AdmissionHeader, BedTile as BedTileDto, Issue } from '@/api/types';
import { dayMonth, relative, time } from '@/lib/format';
import { useRealtime } from '@/realtime/RealtimeProvider';
import { flagColors, useTheme } from '@/theme';
import { Badge, IconButton, News2Badge, Notice, Row, StatusPill, T, type IconName } from './ui';

// ---------- Bed tile ----------
export function BedTile({ bed, onPress, compact }: { bed: BedTileDto; onPress?: () => void; compact?: boolean }) {
  const t = useTheme();
  const c = t.colors.bed[bed.status];
  const o = bed.occupant;
  const ring = o?.flag === 'Critical' ? t.colors.critical : o?.flag === 'Watch' ? t.colors.watch : null;
  return (
    <Pressable onPress={onPress} accessibilityLabel={`Bed ${bed.bedNumber} ${bed.status}${o ? ` ${o.patientName}` : ''}`}
      style={({ pressed }) => [{
        flex: 1, minHeight: compact ? 92 : 112, borderRadius: t.radius.lg, padding: 12, backgroundColor: o ? t.colors.surface : c.bg,
        borderLeftWidth: 5, borderLeftColor: c.fg, borderWidth: ring ? 2 : StyleSheet.hairlineWidth, borderColor: ring ?? t.colors.border,
        opacity: pressed ? 0.85 : 1, ...t.shadow.card,
      }]}>
      <Row>
        <T v="heading">{bed.bedNumber}</T>
        <View style={{ flex: 1 }} />
        {o && o.news2Score != null ? <News2Badge score={o.news2Score} band={o.news2Band} size="sm" /> : <StatusPill status={bed.status} kind="bed" />}
      </Row>
      {o ? (
        <View style={{ marginTop: 6, gap: 3 }}>
          <T v="bodyStrong" numberOfLines={1}>{o.patientName}</T>
          <T v="caption" color="muted" numberOfLines={1}>{o.ageYears}y {o.sex} · {o.consultantName}</T>
          <Row gap={6} style={{ marginTop: 2 }} wrap>
            {o.awaitingScan ? <StatusPill status="Reserved" kind="bed" /> : null}
            {o.flag !== 'Stable' ? <StatusPill status={o.flag} /> : null}
          </Row>
        </View>
      ) : (
        <T v="caption" color="muted" style={{ marginTop: 8 }} numberOfLines={2}>{bed.lastNote ?? ''} {relative(bed.statusChangedAt)}</T>
      )}
    </Pressable>
  );
}

// ---------- Patient header ----------
export function PatientHeader({ a, right }: { a: AdmissionHeader; right?: ReactNode }) {
  const t = useTheme();
  const f = flagColors(t, a.flag);
  return (
    <View style={{ backgroundColor: t.colors.surface, padding: t.spacing.lg, borderBottomWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border,
      borderLeftWidth: a.flag !== 'Stable' ? 6 : 0, borderLeftColor: f.fg }}>
      <Row gap={12}>
        <News2Badge score={a.news2Score} band={a.news2Band} applicable={a.news2Applicable} size="lg" />
        <View style={{ flex: 1, gap: 2 }}>
          <T v="title" numberOfLines={1}>{a.patientName}</T>
          <T color="muted">{a.ageYears}y {a.sex} · {a.mrn}{a.bloodGroup ? ` · ${a.bloodGroup}` : ''}</T>
          <T color="muted" numberOfLines={1}>{a.wardName ?? '—'} · <T v="bodyStrong">{a.bedNumber ?? 'no bed'}</T> · Day {a.dayOfStay}</T>
        </View>
        {right}
      </Row>
      <Row gap={6} style={{ marginTop: 10 }} wrap>
        <StatusPill status={a.flag} />
        {a.activeAlerts ? <StatusPill status={`${a.activeAlerts} alert${a.activeAlerts > 1 ? 's' : ''}`} kind="severity" /> : null}
        {!a.bedConfirmed && a.status === 'Active' ? <StatusPill status="Reserved" kind="bed" /> : null}
        {a.status === 'Discharged' ? <StatusPill status="Discharged" kind="bed" /> : null}
        {!a.news2Applicable ? <T v="caption" color="faint">NEWS2 not used under 16</T> : null}
      </Row>
      {a.provisionalDiagnosis ? <T v="caption" color="muted" style={{ marginTop: 8 }} numberOfLines={2}>{a.provisionalDiagnosis} · {a.consultantName}</T> : null}
    </View>
  );
}

// ---------- Bottom sheet ----------
export function BottomSheet({ visible, onClose, title, children }: { visible: boolean; onClose: () => void; title?: string; children: ReactNode }) {
  const t = useTheme();
  const insets = useSafeAreaInsets();
  const { height } = useWindowDimensions();
  return (
    <Modal visible={visible} transparent animationType="none" onRequestClose={onClose} statusBarTranslucent>
      {visible ? (
        <View style={{ flex: 1, justifyContent: 'flex-end' }}>
          <Animated.View entering={FadeIn.duration(180)} exiting={FadeOut.duration(150)} style={[StyleSheet.absoluteFill, { backgroundColor: t.colors.overlay }]}>
            <Pressable style={{ flex: 1 }} onPress={onClose} accessibilityLabel="Close" />
          </Animated.View>
          <Animated.View entering={SlideInDown.duration(260).easing(Easing.out(Easing.cubic))} exiting={SlideOutDown.duration(200)}
            style={{ backgroundColor: t.colors.surface, borderTopLeftRadius: 24, borderTopRightRadius: 24, maxHeight: height * 0.88, paddingBottom: insets.bottom + 12, ...t.shadow.raised }}>
            <View style={{ alignItems: 'center', paddingTop: 10 }}><View style={{ width: 40, height: 5, borderRadius: 3, backgroundColor: t.colors.border }} /></View>
            {title ? <Row style={{ paddingHorizontal: 20, paddingTop: 8 }}><T v="title" style={{ flex: 1 }}>{title}</T><IconButton icon="close" label="Close" onPress={onClose} /></Row> : null}
            <ScrollView contentContainerStyle={{ padding: 20, paddingTop: 8 }} keyboardShouldPersistTaps="handled">{children}</ScrollView>
          </Animated.View>
        </View>
      ) : null}
    </Modal>
  );
}

// ---------- Confirm card (AI draft review) ----------
export function ConfirmCard({ title, icon, children, issues }: { title: string; icon: IconName; children: ReactNode; issues?: Issue[] }) {
  const t = useTheme();
  const blockers = issues?.filter((i) => i.level === 'blocker') ?? [];
  return (
    <View style={{ backgroundColor: t.colors.surface, borderRadius: t.radius.lg, padding: t.spacing.lg, marginBottom: 12,
      borderWidth: blockers.length ? 2 : StyleSheet.hairlineWidth, borderColor: blockers.length ? t.colors.critical : t.colors.border, ...t.shadow.card }}>
      <Row style={{ marginBottom: 10 }}>
        <View style={{ width: 32, height: 32, borderRadius: 10, backgroundColor: t.colors.primarySoft, alignItems: 'center', justifyContent: 'center' }}>
          <Ionicons name={icon} size={18} color={t.colors.primary} />
        </View>
        <T v="heading" style={{ flex: 1 }}>{title}</T>
      </Row>
      {issues?.map((i, k) => <Notice key={k} tone={i.level === 'blocker' ? 'critical' : 'warning'} message={i.message} />)}
      {children}
    </View>
  );
}

// ---------- Mic button (animated pulse) ----------
export function MicButton({ recording, onPress, seconds, max = 90, disabled, busy }: { recording: boolean; onPress: () => void; seconds: number; max?: number; disabled?: boolean; busy?: boolean }) {
  const t = useTheme();
  const pulse = useSharedValue(0);
  useEffect(() => {
    if (recording) pulse.value = withRepeat(withTiming(1, { duration: 1100, easing: Easing.out(Easing.quad) }), -1, false);
    else { cancelAnimation(pulse); pulse.value = 0; }
  }, [recording, pulse]);
  const ring = useAnimatedStyle(() => ({ transform: [{ scale: 1 + pulse.value * 0.55 }], opacity: recording ? 0.45 * (1 - pulse.value) : 0 }));
  const color = recording ? t.colors.critical : t.colors.primary;
  const mm = Math.floor(seconds / 60), ss = seconds % 60;
  return (
    <View style={{ alignItems: 'center', gap: 14 }}>
      <View style={{ width: 168, height: 168, alignItems: 'center', justifyContent: 'center' }}>
        <Animated.View style={[{ position: 'absolute', width: 150, height: 150, borderRadius: 75, backgroundColor: color }, ring]} />
        <Pressable accessibilityRole="button" accessibilityLabel={recording ? 'Stop recording' : 'Start recording'} disabled={disabled || busy}
          onPress={() => { void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium); onPress(); }}
          style={({ pressed }) => ({ width: 128, height: 128, borderRadius: 64, backgroundColor: color, alignItems: 'center', justifyContent: 'center',
            opacity: disabled ? 0.4 : pressed ? 0.85 : 1, transform: [{ scale: pressed ? 0.96 : 1 }], ...t.shadow.raised })}>
          <Ionicons name={busy ? 'hourglass' : recording ? 'stop' : 'mic'} size={54} color="#fff" />
        </Pressable>
      </View>
      <T v="title" style={{ fontVariant: ['tabular-nums'] }} color={recording ? 'critical' : undefined}>{`${mm}:${String(ss).padStart(2, '0')}`}</T>
      <T color="muted">{busy ? 'Transcribing and structuring…' : recording ? `Tap to stop · auto-stops at ${max} s` : 'Tap to start speaking'}</T>
    </View>
  );
}

// ---------- Floating scan button ----------
export function ScanButton() {
  const t = useTheme();
  const insets = useSafeAreaInsets();
  return (
    <Pressable accessibilityRole="button" accessibilityLabel="Scan QR code"
      onPress={() => { void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium); router.push({ pathname: '/scan', params: { mode: 'open' } }); }}
      style={({ pressed }) => ({ position: 'absolute', right: 18, bottom: insets.bottom + 72, width: 62, height: 62, borderRadius: 31, backgroundColor: t.colors.primary,
        alignItems: 'center', justifyContent: 'center', ...t.shadow.raised, transform: [{ scale: pressed ? 0.94 : 1 }] })}>
      <Ionicons name="qr-code" size={28} color={t.colors.onPrimary} />
    </Pressable>
  );
}

// ---------- Trend chart ----------
export type Point = { x: string; y: number | null; label?: string };
export function TrendChart({ title, points, unit, normal, color, height = 150, decimals = 0 }:
  { title: string; points: Point[]; unit?: string; normal?: [number, number]; color?: string; height?: number; decimals?: number }) {
  const t = useTheme();
  const { width: screen } = useWindowDimensions();
  const width = Math.min(screen - 64, 700);
  const valid = points.filter((p) => p.y != null) as (Point & { y: number })[];
  const stroke = color ?? t.colors.primary;
  if (valid.length === 0) return null;
  const ys = valid.map((p) => p.y).concat(normal ?? []);
  let min = Math.min(...ys), max = Math.max(...ys);
  if (max - min < 1e-6) { max += 1; min -= 1; }
  const padY = (max - min) * 0.15; min -= padY; max += padY;
  const left = 34, right = 10, top = 10, bottom = 22;
  const w = width - left - right, h = height - top - bottom;
  const times = valid.map((p) => new Date(p.x).getTime());
  const t0 = Math.min(...times), t1 = Math.max(...times);
  const X = (iso: string) => left + (t1 === t0 ? w / 2 : ((new Date(iso).getTime() - t0) / (t1 - t0)) * w);
  const Y = (v: number) => top + h - ((v - min) / (max - min)) * h;
  const d = valid.map((p, i) => `${i === 0 ? 'M' : 'L'}${X(p.x).toFixed(1)},${Y(p.y).toFixed(1)}`).join(' ');
  const last = valid[valid.length - 1];
  const fmt = (v: number) => v.toFixed(decimals);
  return (
    <View style={{ backgroundColor: t.colors.surface, borderRadius: t.radius.lg, padding: 14, marginBottom: 12, borderWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border }}>
      <Row style={{ marginBottom: 6 }}>
        <T v="label" color="muted" style={{ flex: 1 }}>{title}</T>
        <T v="heading" color={stroke}>{fmt(last.y)}</T><T v="caption" color="faint"> {unit}</T>
      </Row>
      <Svg width={width} height={height} accessibilityLabel={`${title} trend chart`}>
        {normal ? <Rect x={left} y={Y(normal[1])} width={w} height={Math.max(0, Y(normal[0]) - Y(normal[1]))} fill={t.colors.stableSoft} opacity={0.7} /> : null}
        {[min + padY, (min + max) / 2, max - padY].map((v, i) => (
          <SvgText key={i} x={left - 6} y={Y(v) + 4} fontSize={10} fill={t.colors.textFaint} textAnchor="end">{fmt(v)}</SvgText>
        ))}
        <Line x1={left} y1={top + h} x2={left + w} y2={top + h} stroke={t.colors.border} />
        <Path d={d} stroke={stroke} strokeWidth={2.5} fill="none" strokeLinejoin="round" strokeLinecap="round" />
        {valid.map((p, i) => <Circle key={i} cx={X(p.x)} cy={Y(p.y)} r={i === valid.length - 1 ? 4.5 : 2.5} fill={i === valid.length - 1 ? stroke : t.colors.surface} stroke={stroke} strokeWidth={1.5} />)}
        <SvgText x={left} y={height - 4} fontSize={10} fill={t.colors.textFaint}>{dayMonth(valid[0].x)} {time(valid[0].x)}</SvgText>
        <SvgText x={left + w} y={height - 4} fontSize={10} fill={t.colors.textFaint} textAnchor="end">{dayMonth(last.x)} {time(last.x)}</SvgText>
      </Svg>
    </View>
  );
}

// ---------- In-app banners (realtime alerts, dose reminders) ----------
export function Banners() {
  const t = useTheme();
  const insets = useSafeAreaInsets();
  const { banners, dismiss } = useRealtime();
  if (banners.length === 0) return null;
  return (
    <View pointerEvents="box-none" style={{ position: 'absolute', top: insets.top + 6, left: 12, right: 12, gap: 8, zIndex: 100 }}>
      {banners.map((b) => {
        const c = b.kind === 'critical' ? { fg: '#fff', bg: t.colors.critical, icon: 'warning' as IconName }
          : b.kind === 'warning' ? { fg: '#1F1300', bg: '#FBBF24', icon: 'alert-circle' as IconName }
          : b.kind === 'dose' ? { fg: '#fff', bg: t.colors.info, icon: 'medkit' as IconName }
          : b.kind === 'success' ? { fg: '#fff', bg: t.colors.stable, icon: 'checkmark-circle' as IconName }
          : { fg: t.colors.onPrimary, bg: t.colors.primary, icon: 'notifications' as IconName };
        return (
          <Animated.View key={b.id} entering={SlideInUp.duration(240)} exiting={SlideOutUp.duration(200)}>
            <Pressable onPress={() => { dismiss(b.id); if (b.admissionId) router.push(`/patient/${b.admissionId}`); }}
              style={{ flexDirection: 'row', gap: 10, padding: 14, borderRadius: 16, backgroundColor: c.bg, alignItems: 'center', ...t.shadow.raised }}>
              <Ionicons name={c.icon} size={22} color={c.fg} />
              <View style={{ flex: 1 }}>
                <Text style={{ color: c.fg, fontWeight: '800', fontSize: 15 }} numberOfLines={1}>{b.title}</Text>
                {b.message ? <Text style={{ color: c.fg, fontSize: 13, marginTop: 2 }} numberOfLines={3}>{b.message}</Text> : null}
              </View>
              <Pressable onPress={() => dismiss(b.id)} hitSlop={12}><Ionicons name="close" size={20} color={c.fg} /></Pressable>
            </Pressable>
          </Animated.View>
        );
      })}
    </View>
  );
}

export function LiveDot() {
  const t = useTheme();
  const { connected } = useRealtime();
  return (
    <Row gap={6}>
      <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: connected ? t.colors.stable : t.colors.textFaint }} />
      <T v="caption" color="muted">{connected ? 'Live' : 'Polling'}</T>
    </Row>
  );
}

export function CountTile({ label, value, icon, tone, onPress }: { label: string; value: number | string; icon: IconName; tone?: string; onPress?: () => void }) {
  const t = useTheme();
  return (
    <Pressable onPress={onPress} style={({ pressed }) => ({ flex: 1, minWidth: 140, backgroundColor: t.colors.surface, borderRadius: t.radius.lg, padding: 14, gap: 6,
      borderWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border, opacity: pressed ? 0.85 : 1, ...t.shadow.card })}>
      <Row><Ionicons name={icon} size={18} color={tone ?? t.colors.primary} /><View style={{ flex: 1 }} />{typeof value === 'number' && value > 0 && tone ? <Badge count={value} color={tone} /> : null}</Row>
      <T v="display">{value}</T>
      <T v="caption" color="muted">{label}</T>
    </Pressable>
  );
}
