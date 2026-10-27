import { useColorScheme } from 'react-native';
import { darkColors, lightColors, radius, shadow, spacing, touch, typography, type ColorTokens } from './tokens';

export * from './tokens';

export type Theme = {
  dark: boolean;
  colors: ColorTokens;
  spacing: typeof spacing;
  radius: typeof radius;
  typography: typeof typography;
  touch: typeof touch;
  shadow: ReturnType<typeof shadow>;
};

const light: Theme = { dark: false, colors: lightColors, spacing, radius, typography, touch, shadow: shadow(false) };
const dark: Theme = { dark: true, colors: darkColors, spacing, radius, typography, touch, shadow: shadow(true) };

export function useTheme(): Theme {
  return useColorScheme() === 'dark' ? dark : light;
}

export function flagColors(t: Theme, flag: string | null | undefined) {
  if (flag === 'Critical') return { fg: t.colors.critical, bg: t.colors.criticalSoft };
  if (flag === 'Watch') return { fg: t.colors.watch, bg: t.colors.watchSoft };
  return { fg: t.colors.stable, bg: t.colors.stableSoft };
}

export function news2Colors(t: Theme, band: string | null | undefined) {
  switch (band) {
    case 'High': return { fg: '#FFFFFF', bg: t.colors.critical };
    case 'Medium': return { fg: t.dark ? '#1F1300' : '#7C2D12', bg: t.dark ? '#FB923C' : '#FED7AA' };
    case 'LowMedium': return { fg: t.dark ? '#FBBF24' : '#92400E', bg: t.colors.watchSoft };
    case 'Low': return { fg: t.colors.stable, bg: t.colors.stableSoft };
    default: return { fg: t.colors.textMuted, bg: t.colors.surfaceAlt };
  }
}
