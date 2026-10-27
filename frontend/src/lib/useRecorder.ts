import { AudioModule, RecordingPresets, setAudioModeAsync, useAudioRecorder, useAudioRecorderState } from 'expo-audio';
import { useCallback, useEffect, useRef, useState } from 'react';

/** Tap-to-start/stop voice recorder (.m4a, AAC) with live timer and auto-stop. */
export function useRecorder(maxSeconds = 90) {
  const recorder = useAudioRecorder(RecordingPresets.HIGH_QUALITY);
  const state = useAudioRecorderState(recorder, 250);
  const [error, setError] = useState<string | null>(null);
  const [uri, setUri] = useState<string | null>(null);
  const stopping = useRef(false);

  const seconds = Math.floor((state.durationMillis ?? 0) / 1000);

  const start = useCallback(async () => {
    setError(null); setUri(null);
    try {
      const perm = await AudioModule.requestRecordingPermissionsAsync();
      if (!perm.granted) { setError('Microphone permission is needed to record'); return false; }
      await setAudioModeAsync({ playsInSilentMode: true, allowsRecording: true });
      await recorder.prepareToRecordAsync();
      recorder.record();
      return true;
    } catch (e) { setError((e as Error).message ?? 'Could not start recording'); return false; }
  }, [recorder]);

  const stop = useCallback(async (): Promise<string | null> => {
    if (stopping.current) return null;
    stopping.current = true;
    try {
      await recorder.stop();
      await setAudioModeAsync({ playsInSilentMode: true, allowsRecording: false });
      const u = recorder.uri;
      setUri(u);
      return u;
    } catch (e) { setError((e as Error).message ?? 'Could not stop recording'); return null; }
    finally { stopping.current = false; }
  }, [recorder]);

  // Auto-stop at the limit.
  const onAutoStop = useRef<((u: string | null) => void) | null>(null);
  useEffect(() => {
    if (state.isRecording && seconds >= maxSeconds) void stop().then((u) => onAutoStop.current?.(u));
  }, [state.isRecording, seconds, maxSeconds, stop]);

  return { recording: state.isRecording, seconds, uri, error, start, stop, onAutoStop };
}
