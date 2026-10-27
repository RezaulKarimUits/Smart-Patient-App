import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import * as Haptics from '@/lib/haptics';
import * as Speech from 'expo-speech';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { API_URL } from '@/api/client';
import type { Alert as AlertDto } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';

export type BannerKind = 'info' | 'dose' | 'warning' | 'critical' | 'success';
export type Banner = { id: number; kind: BannerKind; title: string; message?: string; admissionId?: number };

type RealtimeState = {
  connected: boolean;
  banners: Banner[];
  dismiss: (id: number) => void;
  notify: (b: Omit<Banner, 'id'>) => void;
  speakReminders: boolean;
  setSpeakReminders: (v: boolean) => void;
};

const Ctx = createContext<RealtimeState | null>(null);
let nextId = 1;

/**
 * SignalR connection to /hubs/ward. Server events invalidate react-query caches; important ones raise an in-app banner
 * with haptics (and optional spoken read-out of dose reminders). While disconnected, screens poll (see usePolling).
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { token, user } = useAuth();
  const qc = useQueryClient();
  const [connected, setConnected] = useState(false);
  const [banners, setBanners] = useState<Banner[]>([]);
  const [speakReminders, setSpeakReminders] = useState(false);
  const speakRef = useRef(speakReminders);
  speakRef.current = speakReminders;
  const conn = useRef<HubConnection | null>(null);

  const dismiss = useCallback((id: number) => setBanners((b) => b.filter((x) => x.id !== id)), []);
  const notify = useCallback((b: Omit<Banner, 'id'>) => {
    const id = nextId++;
    setBanners((list) => [{ ...b, id }, ...list].slice(0, 3));
    if (b.kind === 'critical') void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    else if (b.kind === 'warning' || b.kind === 'dose') void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
    else if (b.kind === 'success') void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    setTimeout(() => dismiss(id), b.kind === 'critical' ? 15000 : 7000);
  }, [dismiss]);

  useEffect(() => {
    if (!token || !user || user.role === 'Patient') return;
    const c = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/ward`, { accessTokenFactory: () => token })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.None)
      .build();
    conn.current = c;

    const invalidateClinical = (admissionId?: number) => {
      void qc.invalidateQueries({ queryKey: ['nurseBeds'] });
      void qc.invalidateQueries({ queryKey: ['priority'] });
      void qc.invalidateQueries({ queryKey: ['alerts'] });
      void qc.invalidateQueries({ queryKey: ['round'] });
      if (admissionId) void qc.invalidateQueries({ queryKey: ['admission', admissionId] });
    };

    c.on('bedChanged', () => { void qc.invalidateQueries({ queryKey: ['board'] }); void qc.invalidateQueries({ queryKey: ['wards'] }); void qc.invalidateQueries({ queryKey: ['nurseBeds'] }); });
    c.on('admissionUpdated', (p: { admissionId?: number }) => { invalidateClinical(p?.admissionId); void qc.invalidateQueries({ queryKey: ['board'] }); });
    c.on('tasksChanged', (p: { admissionId?: number; message?: string; reason?: string }) => {
      invalidateClinical(p?.admissionId);
      if (p?.reason === 'prescriptionSigned') notify({ kind: 'info', title: 'New medication orders', message: 'A prescription was signed — doses added to your round', admissionId: p.admissionId });
      if (p?.reason === 'vitalsRecheck' && p.message) notify({ kind: 'warning', title: 'Vitals re-check', message: p.message, admissionId: p.admissionId });
    });
    c.on('doseDue', (p: { message?: string; admissionId?: number }) => {
      invalidateClinical(p?.admissionId);
      notify({ kind: 'dose', title: 'Dose due', message: p?.message, admissionId: p?.admissionId });
      if (speakRef.current && p?.message) Speech.speak(p.message, { rate: 0.95 });
    });
    c.on('doseMissed', (p: { message?: string; admissionId?: number }) => {
      invalidateClinical(p?.admissionId);
      notify({ kind: 'warning', title: 'Missed dose', message: p?.message, admissionId: p?.admissionId });
    });
    c.on('alertRaised', (p: { alert?: AlertDto }) => {
      invalidateClinical(p?.alert?.admissionId);
      const a = p?.alert;
      if (a) notify({ kind: a.severity === 'Critical' ? 'critical' : 'warning', title: `${a.severity} · ${a.bedNumber ?? ''} ${a.patientName}`, message: a.aiExplanation ?? a.ruleDetail, admissionId: a.admissionId });
    });
    c.on('alertUpdated', (p: { alert?: AlertDto; escalated?: boolean; admissionId?: number }) => {
      invalidateClinical(p?.alert?.admissionId ?? p?.admissionId);
      if (p?.escalated && p.alert) notify({ kind: 'critical', title: `Escalated · ${p.alert.bedNumber ?? ''} ${p.alert.patientName}`, message: p.alert.ruleDetail, admissionId: p.alert.admissionId });
    });
    c.onreconnecting(() => setConnected(false));
    c.onreconnected(() => { setConnected(true); invalidateClinical(); });
    c.onclose(() => setConnected(false));

    let cancelled = false;
    const start = async () => {
      try { await c.start(); if (!cancelled) setConnected(true); }
      catch { if (!cancelled) { setConnected(false); setTimeout(() => { if (!cancelled && c.state === HubConnectionState.Disconnected) void start(); }, 10000); } }
    };
    void start();
    return () => { cancelled = true; void c.stop(); conn.current = null; setConnected(false); };
  }, [token, user, qc, notify]);

  const value = useMemo(() => ({ connected, banners, dismiss, notify, speakReminders, setSpeakReminders }), [connected, banners, dismiss, notify, speakReminders]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useRealtime() {
  const v = useContext(Ctx);
  if (!v) throw new Error('useRealtime outside RealtimeProvider');
  return v;
}

/** refetchInterval for react-query: poll every 20 s only while the realtime channel is down. */
export function usePolling(ms = 20000): number | false {
  const { connected } = useRealtime();
  return connected ? false : ms;
}
