import { useQuery } from '@tanstack/react-query';
import * as Haptics from '@/lib/haptics';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Image, View } from 'react-native';
import { api } from '@/api/client';
import { useInvalidateAdmission } from '@/api/hooks';
import type { BedTile, Qr } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { BedTile as Tile } from '@/components/clinical';
import { Button, Card, QueryView, Screen, Spacer, T, TextField } from '@/components/ui';
import { relative } from '@/lib/format';
import { toastError } from '@/components/Toast';

/** Bed detail and lifecycle actions: Free → Reserved → Occupied → Cleaning → Free, plus Blocked. */
export default function BedScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const bedId = Number(id);
  const { user } = useAuth();
  const frontDesk = user?.role === 'Receptionist' || user?.role === 'HospitalAdmin';
  const q = useQuery<BedTile>({ queryKey: ['bed', bedId], queryFn: () => api.get(`/api/beds/${bedId}`) });
  const qr = useQuery<Qr>({ queryKey: ['bedQr', bedId], queryFn: () => api.get(`/api/beds/${bedId}/qr`), enabled: frontDesk });
  const invalidate = useInvalidateAdmission();
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);

  const act = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    try { await fn(); void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success); invalidate(); await q.refetch(); setNote(''); }
    catch (e) { toastError(e); } finally { setBusy(false); }
  };
  const setStatus = (status: string, n?: string) => act(() => api.post(`/api/beds/${bedId}/status`, { status, note: n ?? (note || null) }));

  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <QueryView q={q}>
        {(bed) => (
          <>
            <Stack.Screen options={{ title: `${bed.wardName.replace(' Ward', '')} · ${bed.bedNumber}` }} />
            <Tile bed={bed} />
            <T v="caption" color="muted" style={{ marginTop: 8 }}>Status since {relative(bed.statusChangedAt)}</T>
            <Spacer h={16} />
            <View style={{ gap: 10 }}>
              {bed.occupant ? <Button title={`Open ${bed.occupant.patientName}`} icon="person-outline" onPress={() => router.push(`/patient/${bed.occupant!.admissionId}`)} /> : null}
              {frontDesk && bed.status === 'Free' ? (
                <>
                  <Button title="Admit patient here" icon="person-add-outline" size="lg" onPress={() => router.push({ pathname: '/admit', params: { bedId: String(bed.id) } })} />
                  <TextField label="Note (reason for reserve / block)" value={note} onChangeText={setNote} />
                  <Button title="Reserve" variant="secondary" loading={busy} onPress={() => void act(() => api.post(`/api/beds/${bedId}/reserve`, { note: note || null }))} />
                  <Button title="Block bed" variant="secondary" loading={busy} disabled={!note} onPress={() => void setStatus('Blocked')} />
                </>
              ) : null}
              {frontDesk && bed.status === 'Reserved' && !bed.occupant ? <Button title="Release reservation" variant="secondary" loading={busy} onPress={() => void setStatus('Free', 'Reservation released')} /> : null}
              {bed.occupant && frontDesk ? <Button title="Wristband" icon="qr-code-outline" variant="secondary" onPress={() => router.push(`/wristband/${bed.occupant!.admissionId}`)} /> : null}
              {bed.status === 'Cleaning' && user?.role !== 'Consultant' ? <Button title="Mark cleaned · Free" icon="sparkles-outline" size="lg" loading={busy} onPress={() => void setStatus('Free', 'Cleaned and ready')} /> : null}
              {bed.status === 'Blocked' && frontDesk ? <Button title="Unblock" loading={busy} onPress={() => void setStatus('Free', 'Unblocked')} /> : null}
            </View>
            {frontDesk && qr.data ? (
              <Card style={{ marginTop: 20, alignItems: 'center' }}>
                <T v="label" color="muted">BED QR</T>
                <Image source={{ uri: `data:image/png;base64,${qr.data.pngBase64}` }} style={{ width: 200, height: 200, marginVertical: 10 }} />
                <T v="caption" color="faint" selectable>{qr.data.token}</T>
              </Card>
            ) : null}
          </>
        )}
      </QueryView>
    </Screen>
  );
}
