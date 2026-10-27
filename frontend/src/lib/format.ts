// Display helpers. All times are shown in Asia/Dhaka (UTC+6, no DST) regardless of the phone's time zone.
const OFFSET = 6 * 3600 * 1000;
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const pad = (n: number) => String(n).padStart(2, '0');

function dhaka(iso: string | Date) {
  const d = typeof iso === 'string' ? new Date(iso) : iso;
  return new Date(d.getTime() + OFFSET);
}

export const time = (iso?: string | null) => { if (!iso) return ''; const d = dhaka(iso); return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`; };
export const dayMonth = (iso?: string | null) => { if (!iso) return ''; const d = dhaka(iso); return `${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`; };
export const dateTime = (iso?: string | null) => iso ? `${dayMonth(iso)} · ${time(iso)}` : '';
export const dhakaDateKey = (iso: string | Date = new Date()) => dhaka(iso).toISOString().slice(0, 10);

export function relative(iso?: string | null): string {
  if (!iso) return '';
  const mins = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  if (Math.abs(mins) < 1) return 'now';
  if (mins > 0) return mins < 60 ? `${mins} min ago` : mins < 1440 ? `${Math.round(mins / 60)} h ago` : `${Math.round(mins / 1440)} d ago`;
  const m = -mins;
  return m < 60 ? `in ${m} min` : `in ${Math.round(m / 60)} h`;
}

export function dueLabel(minutesFromDue: number, status: string): string {
  if (status === 'Given' || status === 'Refused' || status === 'Withheld') return status;
  if (minutesFromDue < -1) return `in ${-minutesFromDue} min`;
  if (minutesFromDue <= 1) return 'due now';
  return `${minutesFromDue} min late`;
}

export const sexLabel = (s: string) => (s === 'M' ? 'M' : s === 'F' ? 'F' : 'O');
export const num = (v: number | null | undefined, digits = 1) => v == null ? '—' : Number.isInteger(v) ? String(v) : v.toFixed(digits);
export const fmtLab = (test: string, v: number | null | undefined) => v == null ? '—' : test === 'Platelets' ? Math.round(v).toLocaleString('en-US') : String(Number(v.toFixed(2)));

export const parseNum = (s: string): number | null => {
  const t = s.trim().replace(',', '.');
  if (!t) return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
};
