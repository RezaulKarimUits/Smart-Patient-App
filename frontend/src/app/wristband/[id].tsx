import { useQuery } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { Image, Linking, View } from 'react-native';
import { API_URL, api, getToken } from '@/api/client';
import type { Wristband } from '@/api/types';
import { Button, Card, InfoRow, Notice, QueryView, Screen, Spacer, T } from '@/components/ui';
import { dayMonth } from '@/lib/format';
import { useTheme } from '@/theme';

/** Shows the wristband QR (random token, no personal data) and opens the printable band in the browser. */
export default function WristbandScreen() {
  const t = useTheme();
  const { id } = useLocalSearchParams<{ id: string }>();
  const q = useQuery<Wristband>({ queryKey: ['wristband', id], queryFn: () => api.get(`/api/admissions/${id}/wristband`) });
  return (
    <Screen>
      <QueryView q={q}>
        {(w) => (
          <>
            <Notice tone="success" title="Admitted" message={`${w.bedNumber} is reserved. The nurse scans the bed, then this wristband, to mark it Occupied.`} />
            <Card style={{ alignItems: 'center' }}>
              <Image source={{ uri: `data:image/png;base64,${w.qrPngBase64}` }} style={{ width: 220, height: 220, backgroundColor: '#fff', borderRadius: 12 }} />
              <Spacer />
              <T v="title">{w.patientName}</T>
              <T color="muted">{w.mrn} · {w.ageYears}y {w.sex}{w.bloodGroup ? ` · ${w.bloodGroup}` : ''}</T>
            </Card>
            <Card style={{ marginTop: 12 }}>
              <InfoRow icon="bed-outline" label="Ward / bed" value={`${w.wardName ?? '—'} · ${w.bedNumber ?? '—'}`} />
              <InfoRow icon="person-outline" label="Consultant" value={w.consultantName} />
              <InfoRow icon="calendar-outline" label="Admitted" value={dayMonth(w.admittedAt)} />
            </Card>
            <Spacer h={16} />
            <View style={{ gap: 10 }}>
              <Button title="Print wristband" icon="print-outline" size="lg"
                onPress={() => void Linking.openURL(`${API_URL}/print/wristband.html?id=${w.admissionId}#token=${getToken() ?? ''}`)} />
              <Button title="Done" variant="secondary" onPress={() => router.dismissTo('/(reception)')} />
            </View>
            <T v="caption" color="faint" style={{ textAlign: 'center', marginTop: 12 }}>Code: {w.token}</T>
            <View style={{ height: 1, backgroundColor: t.colors.border, marginTop: 12 }} />
          </>
        )}
      </QueryView>
    </Screen>
  );
}
