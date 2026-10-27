import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DarkTheme, DefaultTheme, Stack, ThemeProvider } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { ApiError } from '@/api/client';
import { AuthProvider, useAuth } from '@/auth/AuthContext';
import { Banners } from '@/components/clinical';
import { ToastHost } from '@/components/Toast';
import { RealtimeProvider } from '@/realtime/RealtimeProvider';
import { useTheme } from '@/theme';

void SplashScreen.preventAutoHideAsync();

function makeClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15000,
        retry: (count, err) => !(err instanceof ApiError && [400, 401, 403, 404, 503].includes(err.status)) && count < 2,
      },
    },
  });
}

function Navigation() {
  const t = useTheme();
  const { ready } = useAuth();
  useEffect(() => { if (ready) void SplashScreen.hideAsync(); }, [ready]);
  const nav = t.dark ? DarkTheme : DefaultTheme;
  return (
    <ThemeProvider value={{ ...nav, colors: { ...nav.colors, primary: t.colors.primary, background: t.colors.background, card: t.colors.surface, text: t.colors.text, border: t.colors.border } }}>
      <StatusBar style={t.dark ? 'light' : 'dark'} />
      <View style={{ flex: 1, backgroundColor: t.colors.background }}>
        <Stack screenOptions={{ headerTintColor: t.colors.primary, headerTitleStyle: { color: t.colors.text, fontWeight: '700' }, headerStyle: { backgroundColor: t.colors.surface },
          contentStyle: { backgroundColor: t.colors.background }, animation: 'slide_from_right', headerBackButtonDisplayMode: 'minimal' }}>
          <Stack.Screen name="index" options={{ headerShown: false }} />
          <Stack.Screen name="login" options={{ headerShown: false, animation: 'fade' }} />
          <Stack.Screen name="(reception)" options={{ headerShown: false }} />
          <Stack.Screen name="(nurse)" options={{ headerShown: false }} />
          <Stack.Screen name="(consultant)" options={{ headerShown: false }} />
          <Stack.Screen name="(admin)" options={{ headerShown: false }} />
          <Stack.Screen name="(patient)" options={{ headerShown: false }} />
          <Stack.Screen name="patient/[id]" options={{ title: 'Patient' }} />
          <Stack.Screen name="record/[id]" options={{ title: 'Record entry' }} />
          <Stack.Screen name="prescribe/[id]" options={{ title: 'Prescribe' }} />
          <Stack.Screen name="round-note/[id]" options={{ title: 'Round note' }} />
          <Stack.Screen name="report/[id]" options={{ title: 'Report photo' }} />
          <Stack.Screen name="discharge/[id]" options={{ title: 'Discharge' }} />
          <Stack.Screen name="wristband/[id]" options={{ title: 'Wristband', presentation: 'modal' }} />
          <Stack.Screen name="bed/[id]" options={{ title: 'Bed' }} />
          <Stack.Screen name="admit" options={{ title: 'Admit patient' }} />
          <Stack.Screen name="scan" options={{ headerShown: false, presentation: 'fullScreenModal', animation: 'fade' }} />
          <Stack.Screen name="profile" options={{ title: 'Settings', presentation: 'modal' }} />
        </Stack>
        <Banners />
        <ToastHost />
      </View>
    </ThemeProvider>
  );
}

export default function RootLayout() {
  const [client] = useState(makeClient);
  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <SafeAreaProvider>
        <QueryClientProvider client={client}>
          <AuthProvider>
            <RealtimeProvider>
              <Navigation />
            </RealtimeProvider>
          </AuthProvider>
        </QueryClientProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}
