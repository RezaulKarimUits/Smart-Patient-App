import { Ionicons } from '@expo/vector-icons';
import * as Haptics from '@/lib/haptics';
import { router } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, View } from 'react-native';
import Animated, { FadeInDown, FadeInUp } from 'react-native-reanimated';
import { SafeAreaView } from 'react-native-safe-area-context';
import { API_URL, errorMessage } from '@/api/client';
import { homeFor, useAuth } from '@/auth/AuthContext';
import { Button, Row, T, TextField } from '@/components/ui';
import { useTheme } from '@/theme';
import { toastError } from '@/components/Toast';

const DEMO = [
  { role: 'Reception', phone: '01800000002', icon: 'grid-outline' as const },
  { role: 'Nurse', phone: '01800000011', icon: 'medkit-outline' as const },
  { role: 'Consultant', phone: '01800000021', icon: 'pulse-outline' as const },
  { role: 'Admin', phone: '01800000001', icon: 'business-outline' as const },
  { role: 'Patient', phone: '01800000032', icon: 'person-outline' as const },
  { role: 'Hospital 2', phone: '01800000102', icon: 'swap-horizontal-outline' as const },
];

export default function Login() {
  const t = useTheme();
  const { signIn } = useAuth();
  const [phone, setPhone] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [showCode, setShowCode] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (p = phone, pw = password) => {
    setBusy(true);
    try {
      const user = await signIn(p, pw, code || undefined);
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      router.replace(homeFor(user.role));
    } catch (e) {
      toastError(e);
      if (errorMessage(e).includes('hospital code')) setShowCode(true);
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    } finally { setBusy(false); }
  };

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: t.colors.background }}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        <ScrollView contentContainerStyle={{ padding: 24, flexGrow: 1, justifyContent: 'center', maxWidth: 520, width: '100%', alignSelf: 'center' }} keyboardShouldPersistTaps="handled">
          <Animated.View entering={FadeInUp.duration(400)} style={{ alignItems: 'center', marginBottom: 28 }}>
            <View style={{ width: 72, height: 72, borderRadius: 22, backgroundColor: t.colors.primary, alignItems: 'center', justifyContent: 'center', ...t.shadow.raised }}>
              <Ionicons name="medical" size={38} color={t.colors.onPrimary} />
            </View>
            <T v="display" style={{ marginTop: 16 }}>InPatient</T>
            <T color="muted" style={{ textAlign: 'center', marginTop: 4 }}>Ward care, together · ওয়ার্ড ব্যবস্থাপনা</T>
          </Animated.View>

          <Animated.View entering={FadeInDown.duration(400).delay(100)}>
            <TextField label="Phone" value={phone} onChangeText={setPhone} keyboardType="phone-pad" autoComplete="tel" placeholder="01800000011" />
            <TextField label="Password" value={password} onChangeText={setPassword} secureTextEntry autoComplete="password" placeholder="••••••••"
              onSubmitEditing={() => void submit()} returnKeyType="go" />
            {showCode ? <TextField label="Hospital code" value={code} onChangeText={setCode} autoCapitalize="characters" placeholder="DCGH" /> : null}
            <Button title="Sign in" size="lg" onPress={() => void submit()} loading={busy} disabled={!phone || !password} />
            {!showCode ? <Pressable onPress={() => setShowCode(true)} style={{ alignSelf: 'center', padding: 12 }}><T v="label" color="primary">Use hospital code</T></Pressable> : null}
          </Animated.View>

          <Animated.View entering={FadeInDown.duration(400).delay(220)} style={{ marginTop: 18 }}>
            <T v="label" color="muted" style={{ marginBottom: 8 }}>DEMO LOGINS · password Demo@123</T>
            <Row wrap gap={8}>
              {DEMO.map((d) => (
                <Pressable key={d.phone} onPress={() => { setPhone(d.phone); setPassword('Demo@123'); void submit(d.phone, 'Demo@123'); }}
                  style={({ pressed }) => ({ flexDirection: 'row', alignItems: 'center', gap: 6, paddingHorizontal: 12, minHeight: 44, borderRadius: 12,
                    backgroundColor: t.colors.surface, borderWidth: 1, borderColor: t.colors.border, opacity: pressed ? 0.7 : 1 })}>
                  <Ionicons name={d.icon} size={16} color={t.colors.primary} />
                  <T v="label">{d.role}</T>
                </Pressable>
              ))}
            </Row>
            <T v="caption" color="faint" style={{ marginTop: 16, textAlign: 'center' }}>Server: {API_URL}</T>
          </Animated.View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
