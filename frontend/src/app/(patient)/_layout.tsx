import { RoleTabs } from '@/components/RoleTabs';

export default function PatientLayout() {
  return <RoleTabs roles={['Patient']} tabs={[{ name: 'index', title: 'My stay', icon: 'heart-outline' }]} />;
}
