import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import { useEffect, type ComponentProps, type ReactNode } from 'react';
import {
  ActivityIndicator, Pressable, RefreshControl, ScrollView, StyleSheet, Text, TextInput, View,
  type StyleProp, type TextInputProps, type TextStyle, type ViewStyle,
} from 'react-native';
import Animated, { FadeInDown, useAnimatedStyle, useSharedValue, withRepeat, withTiming } from 'react-native-reanimated';
import { SafeAreaView, type Edge } from 'react-native-safe-area-context';
import { flagColors, news2Colors, useTheme, type Theme } from '@/theme';

export type IconName = ComponentProps<typeof Ionicons>['name'];

// ---------- Text ----------
type Variant = keyof Theme['typography'];
export function T({ v = 'body', color, style, children, numberOfLines, selectable }:
  { v?: Variant; color?: 'muted' | 'faint' | 'primary' | 'critical' | 'watch' | 'stable' | 'onPrimary' | string; style?: StyleProp<TextStyle>; children: ReactNode; numberOfLines?: number; selectable?: boolean }) {
  const t = useTheme();
  const c = color === 'muted' ? t.colors.textMuted : color === 'faint' ? t.colors.textFaint : color === 'primary' ? t.colors.primary
    : color === 'critical' ? t.colors.critical : color === 'watch' ? t.colors.watch : color === 'stable' ? t.colors.stable
    : color === 'onPrimary' ? t.colors.onPrimary : color ?? t.colors.text;
  return <Text selectable={selectable} numberOfLines={numberOfLines} style={[t.typography[v], { color: c }, style]}>{children}</Text>;
}

// ---------- Layout ----------
export function Screen({ children, scroll = true, refreshing, onRefresh, edges = ['bottom'], padded = true, footer }:
  { children: ReactNode; scroll?: boolean; refreshing?: boolean; onRefresh?: () => void; edges?: Edge[]; padded?: boolean; footer?: ReactNode }) {
  const t = useTheme();
  const pad = padded ? { padding: t.spacing.lg, paddingBottom: 120 } : undefined;
  return (
    <SafeAreaView edges={edges} style={{ flex: 1, backgroundColor: t.colors.background }}>
      {scroll ? (
        <ScrollView contentContainerStyle={pad} keyboardShouldPersistTaps="handled"
          refreshControl={onRefresh ? <RefreshControl refreshing={!!refreshing} onRefresh={onRefresh} tintColor={t.colors.primary} colors={[t.colors.primary]} /> : undefined}>
          {children}
        </ScrollView>
      ) : <View style={[{ flex: 1 }, padded ? { padding: t.spacing.lg } : null]}>{children}</View>}
      {footer}
    </SafeAreaView>
  );
}

export function Row({ children, gap = 8, style, wrap }: { children: ReactNode; gap?: number; style?: StyleProp<ViewStyle>; wrap?: boolean }) {
  return <View style={[{ flexDirection: 'row', alignItems: 'center', gap, flexWrap: wrap ? 'wrap' : 'nowrap' }, style]}>{children}</View>;
}
export const Spacer = ({ h = 12 }: { h?: number }) => <View style={{ height: h }} />;
export const Grow = () => <View style={{ flex: 1 }} />;

export function Card({ children, style, onPress, accent, delay = 0 }: { children: ReactNode; style?: StyleProp<ViewStyle>; onPress?: () => void; accent?: string; delay?: number }) {
  const t = useTheme();
  const base: ViewStyle = { backgroundColor: t.colors.surface, borderRadius: t.radius.lg, padding: t.spacing.lg, borderWidth: StyleSheet.hairlineWidth, borderColor: t.colors.border, ...t.shadow.card };
  const accentStyle: ViewStyle | null = accent ? { borderLeftWidth: 5, borderLeftColor: accent } : null;
  const content = onPress ? (
    <Pressable onPress={onPress} style={({ pressed }) => [base, accentStyle, style, pressed && { opacity: 0.85, transform: [{ scale: 0.99 }] }]}>{children}</Pressable>
  ) : <View style={[base, accentStyle, style]}>{children}</View>;
  return <Animated.View entering={FadeInDown.duration(260).delay(Math.min(delay, 400))}>{content}</Animated.View>;
}

export function SectionHeader({ title, action, onAction, count }: { title: string; action?: string; onAction?: () => void; count?: number }) {
  const t = useTheme();
  return (
    <Row style={{ marginTop: t.spacing.xl, marginBottom: t.spacing.sm }}>
      <T v="label" color="muted" style={{ textTransform: 'uppercase', letterSpacing: 0.6 }}>{title}{count != null ? ` · ${count}` : ''}</T>
      <Grow />
      {action ? <Pressable onPress={onAction} hitSlop={12}><T v="label" color="primary">{action}</T></Pressable> : null}
    </Row>
  );
}

// ---------- Buttons ----------
export function Button({ title, onPress, variant = 'primary', icon, loading, disabled, size = 'md', style, haptic = true }:
  { title: string; onPress?: () => void; variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'success'; icon?: IconName; loading?: boolean; disabled?: boolean; size?: 'md' | 'lg' | 'sm'; style?: StyleProp<ViewStyle>; haptic?: boolean }) {
  const t = useTheme();
  const bg = { primary: t.colors.primary, secondary: t.colors.surfaceAlt, ghost: 'transparent', danger: t.colors.critical, success: t.colors.stable }[variant];
  const fg = variant === 'secondary' || variant === 'ghost' ? (variant === 'ghost' ? t.colors.primary : t.colors.text) : variant === 'primary' ? t.colors.onPrimary : '#fff';
  const h = size === 'lg' ? t.touch.large + 4 : size === 'sm' ? 38 : t.touch.min;
  return (
    <Pressable accessibilityRole="button" disabled={disabled || loading}
      onPress={() => { if (haptic) void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light); onPress?.(); }}
      style={({ pressed }) => [{ minHeight: h, borderRadius: t.radius.md, backgroundColor: bg, paddingHorizontal: size === 'sm' ? 12 : 18,
        alignItems: 'center', justifyContent: 'center', flexDirection: 'row', gap: 8, opacity: disabled ? 0.45 : pressed ? 0.85 : 1,
        borderWidth: variant === 'secondary' ? StyleSheet.hairlineWidth : 0, borderColor: t.colors.border }, style]}>
      {loading ? <ActivityIndicator color={fg} /> : icon ? <Ionicons name={icon} size={size === 'sm' ? 16 : 20} color={fg} /> : null}
      <Text style={[size === 'sm' ? t.typography.label : t.typography.bodyStrong, { color: fg, fontSize: size === 'lg' ? 17 : undefined }]}>{title}</Text>
    </Pressable>
  );
}

export function IconButton({ icon, onPress, label, color, size = 22 }: { icon: IconName; onPress?: () => void; label: string; color?: string; size?: number }) {
  const t = useTheme();
  return (
    <Pressable accessibilityLabel={label} onPress={onPress} hitSlop={8}
      style={({ pressed }) => ({ width: t.touch.min, height: t.touch.min, alignItems: 'center', justifyContent: 'center', borderRadius: t.radius.pill, opacity: pressed ? 0.6 : 1 })}>
      <Ionicons name={icon} size={size} color={color ?? t.colors.text} />
    </Pressable>
  );
}

// ---------- Status ----------
export function Pill({ label, fg, bg, icon }: { label: string; fg: string; bg: string; icon?: IconName }) {
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 4, paddingHorizontal: 10, paddingVertical: 3, borderRadius: 999, backgroundColor: bg, alignSelf: 'flex-start' }}>
      {icon ? <Ionicons name={icon} size={12} color={fg} /> : null}
      <Text style={{ color: fg, fontSize: 12, fontWeight: '700' }}>{label}</Text>
    </View>
  );
}

/** Flag (Stable/Watch/Critical), bed status, dose status or alert severity. */
export function StatusPill({ status, kind = 'flag' }: { status: string; kind?: 'flag' | 'bed' | 'dose' | 'severity' }) {
  const t = useTheme();
  if (kind === 'bed') {
    const c = t.colors.bed[status as keyof typeof t.colors.bed] ?? t.colors.bed.Blocked;
    return <Pill label={status} fg={c.fg} bg={c.bg} />;
  }
  if (kind === 'dose') {
    const map: Record<string, { fg: string; bg: string; icon: IconName }> = {
      Given: { fg: t.colors.stable, bg: t.colors.stableSoft, icon: 'checkmark-circle' },
      Due: { fg: t.colors.info, bg: t.colors.infoSoft, icon: 'time' },
      Scheduled: { fg: t.colors.textMuted, bg: t.colors.surfaceAlt, icon: 'calendar-outline' },
      Delayed: { fg: t.colors.watch, bg: t.colors.watchSoft, icon: 'hourglass-outline' },
      Refused: { fg: t.colors.watch, bg: t.colors.watchSoft, icon: 'hand-left-outline' },
      Withheld: { fg: t.colors.watch, bg: t.colors.watchSoft, icon: 'pause-circle-outline' },
      Missed: { fg: t.colors.critical, bg: t.colors.criticalSoft, icon: 'alert-circle' },
    };
    const c = map[status] ?? map.Scheduled;
    return <Pill label={status} fg={c.fg} bg={c.bg} icon={c.icon} />;
  }
  if (kind === 'severity') {
    const c = status === 'Critical' ? flagColors(t, 'Critical') : status === 'Warning' ? flagColors(t, 'Watch') : { fg: t.colors.info, bg: t.colors.infoSoft };
    return <Pill label={status} fg={c.fg} bg={c.bg} icon={status === 'Critical' ? 'warning' : 'alert-circle-outline'} />;
  }
  const c = flagColors(t, status);
  return <Pill label={status} fg={c.fg} bg={c.bg} icon={status === 'Critical' ? 'warning' : status === 'Watch' ? 'eye' : 'checkmark'} />;
}

export function News2Badge({ score, band, applicable = true, size = 'md' }: { score: number | null | undefined; band: string | null | undefined; applicable?: boolean; size?: 'sm' | 'md' | 'lg' }) {
  const t = useTheme();
  const c = news2Colors(t, applicable ? band : null);
  const dim = size === 'lg' ? 52 : size === 'sm' ? 30 : 40;
  return (
    <View accessibilityLabel={applicable && score != null ? `NEWS2 ${score}` : 'NEWS2 not applicable'}
      style={{ minWidth: dim, height: dim, borderRadius: dim / 3.2, backgroundColor: c.bg, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 6 }}>
      <Text style={{ color: c.fg, fontWeight: '800', fontSize: size === 'lg' ? 22 : size === 'sm' ? 13 : 16 }}>{applicable ? (score ?? '–') : 'n/a'}</Text>
      {size !== 'sm' ? <Text style={{ color: c.fg, fontSize: 8.5, fontWeight: '700', marginTop: -2, opacity: 0.9 }}>NEWS2</Text> : null}
    </View>
  );
}

// ---------- States ----------
export function EmptyState({ icon = 'leaf-outline', title, message, action, onAction }: { icon?: IconName; title: string; message?: string; action?: string; onAction?: () => void }) {
  const t = useTheme();
  return (
    <View style={{ alignItems: 'center', paddingVertical: 40, paddingHorizontal: 24, gap: 8 }}>
      <View style={{ width: 64, height: 64, borderRadius: 32, backgroundColor: t.colors.primarySoft, alignItems: 'center', justifyContent: 'center' }}>
        <Ionicons name={icon} size={28} color={t.colors.primary} />
      </View>
      <T v="heading" style={{ textAlign: 'center' }}>{title}</T>
      {message ? <T color="muted" style={{ textAlign: 'center' }}>{message}</T> : null}
      {action ? <Button title={action} variant="secondary" onPress={onAction} style={{ marginTop: 8 }} /> : null}
    </View>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const t = useTheme();
  return (
    <View style={{ alignItems: 'center', paddingVertical: 36, paddingHorizontal: 24, gap: 8 }}>
      <View style={{ width: 64, height: 64, borderRadius: 32, backgroundColor: t.colors.criticalSoft, alignItems: 'center', justifyContent: 'center' }}>
        <Ionicons name="cloud-offline-outline" size={28} color={t.colors.critical} />
      </View>
      <T v="heading">Couldn't load</T>
      <T color="muted" style={{ textAlign: 'center' }}>{message}</T>
      {onRetry ? <Button title="Try again" icon="refresh" variant="secondary" onPress={onRetry} style={{ marginTop: 8 }} /> : null}
    </View>
  );
}

export function Skeleton({ height = 16, width = '100%', radius = 8, style }: { height?: number; width?: number | `${number}%`; radius?: number; style?: StyleProp<ViewStyle> }) {
  const t = useTheme();
  const o = useSharedValue(0.45);
  useEffect(() => { o.value = withRepeat(withTiming(1, { duration: 800 }), -1, true); }, [o]);
  const a = useAnimatedStyle(() => ({ opacity: o.value }));
  return <Animated.View style={[{ height, width, borderRadius: radius, backgroundColor: t.colors.skeleton }, a, style]} />;
}

export function SkeletonList({ rows = 4, height = 84 }: { rows?: number; height?: number }) {
  return <View style={{ gap: 12 }}>{Array.from({ length: rows }).map((_, i) => <Skeleton key={i} height={height} radius={16} />)}</View>;
}

/** Standard loading / error / empty wrapper for react-query results. */
export function QueryView<T>({ q, empty, skeleton, children }: {
  q: { data: T | undefined; isLoading: boolean; error: unknown; refetch: () => unknown };
  empty?: (d: T) => ReactNode | null; skeleton?: ReactNode; children: (d: T) => ReactNode;
}) {
  if (q.isLoading) return <>{skeleton ?? <SkeletonList />}</>;
  if (q.error && !q.data) return <ErrorState message={(q.error as Error).message} onRetry={() => void q.refetch()} />;
  if (q.data === undefined) return null;
  const e = empty?.(q.data);
  if (e) return <>{e}</>;
  return <>{children(q.data)}</>;
}

// ---------- Inputs ----------
export function TextField({ label, error, style, inputStyle, suffix, ...props }:
  Omit<TextInputProps, 'style'> & { label?: string; error?: string | null; suffix?: string; style?: StyleProp<ViewStyle>; inputStyle?: StyleProp<TextStyle> }) {
  const t = useTheme();
  return (
    <View style={[{ marginBottom: 12 }, style]}>
      {label ? <T v="label" color="muted" style={{ marginBottom: 6 }}>{label}</T> : null}
      <View style={{ flexDirection: 'row', alignItems: 'center', minHeight: t.touch.min, borderRadius: t.radius.md, borderWidth: 1,
        borderColor: error ? t.colors.critical : t.colors.border, backgroundColor: t.colors.surface, paddingHorizontal: 12 }}>
        <TextInput placeholderTextColor={t.colors.textFaint} {...props}
          style={[t.typography.body, { flex: 1, color: t.colors.text, paddingVertical: 10 }, props.multiline ? { minHeight: 90, textAlignVertical: 'top' } : null, inputStyle]} />
        {suffix ? <T color="faint" v="label">{suffix}</T> : null}
      </View>
      {error ? <T v="caption" color="critical" style={{ marginTop: 4 }}>{error}</T> : null}
    </View>
  );
}

export function Chip({ label, selected, onPress, icon, tone }: { label: string; selected?: boolean; onPress?: () => void; icon?: IconName; tone?: string }) {
  const t = useTheme();
  const bg = selected ? (tone ?? t.colors.primary) : t.colors.surface;
  const fg = selected ? '#fff' : t.colors.text;
  return (
    <Pressable onPress={() => { void Haptics.selectionAsync(); onPress?.(); }}
      style={({ pressed }) => ({ flexDirection: 'row', alignItems: 'center', gap: 6, minHeight: 40, paddingHorizontal: 14, borderRadius: 999,
        backgroundColor: bg, borderWidth: 1, borderColor: selected ? bg : t.colors.border, opacity: pressed ? 0.8 : 1 })}>
      {icon ? <Ionicons name={icon} size={15} color={fg} /> : null}
      <Text style={{ color: fg, fontWeight: '600', fontSize: 14 }}>{label}</Text>
    </Pressable>
  );
}

export function Segmented<K extends string>({ options, value, onChange }: { options: { key: K; label: string; badge?: number }[]; value: K; onChange: (k: K) => void }) {
  const t = useTheme();
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 6, paddingVertical: 4 }}>
      {options.map((o) => {
        const on = o.key === value;
        return (
          <Pressable key={o.key} onPress={() => { void Haptics.selectionAsync(); onChange(o.key); }}
            style={{ paddingHorizontal: 14, minHeight: 40, justifyContent: 'center', borderRadius: 12, backgroundColor: on ? t.colors.primarySoft : 'transparent', flexDirection: 'row', alignItems: 'center', gap: 6 }}>
            <Text style={{ color: on ? t.colors.primaryStrong : t.colors.textMuted, fontWeight: '700' }}>{o.label}</Text>
            {o.badge ? <View style={{ backgroundColor: t.colors.critical, borderRadius: 9, minWidth: 18, height: 18, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 4 }}>
              <Text style={{ color: '#fff', fontSize: 11, fontWeight: '800' }}>{o.badge}</Text></View> : null}
          </Pressable>
        );
      })}
    </ScrollView>
  );
}

export function Badge({ count, color }: { count: number; color?: string }) {
  const t = useTheme();
  if (!count) return null;
  return (
    <View style={{ backgroundColor: color ?? t.colors.critical, borderRadius: 10, minWidth: 20, height: 20, paddingHorizontal: 5, alignItems: 'center', justifyContent: 'center' }}>
      <Text style={{ color: '#fff', fontSize: 11, fontWeight: '800' }}>{count}</Text>
    </View>
  );
}

export function InfoRow({ icon, label, value }: { icon: IconName; label: string; value: ReactNode }) {
  const t = useTheme();
  return (
    <Row style={{ paddingVertical: 8 }} gap={12}>
      <Ionicons name={icon} size={18} color={t.colors.textFaint} />
      <T color="muted" style={{ width: 110 }}>{label}</T>
      <View style={{ flex: 1 }}>{typeof value === 'string' ? <T v="bodyStrong">{value}</T> : value}</View>
    </Row>
  );
}

export function Notice({ tone = 'info', icon, title, message, children }: { tone?: 'info' | 'warning' | 'critical' | 'success'; icon?: IconName; title?: string; message?: string; children?: ReactNode }) {
  const t = useTheme();
  const c = tone === 'critical' ? { fg: t.colors.critical, bg: t.colors.criticalSoft } : tone === 'warning' ? { fg: t.colors.watch, bg: t.colors.watchSoft }
    : tone === 'success' ? { fg: t.colors.stable, bg: t.colors.stableSoft } : { fg: t.colors.info, bg: t.colors.infoSoft };
  return (
    <View style={{ flexDirection: 'row', gap: 10, padding: 12, borderRadius: t.radius.md, backgroundColor: c.bg, marginBottom: 8 }}>
      <Ionicons name={icon ?? (tone === 'critical' ? 'close-circle' : tone === 'warning' ? 'warning' : tone === 'success' ? 'checkmark-circle' : 'information-circle')} size={20} color={c.fg} />
      <View style={{ flex: 1, gap: 2 }}>
        {title ? <T v="bodyStrong" color={c.fg}>{title}</T> : null}
        {message ? <T color={t.colors.text}>{message}</T> : null}
        {children}
      </View>
    </View>
  );
}
