import { router } from 'expo-router';

/**
 * Lets any screen ask for a QR scan and await the token:
 *   const token = await requestScan('wristband', 'Scan the patient wristband');
 * The /scan screen resolves the pending request and goes back. Returns null if cancelled.
 */
export type ScanPurpose = 'any' | 'bed' | 'wristband';
type Pending = { purpose: ScanPurpose; prompt: string; resolve: (token: string | null) => void };
let pending: Pending | null = null;

export function requestScan(purpose: ScanPurpose, prompt: string): Promise<string | null> {
  pending?.resolve(null);
  return new Promise((resolve) => {
    pending = { purpose, prompt, resolve };
    router.push({ pathname: '/scan', params: { mode: 'request', purpose, prompt } });
  });
}

export function hasPendingScan() { return pending !== null; }
export function pendingPrompt() { return pending?.prompt ?? null; }

export function completeScan(token: string | null) {
  const p = pending;
  pending = null;
  p?.resolve(token);
}
