import * as ExpoHaptics from 'expo-haptics';

/**
 * Same API as expo-haptics, but never throws: vibration is a nicety, so a device or Expo Go build without a
 * haptics method (UnavailabilityError "Haptic.impactAsync is not available on android") must not break a screen.
 * Import as: import * as Haptics from '@/lib/haptics';
 */
export const ImpactFeedbackStyle = ExpoHaptics.ImpactFeedbackStyle;
export const NotificationFeedbackType = ExpoHaptics.NotificationFeedbackType;

async function safely(run: () => Promise<void>): Promise<void> {
  try { await run(); } catch { /* haptics unavailable on this device/build: ignore */ }
}

export const impactAsync = (style: ExpoHaptics.ImpactFeedbackStyle = ExpoHaptics.ImpactFeedbackStyle.Medium) =>
  safely(() => ExpoHaptics.impactAsync(style));

export const notificationAsync = (type: ExpoHaptics.NotificationFeedbackType = ExpoHaptics.NotificationFeedbackType.Success) =>
  safely(() => ExpoHaptics.notificationAsync(type));

export const selectionAsync = () => safely(() => ExpoHaptics.selectionAsync());
