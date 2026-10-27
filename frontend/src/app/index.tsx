import { Redirect } from 'expo-router';
import { ActivityIndicator, View } from 'react-native';
import { homeFor, useAuth } from '@/auth/AuthContext';
import { useTheme } from '@/theme';

/** Role-based entry: each role has its own home. */
export default function Index() {
  const { ready, user } = useAuth();
  const t = useTheme();
  if (!ready) return <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: t.colors.background }}><ActivityIndicator color={t.colors.primary} /></View>;
  return <Redirect href={user ? homeFor(user.role) : '/login'} />;
}
