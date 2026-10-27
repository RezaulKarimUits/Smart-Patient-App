import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { usePolling } from '@/realtime/RealtimeProvider';
import { api } from './client';
import type {
  AdminBed, AdminWard, AdmissionHeader, AiProgress, Alert, Board, DoseSchedule, FileItem, Handover, Journey, LabsResponse, NurseHome, Note,
  PatientStay, PendingPatient, Prescription, PriorityPatient, RoundPatient, Shift, Staff, TimelineItem, Vital, WardHistory, WardSummary,
} from './types';

export function useBoard() {
  const poll = usePolling();
  return useQuery<Board>({ queryKey: ['board'], queryFn: () => api.get('/api/board'), refetchInterval: poll });
}
export function useWards() {
  const poll = usePolling();
  return useQuery<WardSummary[]>({ queryKey: ['wards'], queryFn: () => api.get('/api/wards'), refetchInterval: poll });
}
export const useNurseHome = () => {
  const poll = usePolling();
  return useQuery<NurseHome>({ queryKey: ['nurseBeds'], queryFn: () => api.get('/api/nurse/beds'), refetchInterval: poll || 60000 });
};
export const useRound = () => {
  const poll = usePolling();
  return useQuery<RoundPatient[]>({ queryKey: ['round'], queryFn: () => api.get('/api/nurse/medication-round'), refetchInterval: poll || 60000 });
};
export const usePriority = () => {
  const poll = usePolling();
  return useQuery<PriorityPatient[]>({ queryKey: ['priority'], queryFn: () => api.get('/api/consultant/patients'), refetchInterval: poll || 60000 });
};
export const useAlerts = (active = true) => {
  const poll = usePolling();
  return useQuery<Alert[]>({ queryKey: ['alerts', active], queryFn: () => api.get(`/api/alerts?active=${active}`), refetchInterval: poll });
};
export const usePending = () => useQuery<PendingPatient[]>({ queryKey: ['pending'], queryFn: () => api.get('/api/signoff/pending') });
export const useHandover = (enabled: boolean) => useQuery<Handover>({ queryKey: ['handover'], queryFn: () => api.get('/api/nurse/handover'), enabled, staleTime: 5 * 60000, retry: false });

// ----- one admission -----
export const useAdmission = (id: number) => {
  const poll = usePolling();
  return useQuery<AdmissionHeader>({ queryKey: ['admission', id, 'header'], queryFn: () => api.get(`/api/admissions/${id}`), refetchInterval: poll });
};
export const useTimeline = (id: number) => useQuery<TimelineItem[]>({ queryKey: ['admission', id, 'timeline'], queryFn: () => api.get(`/api/admissions/${id}/timeline?limit=150`) });
export const useVitals = (id: number) => useQuery<Vital[]>({ queryKey: ['admission', id, 'vitals'], queryFn: () => api.get(`/api/admissions/${id}/vitals?hours=120`) });
export const useLabs = (id: number) => useQuery<LabsResponse>({ queryKey: ['admission', id, 'labs'], queryFn: () => api.get(`/api/admissions/${id}/labs`) });
export const useDoses = (id: number, date?: string) => useQuery<DoseSchedule>({ queryKey: ['admission', id, 'doses', date ?? 'today'], queryFn: () => api.get(`/api/admissions/${id}/doses${date ? `?date=${date}` : ''}`) });
export const usePrescriptions = (id: number) => useQuery<Prescription[]>({ queryKey: ['admission', id, 'prescriptions'], queryFn: () => api.get(`/api/admissions/${id}/prescriptions`) });
export const useFiles = (id: number) => useQuery<FileItem[]>({ queryKey: ['admission', id, 'files'], queryFn: () => api.get(`/api/admissions/${id}/files`) });
export const useNotes = (id: number) => useQuery<Note[]>({ queryKey: ['admission', id, 'notes'], queryFn: () => api.get(`/api/admissions/${id}/notes`) });
export const useJourney = (id: number) => useQuery<Journey>({ queryKey: ['admission', id, 'journey'], queryFn: () => api.get(`/api/admissions/${id}/journey`) });
export const useAdmissionAlerts = (id: number) => useQuery<Alert[]>({ queryKey: ['admission', id, 'alerts'], queryFn: () => api.get(`/api/alerts?active=false&admissionId=${id}`) });
export const useAiProgress = (id: number, enabled: boolean) =>
  useQuery<AiProgress>({ queryKey: ['admission', id, 'ai'], queryFn: () => api.post(`/api/admissions/${id}/ai-progress`, {}, 60000), enabled, retry: false, staleTime: 15 * 60000 });

export const useWardHistory = (wardId: number | null, from: string, to: string) =>
  useQuery<WardHistory>({ queryKey: ['wardHistory', wardId, from, to], queryFn: () => api.get(`/api/wards/${wardId}/history?from=${from}&to=${to}`), enabled: wardId != null });

export const useMyAdmissions = () => useQuery<AdmissionHeader[]>({ queryKey: ['me', 'admissions'], queryFn: () => api.get('/api/me/admissions') });
export const useMyStay = (id: number | undefined) => useQuery<PatientStay>({ queryKey: ['me', 'stay', id], queryFn: () => api.get(`/api/me/admissions/${id}`), enabled: id != null });

// ----- admin -----
export const useAdminWards = () => useQuery<AdminWard[]>({ queryKey: ['admin', 'wards'], queryFn: () => api.get('/api/admin/wards') });
export const useAdminBeds = (wardId: number | null) => useQuery<AdminBed[]>({ queryKey: ['admin', 'beds', wardId], queryFn: () => api.get(`/api/admin/wards/${wardId}/beds`), enabled: wardId != null });
export const useStaff = (role?: string) => useQuery<Staff[]>({ queryKey: ['admin', 'staff', role ?? 'all'], queryFn: () => api.get(`/api/admin/staff${role ? `?role=${role}` : ''}`) });
export const useShifts = (date: string, wardId: number | null) =>
  useQuery<Shift[]>({ queryKey: ['admin', 'shifts', date, wardId], queryFn: () => api.get(`/api/admin/shifts?date=${date}${wardId ? `&wardId=${wardId}` : ''}`), enabled: wardId != null });

/** Invalidate everything about one admission plus the role worklists. */
export function useInvalidateAdmission() {
  const qc = useQueryClient();
  return (id?: number) => {
    if (id) void qc.invalidateQueries({ queryKey: ['admission', id] });
    for (const k of ['nurseBeds', 'priority', 'alerts', 'round', 'board', 'pending', 'wards']) void qc.invalidateQueries({ queryKey: [k] });
  };
}

export { useMutation, useQueryClient };
