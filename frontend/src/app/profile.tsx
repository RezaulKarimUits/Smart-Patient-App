import { router } from 'expo-router';
import { Switch } from 'react-native';
import { API_URL } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { Button, Card, InfoRow, Row, Screen, Spacer, T } from '@/components/ui';
import { useRealtime } from '@/realtime/RealtimeProvider';
import { useTheme } from '@/theme';

export default function Profile() {
  const t = useTheme();
  const { user, signOut } = useAuth();
  const { connected, speakReminders, setSpeakReminders } = useRealtime();
  if (!user) return null;
  return (
    <Screen>
      <Card>
        <T v="title">{user.name}</T>
        <T color="muted">{user.designation ?? user.role}</T>
        <Spacer h={8} />
        <InfoRow icon="business-outline" label="Hospital" value={user.hospitalName ?? '—'} />
        <InfoRow icon="call-outline" label="Phone" value={user.phone} />
        <InfoRow icon="shield-checkmark-outline" label="Role" value={user.role} />
        <InfoRow icon="wifi-outline" label="Realtime" value={connected ? 'Connected' : 'Polling every 20 s'} />
        <InfoRow icon="server-outline" label="Server" value={API_URL} />
      </Card>
      {user.role === 'Nurse' ? (
        <Card style={{ marginTop: 12 }}>
          <Row>
            <T v="bodyStrong" style={{ flex: 1 }}>Read dose reminders aloud</T>
            <Switch value={speakReminders} onValueChange={setSpeakReminders} trackColor={{ true: t.colors.primary, false: t.colors.border }} />
          </Row>
          <T v="caption" color="muted" style={{ marginTop: 4 }}>Uses the phone's text-to-speech when a dose becomes due.</T>
        </Card>
      ) : null}
      <Spacer h={20} />
      <Button title="Sign out" icon="log-out-outline" variant="danger" onPress={async () => { await signOut(); router.dismissAll(); router.replace('/login'); }} />
      <T v="caption" color="faint" style={{ textAlign: 'center', marginTop: 16 }}>AI supports clinical judgement and never replaces it.</T>
    </Screen>
  );
}
