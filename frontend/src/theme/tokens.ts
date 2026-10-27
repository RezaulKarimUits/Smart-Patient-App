// Design tokens: calm clinical palette (deep teal primary, soft neutral surfaces) with semantic colours.
export const palette = {
  teal900: '#083D3A', teal800: '#0B4F4A', teal700: '#0F766E', teal600: '#0D9488', teal500: '#14B8A6', teal300: '#5EEAD4', teal100: '#CCFBF1', teal50: '#F0FDFA',
  slate950: '#0B1514', slate900: '#12201F', slate850: '#182A29', slate800: '#203433', slate700: '#2F4746', slate500: '#6F8785', slate400: '#93A6A4',
  slate300: '#B8C7C5', slate200: '#DBE4E4', slate100: '#EEF3F3', slate50: '#F6F9F9', white: '#FFFFFF',
  green600: '#16A34A', green100: '#DCFCE7', amber600: '#D97706', amber100: '#FEF3C7', red600: '#DC2626', red100: '#FEE2E2',
  blue600: '#2563EB', blue100: '#DBEAFE', yellow600: '#CA8A04', yellow100: '#FEF9C3', grey600: '#64748B', grey100: '#EEF1F5',
};

export type ColorTokens = {
  primary: string; primaryStrong: string; primarySoft: string; onPrimary: string;
  background: string; surface: string; surfaceAlt: string; border: string; overlay: string;
  text: string; textMuted: string; textFaint: string;
  stable: string; stableSoft: string; watch: string; watchSoft: string; critical: string; criticalSoft: string;
  info: string; infoSoft: string;
  bed: Record<'Free' | 'Reserved' | 'Occupied' | 'Cleaning' | 'Blocked', { fg: string; bg: string }>;
  skeleton: string;
};

export const lightColors: ColorTokens = {
  primary: palette.teal700, primaryStrong: palette.teal800, primarySoft: palette.teal100, onPrimary: palette.white,
  background: palette.slate50, surface: palette.white, surfaceAlt: palette.slate100, border: palette.slate200, overlay: 'rgba(5,25,24,0.45)',
  text: '#0F1F1E', textMuted: '#4A5D5C', textFaint: palette.slate500,
  stable: palette.green600, stableSoft: palette.green100, watch: palette.amber600, watchSoft: palette.amber100, critical: palette.red600, criticalSoft: palette.red100,
  info: palette.blue600, infoSoft: palette.blue100,
  bed: {
    Free: { fg: palette.green600, bg: '#E8F7EE' },
    Reserved: { fg: palette.blue600, bg: '#E7EFFF' },
    Occupied: { fg: palette.teal700, bg: '#E3F4F2' },
    Cleaning: { fg: palette.yellow600, bg: '#FDF6DC' },
    Blocked: { fg: palette.grey600, bg: palette.grey100 },
  },
  skeleton: palette.slate200,
};

export const darkColors: ColorTokens = {
  primary: palette.teal500, primaryStrong: palette.teal300, primarySoft: '#123C39', onPrimary: '#04201E',
  background: palette.slate950, surface: palette.slate900, surfaceAlt: palette.slate850, border: palette.slate800, overlay: 'rgba(0,0,0,0.6)',
  text: '#E6F2F1', textMuted: '#A7BCBA', textFaint: palette.slate500,
  stable: '#4ADE80', stableSoft: '#11301E', watch: '#FBBF24', watchSoft: '#3A2C0A', critical: '#F87171', criticalSoft: '#3D1515',
  info: '#60A5FA', infoSoft: '#132845',
  bed: {
    Free: { fg: '#4ADE80', bg: '#10291B' },
    Reserved: { fg: '#60A5FA', bg: '#121F3B' },
    Occupied: { fg: palette.teal300, bg: '#0F2A28' },
    Cleaning: { fg: '#FACC15', bg: '#2C250C' },
    Blocked: { fg: palette.slate400, bg: '#1C232C' },
  },
  skeleton: palette.slate800,
};

export const spacing = { xxs: 2, xs: 4, sm: 8, md: 12, lg: 16, xl: 20, xxl: 24, xxxl: 32 } as const;
export const radius = { sm: 8, md: 12, lg: 16, xl: 22, pill: 999 } as const;
/** Minimum touch target for nurses on the move. */
export const touch = { min: 48, large: 56 } as const;

export const typography = {
  display: { fontSize: 28, fontWeight: '800' as const, letterSpacing: -0.4 },
  title: { fontSize: 22, fontWeight: '700' as const, letterSpacing: -0.2 },
  heading: { fontSize: 17, fontWeight: '700' as const },
  body: { fontSize: 15, fontWeight: '400' as const, lineHeight: 21 },
  bodyStrong: { fontSize: 15, fontWeight: '600' as const, lineHeight: 21 },
  label: { fontSize: 13, fontWeight: '600' as const },
  caption: { fontSize: 12, fontWeight: '500' as const },
  mono: { fontSize: 13, fontVariant: ['tabular-nums'] as ('tabular-nums')[] },
};

export const shadow = (dark: boolean) => ({
  card: dark
    ? { shadowColor: '#000', shadowOpacity: 0.35, shadowRadius: 10, shadowOffset: { width: 0, height: 4 }, elevation: 2 }
    : { shadowColor: '#0B3B38', shadowOpacity: 0.08, shadowRadius: 12, shadowOffset: { width: 0, height: 4 }, elevation: 2 },
  raised: { shadowColor: '#000', shadowOpacity: dark ? 0.5 : 0.18, shadowRadius: 16, shadowOffset: { width: 0, height: 8 }, elevation: 8 },
});
