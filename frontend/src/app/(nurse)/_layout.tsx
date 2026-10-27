import { useAlerts, useRound } from '@/api/hooks';
import { RoleTabs } from '@/components/RoleTabs';

export default function NurseLayout() {
  const round = useRound();
  const alerts = useAlerts(true);
  const due = round.data?.reduce((a, p) => a + p.doses.filter((d) => d.status === 'Due' || d.isOverdue).length, 0) ?? 0;
  const open = alerts.data?.filter((a) => a.status === 'Open').length ?? 0;
  return (
    <RoleTabs roles={['Nurse']} scan tabs={[
      { name: 'index', title: 'My beds', icon: 'bed-outline' },
      { name: 'round', title: 'Med round', icon: 'medkit-outline', badge: due },
      { name: 'handover', title: 'Handover', icon: 'swap-horizontal-outline' },
      { name: 'alerts', title: 'Alerts', icon: 'notifications-outline', badge: open },
    ]} />
  );
}
