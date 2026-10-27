import { useQuery } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { api } from '@/api/client';
import type { Patient } from '@/api/types';
import { Button, Card, EmptyState, QueryView, Row, Screen, StatusPill, T, TextField } from '@/components/ui';

export default function Patients() {
  const [q, setQ] = useState('');
  const query = useQuery<Patient[]>({ queryKey: ['patients', q], queryFn: () => api.get(`/api/patients?q=${encodeURIComponent(q)}`) });
  return (
    <Screen refreshing={query.isRefetching} onRefresh={() => void query.refetch()}>
      <TextField placeholder="Search name, MRN or phone" value={q} onChangeText={setQ} autoCorrect={false} />
      <Button title="Register & admit new patient" icon="person-add-outline" onPress={() => router.push('/admit')} style={{ marginBottom: 12 }} />
      <QueryView q={query} empty={(l) => l.length === 0 ? <EmptyState icon="search" title="No patients found" message="Register a new patient to admit them." /> : null}>
        {(list) => list.map((p, i) => (
          <Card key={p.id} delay={i * 30} style={{ marginBottom: 10 }}
            onPress={() => p.activeAdmissionId ? router.push(`/wristband/${p.activeAdmissionId}`) : router.push({ pathname: '/admit', params: { patientId: String(p.id), patientName: p.name } })}>
            <Row>
              <T v="bodyStrong" style={{ flex: 1 }}>{p.name}</T>
              {p.activeAdmissionId ? <StatusPill status="Occupied" kind="bed" /> : <T v="label" color="primary">Admit →</T>}
            </Row>
            <T v="caption" color="muted">{p.mrn} · {p.ageYears}y {p.sex}{p.phone ? ` · ${p.phone}` : ''}</T>
          </Card>
        ))}
      </QueryView>
    </Screen>
  );
}
