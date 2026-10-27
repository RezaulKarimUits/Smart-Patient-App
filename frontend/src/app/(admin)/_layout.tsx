import { RoleTabs } from '@/components/RoleTabs';

export default function AdminLayout() {
  return (
    <RoleTabs roles={['HospitalAdmin']} tabs={[
      { name: 'index', title: 'Occupancy', icon: 'pie-chart-outline' },
      { name: 'wards', title: 'Wards & beds', icon: 'bed-outline' },
      { name: 'staff', title: 'Staff & shifts', icon: 'people-outline' },
      { name: 'history', title: 'Ward history', icon: 'stats-chart-outline' },
    ]} />
  );
}
