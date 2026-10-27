import { Ionicons } from '@expo/vector-icons';
import { Redirect, router, Tabs } from 'expo-router';
import { View } from 'react-native';
import type { Role } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { useTheme } from '@/theme';
import { LiveDot, ScanButton } from './clinical';
import { IconButton, type IconName } from './ui';

export type TabDef = { name: string; title: string; icon: IconName; badge?: number };

/** Role-guarded bottom tabs with a consistent header (live indicator + settings) and an optional floating Scan button. */
export function RoleTabs({ roles, tabs, scan }: { roles: Role[]; tabs: TabDef[]; scan?: boolean }) {
  const t = useTheme();
  const { user, ready } = useAuth();
  if (ready && !user) return <Redirect href="/login" />;
  if (user && !roles.includes(user.role)) return <Redirect href="/" />;
  return (
    <View style={{ flex: 1 }}>
      <Tabs screenOptions={{
        headerStyle: { backgroundColor: t.colors.surface }, headerShadowVisible: false,
        headerTitleStyle: { color: t.colors.text, fontWeight: '800', fontSize: 20 },
        headerRight: () => (
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 4, marginRight: 6 }}>
            <LiveDot />
            <IconButton icon="person-circle-outline" label="Settings" onPress={() => router.push('/profile')} size={26} />
          </View>
        ),
        tabBarActiveTintColor: t.colors.primary, tabBarInactiveTintColor: t.colors.textFaint,
        tabBarStyle: { backgroundColor: t.colors.surface, borderTopColor: t.colors.border, height: 64, paddingBottom: 8, paddingTop: 6 },
        tabBarLabelStyle: { fontSize: 12, fontWeight: '600' },
        sceneStyle: { backgroundColor: t.colors.background },
      }}>
        {tabs.map((tab) => (
          <Tabs.Screen key={tab.name} name={tab.name} options={{
            title: tab.title,
            tabBarBadge: tab.badge ? tab.badge : undefined,
            tabBarBadgeStyle: { backgroundColor: t.colors.critical },
            tabBarIcon: ({ color, focused }) => <Ionicons name={(focused ? String(tab.icon).replace('-outline', '') : tab.icon) as IconName} size={24} color={String(color)} />,
          }} />
        ))}
      </Tabs>
      {scan ? <ScanButton /> : null}
    </View>
  );
}
