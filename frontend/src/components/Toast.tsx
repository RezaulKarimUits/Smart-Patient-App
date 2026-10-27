import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import { useEffect, useState, type ReactNode } from 'react';
import { Platform, Pressable, Text, View } from 'react-native';
import Animated, { FadeOut, SlideInUp } from 'react-native-reanimated';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { FullWindowOverlay } from 'react-native-screens';
import { describeError } from '@/api/client';
import { useTheme } from '@/theme';

/**
 * Short pop-up messages ("toasts") that slide in at the top, stay a few seconds and disappear.
 * Callable from anywhere (screens, hooks, plain functions):
 *   toast.success('Saved');  toast.warning('Check the form', 'Drug name is required');  toastError(e);
 */
export type ToastKind = 'error' | 'warning' | 'success' | 'info';
type ToastItem = { id: number; kind: ToastKind; title: string; message?: string };

const DURATION: Record<ToastKind, number> = { error: 5000, warning: 4500, success: 3000, info: 3500 };
const MAX_VISIBLE = 3;

let items: ToastItem[] = [];
let seq = 0;
const timers = new Map<number, ReturnType<typeof setTimeout>>();
const listeners = new Set<(list: ToastItem[]) => void>();
const emit = () => listeners.forEach((l) => l(items));

function dismiss(id: number) {
  const timer = timers.get(id);
  if (timer) clearTimeout(timer);
  timers.delete(id);
  items = items.filter((x) => x.id !== id);
  emit();
}

export function showToast(kind: ToastKind, title: string, message?: string, durationMs = DURATION[kind]) {
  // The same message again (e.g. a double tap) replaces the old one instead of stacking.
  items.filter((x) => x.title === title && x.message === message).forEach((x) => dismiss(x.id));
  const id = ++seq;
  items = [{ id, kind, title, message }, ...items];
  items.slice(MAX_VISIBLE).forEach((x) => dismiss(x.id));
  items = items.slice(0, MAX_VISIBLE);
  timers.set(id, setTimeout(() => dismiss(id), durationMs));
  emit();
  if (kind === 'error') void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error).catch(() => undefined);
  else if (kind === 'warning') void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning).catch(() => undefined);
}

export const toast = {
  error: (title: string, message?: string) => showToast('error', title, message),
  warning: (title: string, message?: string) => showToast('warning', title, message),
  success: (title: string, message?: string) => showToast('success', title, message),
  info: (title: string, message?: string) => showToast('info', title, message),
};

/** Shows a failed request/action as a toast with a plain-language title. `title` overrides the default heading. */
export function toastError(e: unknown, title?: string) {
  const d = describeError(e);
  showToast(d.kind, title ?? d.title, d.message);
}

/** Renders the toasts. Mounted once at the root; on iOS it sits in a full-window overlay so it also shows above modals (e.g. the scanner). */
export function ToastHost() {
  const [list, setList] = useState<ToastItem[]>(items);
  useEffect(() => {
    listeners.add(setList);
    return () => { listeners.delete(setList); };
  }, []);
  if (list.length === 0) return null;
  const content = <ToastStack list={list} />;
  return Platform.OS === 'ios' ? <FullWindowOverlay>{content}</FullWindowOverlay> : content;
}

function ToastStack({ list }: { list: ToastItem[] }) {
  const t = useTheme();
  const insets = useSafeAreaInsets();
  return (
    <View pointerEvents="box-none" style={{ position: 'absolute', top: insets.top + 8, left: 12, right: 12, gap: 8, zIndex: 1000, elevation: 1000 }}>
      {list.map((x) => {
        const c = x.kind === 'error' ? { fg: '#fff', bg: t.colors.critical, icon: 'close-circle' as const }
          : x.kind === 'warning' ? { fg: '#1F1300', bg: '#FBBF24', icon: 'warning' as const }
          : x.kind === 'success' ? { fg: '#fff', bg: t.colors.stable, icon: 'checkmark-circle' as const }
          : { fg: t.colors.onPrimary, bg: t.colors.primary, icon: 'information-circle' as const };
        return (
          <Animated.View key={x.id} entering={SlideInUp.springify().damping(18)} exiting={FadeOut.duration(180)}>
            <Pressable onPress={() => dismiss(x.id)} accessibilityRole="alert" accessibilityLabel={`${x.title}. ${x.message ?? ''}`}
              style={{ flexDirection: 'row', gap: 10, paddingVertical: 12, paddingHorizontal: 14, borderRadius: 16, backgroundColor: c.bg, alignItems: 'center', minHeight: 56, ...t.shadow.raised }}>
              <Ionicons name={c.icon} size={24} color={c.fg} />
              <View style={{ flex: 1 }}>
                <Line color={c.fg} bold>{x.title}</Line>
                {x.message ? <Line color={c.fg}>{x.message}</Line> : null}
              </View>
              <Ionicons name="close" size={18} color={c.fg} style={{ opacity: 0.8 }} />
            </Pressable>
          </Animated.View>
        );
      })}
    </View>
  );
}

function Line({ children, color, bold }: { children: ReactNode; color: string; bold?: boolean }) {
  return <Text style={{ color, fontSize: bold ? 15 : 13, fontWeight: bold ? '800' : '400', marginTop: bold ? 0 : 2 }} numberOfLines={bold ? 2 : 4}>{children}</Text>;
}
