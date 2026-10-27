import { useAlerts, usePending } from '@/api/hooks';
import { RoleTabs } from '@/components/RoleTabs';

export default function ConsultantLayout() {
  const alerts = useAlerts(true);
  const pending = usePending();
  const open = alerts.data?.filter((a) => a.status === 'Open').length ?? 0;
  const toSign = pending.data?.reduce((a, p) => a + p.entries.length, 0) ?? 0;
  return (
    <RoleTabs roles={['Consultant']} tabs={[
      { name: 'index', title: 'Patients', icon: 'people-outline' },
      { name: 'alerts', title: 'Alerts', icon: 'notifications-outline', badge: open },
      { name: 'signoff', title: 'Sign-off', icon: 'checkmark-done-outline', badge: toSign > 99 ? 99 : toSign },
      { name: 'history', title: 'Ward history', icon: 'stats-chart-outline' },
    ]} />
  );
}
