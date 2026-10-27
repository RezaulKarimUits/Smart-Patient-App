import { router } from 'expo-router';
import { useState } from 'react';
import { api } from '@/api/client';
import { useAlerts, useInvalidateAdmission } from '@/api/hooks';
import type { Alert } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { useTheme } from '@/theme';
import { relative } from '@/lib/format';
import { Button, Card, EmptyState, Notice, QueryView, Row, Screen, Segmented, StatusPill, T } from './ui';
import { toastError } from '@/components/Toast';

/** Active alerts for my wards (consultant) / my beds (nurse), critical first, with acknowledge. */
export function AlertsList() {
  const t = useTheme();
  const { user } = useAuth();
  const [scope, setScope] = useState<'active' | 'recent'>('active');
  const q = useAlerts(scope === 'active');
  const invalidate = useInvalidateAdmission();

  const act = async (a: Alert, action: 'ack' | 'resolve') => {
    try { await api.post(`/api/alerts/${a.id}/${action}`); invalidate(a.admissionId); void q.refetch(); }
    catch (e) { toastError(e, 'Failed'); }
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={() => void q.refetch()}>
      <Segmented value={scope} onChange={setScope} options={[{ key: 'active', label: 'Active', badge: q.data?.filter((a) => a.status === 'Open').length }, { key: 'recent', label: 'Last 7 days' }]} />
      <QueryView q={q} empty={(l) => l.length === 0 ? <EmptyState icon="shield-checkmark-outline" title="No alerts" message="Coded rules watch NEWS2, critical labs, trends and missed doses." /> : null}>
        {(list) => list.map((a, i) => (
          <Card key={a.id} delay={i * 30} style={{ marginTop: 10 }} accent={a.severity === 'Critical' ? t.colors.critical : t.colors.watch} onPress={() => router.push(`/patient/${a.admissionId}`)}>
            <Row>
              <StatusPill status={a.severity} kind="severity" />
              <T v="bodyStrong" style={{ flex: 1 }} numberOfLines={1}>{a.bedNumber} · {a.patientName}</T>
              <T v="caption" color="faint">{relative(a.createdAt)}</T>
            </Row>
            <T style={{ marginTop: 6 }}>{a.ruleDetail}</T>
            {a.aiExplanation ? <Notice tone="info" icon="sparkles-outline" message={a.aiExplanation} /> : null}
            <Row style={{ marginTop: 4 }}>
              <T v="caption" color="muted" style={{ flex: 1 }}>{a.alertType}{a.escalationLevel ? ` · escalated L${a.escalationLevel}${a.escalationLevel === 2 ? ' (duty doctor)' : ''}` : ''}{a.ackByName ? ` · ack ${a.ackByName}` : ''}</T>
              {a.status === 'Open' ? <Button title="Acknowledge" size="sm" onPress={() => void act(a, 'ack')} /> : <StatusPill status={a.status === 'Resolved' ? 'Given' : 'Scheduled'} kind="dose" />}
              {user?.role === 'Consultant' && a.status !== 'Resolved' ? <Button title="Resolve" size="sm" variant="ghost" onPress={() => void act(a, 'resolve')} /> : null}
            </Row>
          </Card>
        ))}
      </QueryView>
    </Screen>
  );
}
