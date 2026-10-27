import { useLocalSearchParams } from 'expo-router';
import { BoardView } from '@/components/BoardView';

export default function ReceptionBoard() {
  const { ward } = useLocalSearchParams<{ ward?: string }>();
  return <BoardView key={ward ?? 'all'} initialWard={ward ? Number(ward) : undefined} />;
}
