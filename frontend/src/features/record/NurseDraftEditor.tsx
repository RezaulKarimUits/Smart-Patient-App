import { Ionicons } from '@expo/vector-icons';
import { Pressable, Switch, View } from 'react-native';
import type { ActiveItem, NurseDraft, NurseMedicationDraft } from '@/api/types';
import { ConfirmCard } from '@/components/clinical';
import { Button, Chip, IconButton, Row, T, TextField } from '@/components/ui';
import { parseNum } from '@/lib/format';
import type { Issue } from '@/api/types';
import { useTheme } from '@/theme';

const STATUSES: NurseMedicationDraft['status'][] = ['given', 'delayed', 'refused', 'withheld'];
const CONSC = ['Alert', 'Confusion', 'Voice', 'Pain', 'Unresponsive'];

const issuesFor = (issues: Issue[] | undefined, prefix: string) => issues?.filter((i) => i.field === prefix || i.field.startsWith(prefix + '[') || i.field.startsWith(prefix + '.')) ?? [];

/** Every field of the nurse draft is editable; medications shown as status chips. */
export function NurseDraftEditor({ draft, onChange, issues, items, showMeds = true }:
  { draft: NurseDraft; onChange: (d: NurseDraft) => void; issues?: Issue[]; items: ActiveItem[]; showMeds?: boolean }) {
  const t = useTheme();
  const v = draft.vitals;
  const setV = (patch: Partial<NurseDraft['vitals']>) => onChange({ ...draft, vitals: { ...v, ...patch } });
  const numField = (label: string, key: keyof NurseDraft['vitals'], suffix?: string) => (
    <TextField label={label} keyboardType="decimal-pad" suffix={suffix} style={{ flex: 1 }}
      value={v[key] == null ? '' : String(v[key])} onChangeText={(s) => setV({ [key]: parseNum(s) } as Partial<NurseDraft['vitals']>)} />
  );
  const vitalIssues = (issues ?? []).filter((i) => ['systolic', 'diastolic', 'pulse', 'respRate', 'spo2', 'temperature', 'consciousness', 'vitals', 'news2'].includes(i.field));
  const setMed = (i: number, patch: Partial<NurseMedicationDraft>) => onChange({ ...draft, medications: draft.medications.map((m, k) => (k === i ? { ...m, ...patch } : m)) });

  return (
    <>
      <ConfirmCard title="Vitals" icon="pulse" issues={vitalIssues}>
        <Row gap={10}>{numField('Systolic', 'systolic', 'mmHg')}{numField('Diastolic', 'diastolic')}</Row>
        <Row gap={10}>{numField('Pulse', 'pulse', '/min')}{numField('Resp. rate', 'respRate', '/min')}</Row>
        <Row gap={10}>
          {numField('Temperature', 'temperature')}
          <View style={{ flex: 1 }}>
            <T v="label" color="muted" style={{ marginBottom: 6 }}>Unit</T>
            <Row gap={6}>{(['C', 'F'] as const).map((u) => <Chip key={u} label={`°${u}`} selected={(v.temperatureUnit ?? 'C') === u} onPress={() => setV({ temperatureUnit: u })} />)}</Row>
          </View>
        </Row>
        <Row gap={10}>
          {numField('SpO2', 'spo2', '%')}
          <View style={{ flex: 1, paddingTop: 22 }}>
            <Row><T style={{ flex: 1 }}>On oxygen</T><Switch value={!!v.onOxygen} onValueChange={(x) => setV({ onOxygen: x })} trackColor={{ true: t.colors.primary, false: t.colors.border }} /></Row>
          </View>
        </Row>
        <T v="label" color="muted" style={{ marginBottom: 6 }}>Consciousness (ACVPU)</T>
        <Row wrap gap={6}>{CONSC.map((c) => <Chip key={c} label={c} selected={(v.consciousness ?? 'Alert') === c} onPress={() => setV({ consciousness: c })} />)}</Row>
      </ConfirmCard>

      {showMeds ? (
        <ConfirmCard title="Medicines" icon="medkit" issues={issuesFor(issues, 'medications')}>
          {draft.medications.length === 0 ? <T color="muted">No medicines mentioned.</T> : null}
          {draft.medications.map((m, i) => (
            <View key={i} style={{ paddingVertical: 8, borderTopWidth: i ? 1 : 0, borderColor: t.colors.border }}>
              <Row>
                <T v="bodyStrong" style={{ flex: 1 }}>{m.drugName} {m.dose ?? ''} {m.route ?? ''}{m.time ? ` · ${m.time}` : ''}</T>
                <IconButton icon="trash-outline" label="Remove" onPress={() => onChange({ ...draft, medications: draft.medications.filter((_, k) => k !== i) })} />
              </Row>
              <T v="caption" color="muted" style={{ marginBottom: 6 }}>Prescription item</T>
              <Row wrap gap={6} style={{ marginBottom: 8 }}>
                {items.map((it) => <Chip key={it.id} label={`${it.drugName} ${it.dose} ${it.frequency}`} selected={m.prescriptionItemId === it.id}
                  onPress={() => setMed(i, { prescriptionItemId: it.id, drugName: it.drugName })} />)}
              </Row>
              <Row wrap gap={6}>
                {STATUSES.map((s) => <Chip key={s} label={s[0].toUpperCase() + s.slice(1)} selected={m.status === s} tone={s === 'given' ? t.colors.stable : t.colors.watch} onPress={() => setMed(i, { status: s })} />)}
              </Row>
              {m.status !== 'given' ? <TextField label="Reason" value={m.reason ?? ''} onChangeText={(r) => setMed(i, { reason: r || null })} style={{ marginTop: 8 }} /> : null}
            </View>
          ))}
          {items.length ? (
            <Pressable onPress={() => onChange({ ...draft, medications: [...draft.medications, { prescriptionItemId: items[0].id, drugName: items[0].drugName, dose: items[0].dose, route: items[0].route, time: null, status: 'given', reason: null }] })}>
              <Row style={{ paddingTop: 8 }}><Ionicons name="add-circle-outline" size={18} color={t.colors.primary} /><T v="label" color="primary">Add medicine</T></Row>
            </Pressable>
          ) : null}
        </ConfirmCard>
      ) : null}

      <ConfirmCard title="Results" icon="flask" issues={issuesFor(issues, 'labResults')}>
        {draft.labResults.map((l, i) => (
          <Row key={i} gap={8}>
            <TextField label={i === 0 ? 'Test' : undefined} value={l.testName} onChangeText={(s) => onChange({ ...draft, labResults: draft.labResults.map((x, k) => k === i ? { ...x, testName: s } : x) })} style={{ flex: 2 }} />
            <TextField label={i === 0 ? 'Value' : undefined} keyboardType="decimal-pad" value={l.value == null ? '' : String(l.value)} onChangeText={(s) => onChange({ ...draft, labResults: draft.labResults.map((x, k) => k === i ? { ...x, value: parseNum(s) } : x) })} style={{ flex: 1.3 }} />
            <TextField label={i === 0 ? 'Unit' : undefined} value={l.unit ?? ''} onChangeText={(s) => onChange({ ...draft, labResults: draft.labResults.map((x, k) => k === i ? { ...x, unit: s || null } : x) })} style={{ flex: 1.2 }} />
            <IconButton icon="close" label="Remove" onPress={() => onChange({ ...draft, labResults: draft.labResults.filter((_, k) => k !== i) })} />
          </Row>
        ))}
        <Button title="Add result" icon="add" variant="ghost" size="sm" onPress={() => onChange({ ...draft, labResults: [...draft.labResults, { testName: '', value: null, unit: null }] })} />
      </ConfirmCard>

      <ConfirmCard title="Nursing note" icon="document-text" issues={issuesFor(issues, 'note')}>
        <TextField multiline value={draft.note ?? ''} onChangeText={(s) => onChange({ ...draft, note: s || null })} placeholder="Observations, intake/output, events…" />
      </ConfirmCard>
    </>
  );
}

export const emptyNurseDraft = (): NurseDraft => ({
  vitals: { systolic: null, diastolic: null, pulse: null, respRate: null, temperature: null, temperatureUnit: 'C', spo2: null, onOxygen: null, consciousness: null },
  medications: [], labResults: [], note: null, clarificationQuestion: null,
});
