// Mirrors backend DTOs (camelCase JSON). Times are ISO UTC strings.
export type Role = 'SuperAdmin' | 'HospitalAdmin' | 'Receptionist' | 'Nurse' | 'Consultant' | 'Patient';
export type BedStatus = 'Free' | 'Reserved' | 'Occupied' | 'Cleaning' | 'Blocked';
export type Flag = 'Stable' | 'Watch' | 'Critical';
export type News2Band = 'Low' | 'LowMedium' | 'Medium' | 'High';

export interface User {
  id: number; name: string; phone: string; role: Role; designation: string | null;
  hospitalId: number | null; hospitalName: string | null; hospitalCode: string | null; patientId: number | null;
}
export interface LoginResponse { token: string; expiresAt: string; user: User }

export interface BedOccupant {
  admissionId: number; patientId: number; patientName: string; mrn: string; ageYears: number; sex: string; diagnosis: string | null;
  consultantName: string; news2Score: number | null; news2Band: News2Band | null; flag: Flag; activeAlerts: number; awaitingScan: boolean;
}
export interface BedTile {
  id: number; wardId: number; wardName: string; bedNumber: string; status: BedStatus; statusChangedAt: string; lastNote: string | null;
  occupant: BedOccupant | null;
}
export interface Occupancy { totalBeds: number; free: number; reserved: number; occupied: number; cleaning: number; blocked: number; occupancyPct: number }
export interface StaffBrief { id: number; name: string; designation: string | null }
export interface WardSummary { id: number; name: string; code: string; type: string; floor: string | null; occupancy: Occupancy; watchCount: number; criticalCount: number; consultants: StaffBrief[] }
export interface BoardWard { id: number; name: string; code: string; type: string; occupancy: Occupancy; beds: BedTile[] }
export interface Board { hospitalId: number; hospitalName: string; generatedAt: string; wards: BoardWard[] }
export interface Qr { token: string; pngBase64: string; label: string }

export interface Patient { id: number; mrn: string; name: string; dateOfBirth: string; ageYears: number; sex: string; phone: string | null; guardian: string | null; bloodGroup: string | null; activeAdmissionId: number | null }
export interface AdmissionHeader {
  id: number; patientId: number; patientName: string; mrn: string; ageYears: number; sex: string; bloodGroup: string | null;
  bedId: number | null; bedNumber: string | null; wardId: number | null; wardName: string | null; wardType: string | null;
  consultantUserId: number; consultantName: string; admittedAt: string; provisionalDiagnosis: string | null; status: 'Active' | 'Discharged';
  bedConfirmed: boolean; dischargedAt: string | null; news2Score: number | null; news2Band: News2Band | null; news2Applicable: boolean;
  flag: Flag; activeAlerts: number; dayOfStay: number;
}
export interface Wristband {
  admissionId: number; patientName: string; mrn: string; dateOfBirth: string; ageYears: number; sex: string; bloodGroup: string | null;
  hospitalName: string; wardName: string | null; bedNumber: string | null; consultantName: string; admittedAt: string; token: string; qrPngBase64: string;
}
export interface AdmitResponse { admission: AdmissionHeader; wristband: Wristband }
export interface ScanResolve { kind: 'bed' | 'admission'; bed: BedTile | null; admission: AdmissionHeader | null }
export interface JourneyStay { stayId: number; sequence: number; wardId: number; wardName: string; bedId: number; bedNumber: string; reason: string; startAt: string; endAt: string | null; durationHours: number }
export interface Journey { admissionId: number; patientName: string; mrn: string; admissionStatus: string; stays: JourneyStay[] }

export interface BedEvent { id: number; bedId: number; bedNumber: string; wardId: number; wardName: string; eventType: string; at: string; note: string | null; admissionId: number | null; patientName: string | null; byUserName: string | null }
export interface WardHistoryDay { day: string; admissionsIn: number; transfersIn: number; transfersOut: number; discharges: number; occupiedBedDays: number; avgLengthOfStayDays: number | null }
export interface WardHistory {
  wardId: number; wardName: string; from: string; to: string; totalBeds: number; occupancyNow: Occupancy;
  totals: { admissionsIn: number; transfersIn: number; transfersOut: number; discharges: number; occupiedBedDays: number; avgLengthOfStayDays: number | null; avgOccupancyPct: number };
  days: WardHistoryDay[]; events: BedEvent[];
}

export interface PrescriptionItem { id: number; drugName: string; form: string | null; strength: string | null; dose: string; route: string; frequency: string; startAt: string; durationDays: number | null; instructions: string | null; isActive: boolean; stoppedAt: string | null; summary: string }
export interface FileItem { id: number; kind: string; caption: string | null; contentType: string; sizeBytes: number; uploadedAt: string; uploadedByName: string | null; admissionId: number | null; prescriptionId: number | null; url: string | null; urlExpiresAt: string | null }
export interface Prescription { id: number; admissionId: number; consultantUserId: number; consultantName: string; source: string; status: 'Draft' | 'Signed'; createdAt: string; signedAt: string | null; generalInstructions: string | null; items: PrescriptionItem[]; files: FileItem[]; doseTasksCreated: number }
export interface PrescriptionItemInput { drugName: string; form: string | null; strength: string | null; dose: string; route: string; frequency: string; durationDays: number | null; instructions: string | null; startAt?: string | null }

export interface DoseTask { id: number; admissionId: number; prescriptionItemId: number; drugName: string; form: string | null; strength: string | null; dose: string; route: string; frequency: string; instructions: string | null; dueAt: string; status: string; doneAt: string | null; doneByName: string | null; reason: string | null; isOverdue: boolean; minutesFromDue: number }
export interface RoundPatient { admissionId: number; patientName: string; bedNumber: string | null; wardName: string | null; flag: Flag; doses: DoseTask[] }
export interface DoseSchedule { admissionId: number; date: string; doses: DoseTask[]; prnItems: PrescriptionItem[] }

export interface Alert { id: number; admissionId: number; patientName: string; bedNumber: string | null; wardName: string | null; severity: 'Info' | 'Warning' | 'Critical'; alertType: string; ruleCode: string; ruleDetail: string; aiExplanation: string | null; status: 'Open' | 'Acknowledged' | 'Resolved'; escalationLevel: number; createdAt: string; ackAt: string | null; ackByName: string | null; doseTaskId: number | null }

export interface Vital { id: number; recordedAt: string; systolic: number | null; diastolic: number | null; pulse: number | null; respRate: number | null; temperatureC: number | null; spO2: number | null; onOxygen: boolean; consciousness: string; news2Score: number | null; news2Band: News2Band | null; source: string; recordedByName: string | null; signedOffByName: string | null; signedOffAt: string | null }
export interface VitalsInput { systolic: number | null; diastolic: number | null; pulse: number | null; respRate: number | null; temperature: number | null; temperatureUnit: 'C' | 'F' | null; spO2: number | null; onOxygen: boolean | null; consciousness: string | null }
export interface Monitoring { news2Score: number | null; news2Band: News2Band | null; flag: Flag; newAlerts: Alert[]; vitalsSuggestion: string | null }
export interface Lab { id: number; testName: string; value: number | null; textValue: string | null; unit: string | null; refLow: number | null; refHigh: number | null; flag: string; resultAt: string; source: string; storedFileId: number | null; recordedByName: string | null; signedOffByName: string | null; signedOffAt: string | null }
export interface LabCell { id: number; value: number | null; textValue: string | null; flag: string; resultAt: string }
export interface LabRow { testName: string; unit: string | null; refLow: number | null; refHigh: number | null; cells: (LabCell | null)[]; trend: 'up' | 'down' | 'stable' | null }
export interface LabsResponse { results: Lab[]; table: { dates: string[]; rows: LabRow[] } }
export interface LabInput { testName: string; value: number | null; textValue?: string | null; unit: string | null; refLow?: number | null; refHigh?: number | null; resultAt?: string | null }
export interface LabsSaveResponse { results: Lab[]; notes: string[]; monitoring: Monitoring }
export interface Note { id: number; noteType: string; text: string; source: string; createdAt: string; authorUserId: number; authorName: string; authorRole: string | null; signedOffByName: string | null; signedOffAt: string | null; audioFileId: number | null }
export interface TimelineItem { type: string; at: string; title: string; detail: string | null; actor: string | null; source: string | null; severity: string | null; refId: number; signedOff: boolean | null; signedOffByName: string | null }
export interface PendingEntry { kind: 'vital' | 'lab' | 'note'; id: number; at: string; summary: string; byName: string | null; source: string; flag: string | null }
export interface PendingPatient { admissionId: number; patientName: string; bedNumber: string | null; wardName: string | null; flag: Flag; entries: PendingEntry[] }

export interface CareTask { id: number; admissionId: number; taskType: string; detail: string; dueAt: string; status: string; isOverdue: boolean }
export interface NurseBed { bedId: number; bedNumber: string; wardId: number; wardName: string; bedStatus: BedStatus; occupant: BedOccupant | null; dueDoses: number; overdueDoses: number; nextDoseAt: string | null; openAlerts: number; careTasks: CareTask[]; lastVitalsAt: string | null }
export interface NurseHome { shiftDate: string; shift: string; shiftStart: string; shiftEnd: string; beds: NurseBed[] }
export interface PriorityPatient { patient: AdmissionHeader; activeAlerts: Alert[]; pendingSignOff: number; rank: number; reason: string }

// ----- AI / voice -----
export interface Issue { level: 'warning' | 'blocker'; field: string; message: string }
export interface ActiveItem { id: number; drugName: string; form: string | null; strength: string | null; dose: string; route: string; frequency: string }
export interface NurseVitalsDraft { systolic: number | null; diastolic: number | null; pulse: number | null; respRate: number | null; temperature: number | null; temperatureUnit: string | null; spo2: number | null; onOxygen: boolean | null; consciousness: string | null }
export interface NurseMedicationDraft { prescriptionItemId: number | null; drugName: string; dose: string | null; route: string | null; time: string | null; status: 'given' | 'delayed' | 'refused' | 'withheld'; reason: string | null }
export interface NurseLabDraft { testName: string; value: number | null; unit: string | null }
export interface NurseDraft { vitals: NurseVitalsDraft; medications: NurseMedicationDraft[]; labResults: NurseLabDraft[]; note: string | null; clarificationQuestion: string | null }
export interface PrescriptionItemDraft { drugName: string; form: string | null; strength: string | null; dose: string | null; route: string | null; frequency: string; durationDays: number | null; instructions: string | null }
export interface PrescriptionDraft { items: PrescriptionItemDraft[]; generalInstructions: string | null; clarificationQuestion: string | null }
export interface RoundNoteDraft { note: string; plan: string | null; clarificationQuestion: string | null }
export interface VoiceDraftResponse<T> { captureId: number; purpose: string; transcript: string; draft: T; warnings: Issue[]; clarificationQuestion: string | null; requiresWristband: boolean; activeItems: ActiveItem[]; audioFileId: number | null }
export interface VoiceConfirmResponse {
  captureId: number; purpose: string;
  nurse: { vitalId: number | null; news2Score: number | null; news2Band: News2Band | null; doses: DoseTask[]; labIds: number[]; noteId: number | null } | null;
  prescription: Prescription | null; note: Note | null; monitoring: Monitoring | null;
}
export interface ExtractedResult { testName: string; rawTestName: string; value: number | null; unit: string | null; refLow: number | null; refHigh: number | null; flag: string; note: string | null }
export interface ReportExtract { file: FileItem; results: ExtractedResult[]; warnings: Issue[]; reportDate: string | null; clarificationQuestion: string | null }
export interface ParameterTrend { name: string; group: 'vitals' | 'labs'; previous: number | null; previousAt: string | null; latest: number | null; latestAt: string | null; unit: string | null; change: number | null; direction: 'improving' | 'worsening' | 'stable'; comment: string | null }
export interface AiProgress { admissionId: number; generatedAt: string; cached: boolean; overallTrajectory: 'improving' | 'worsening' | 'stable' | 'mixed'; summary: string; parameters: ParameterTrend[]; watchFor: string[]; model: string; disclaimer: string }
export interface HandoverPatient { admissionId: number; patientName: string; bedNumber: string | null; flag: Flag; facts: { news2First: number | null; news2Latest: number | null; lastVitals: string | null; lastVitalsAt: string | null; dosesGiven: string[]; dosesMissedOrRefused: string[]; newResults: string[]; openAlerts: string[]; pendingTasks: string[] }; summary: string; priorities: string[] }
export interface Handover { shift: string; generatedAt: string; windowStart: string; patients: HandoverPatient[]; model: string }
export interface DischargeDraft { admissionId: number; summaryText: string; generatedAt: string; model: string }

export interface PatientStay { admission: AdmissionHeader; consultantDesignation: string | null; hospitalName: string; todaysMedicines: { doseId: number; medicine: string; dose: string; route: string; dueAt: string; status: string; givenAt: string | null }[]; signedReports: Lab[]; files: FileItem[]; dischargeSummary: string | null; journey: JourneyStay[] }

// ----- Admin -----
export interface AdminWard { id: number; name: string; code: string; type: string; floor: string | null; isActive: boolean; beds: number; occupancy: Occupancy; consultants: StaffBrief[] }
export interface Staff { id: number; name: string; phone: string; role: Role; designation: string | null; isActive: boolean; patientId: number | null; wardIds: number[] }
export interface Shift { id: number; nurseUserId: number; nurseName: string; wardId: number; wardName: string; shiftDate: string; shiftType: string; bedIds: number[]; bedNumbers: string[] }
export interface AdminBed { id: number; wardId: number; bedNumber: string; status: string }
export interface ScanTokens { beds: { kind: string; label: string; token: string }[]; wristbands: { kind: string; label: string; token: string }[] }
