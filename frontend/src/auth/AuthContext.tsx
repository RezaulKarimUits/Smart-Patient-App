import * as SecureStore from 'expo-secure-store';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Platform } from 'react-native';
import { api, markAuthReady, setToken, setUnauthorizedHandler } from '@/api/client';
import type { LoginResponse, User } from '@/api/types';

const KEY = 'inpatient.session';

type Session = { token: string; user: User; expiresAt: string };
type AuthState = {
  ready: boolean;
  user: User | null;
  token: string | null;
  signIn: (phone: string, password: string, hospitalCode?: string) => Promise<User>;
  signOut: () => Promise<void>;
};

const AuthContext = createContext<AuthState | null>(null);

// SecureStore is native-only; fall back to localStorage on web.
const store = {
  get: async () => Platform.OS === 'web' ? globalThis.localStorage?.getItem(KEY) ?? null : SecureStore.getItemAsync(KEY),
  set: async (v: string) => Platform.OS === 'web' ? globalThis.localStorage?.setItem(KEY, v) : SecureStore.setItemAsync(KEY, v),
  del: async () => Platform.OS === 'web' ? globalThis.localStorage?.removeItem(KEY) : SecureStore.deleteItemAsync(KEY),
};

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);

  const signOut = useCallback(async () => {
    setToken(null);
    setSession(null);
    try { await store.del(); } catch { /* ignore */ }
  }, []);

  useEffect(() => {
    setUnauthorizedHandler(() => { void signOut(); });
    (async () => {
      try {
        const raw = await store.get();
        if (raw) {
          const s = JSON.parse(raw) as Session;
          if (new Date(s.expiresAt).getTime() > Date.now()) { setToken(s.token); setSession(s); }
        }
      } catch { /* corrupted session: start fresh */ }
      markAuthReady();
      setReady(true);
    })();
  }, [signOut]);

  const signIn = useCallback(async (phone: string, password: string, hospitalCode?: string) => {
    const res = await api.post<LoginResponse>('/api/auth/login', { phone: phone.trim(), password, hospitalCode: hospitalCode?.trim() || null });
    if (res.user.role === 'SuperAdmin') throw new Error('SuperAdmin uses the web admin page (/admin) on the API server');
    const s: Session = { token: res.token, user: res.user, expiresAt: res.expiresAt };
    setToken(s.token);
    setSession(s);
    try { await store.set(JSON.stringify(s)); } catch { /* keep in memory */ }
    return res.user;
  }, []);

  const value = useMemo<AuthState>(() => ({ ready, user: session?.user ?? null, token: session?.token ?? null, signIn, signOut }),
    [ready, session, signIn, signOut]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth outside AuthProvider');
  return ctx;
}

export function homeFor(role: User['role']): string {
  switch (role) {
    case 'Receptionist': return '/(reception)';
    case 'Nurse': return '/(nurse)';
    case 'Consultant': return '/(consultant)';
    case 'HospitalAdmin': return '/(admin)';
    case 'Patient': return '/(patient)';
    default: return '/login';
  }
}
