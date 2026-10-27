import { RoleTabs } from '@/components/RoleTabs';

export default function ReceptionLayout() {
  return (
    <RoleTabs roles={['Receptionist']} scan tabs={[
      { name: 'index', title: 'Wards', icon: 'grid-outline' },
      { name: 'board', title: 'Bed board', icon: 'bed-outline' },
      { name: 'patients', title: 'Patients', icon: 'people-outline' },
    ]} />
  );
}
