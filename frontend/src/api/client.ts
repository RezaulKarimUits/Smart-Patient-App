import Constants from 'expo-constants';
import { Platform } from 'react-native';

/** API base URL: EXPO_PUBLIC_API_URL, else the Metro host (works on a LAN phone), else the emulator host. */
function resolveBaseUrl(): string {
  const env = process.env.EXPO_PUBLIC_API_URL;
  if (env) return env.replace(/\/$/, '');
  const host = Constants.expoConfig?.hostUri?.split(':')[0];
  if (host) return `http://${host}:5080`;
  return Platform.OS === 'android' ? 'http://10.0.2.2:5080' : 'http://localhost:5080';
}

export const API_URL = resolveBaseUrl();

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

let token: string | null = null;
let onUnauthorized: (() => void) | null = null;
export const setToken = (t: string | null) => { token = t; };
export const getToken = () => token;
export const setUnauthorizedHandler = (fn: () => void) => { onUnauthorized = fn; };

type Body = object | FormData | undefined;

// Requests wait until the stored session has been restored, so deep links don't fire unauthenticated calls.
let markReady: () => void = () => undefined;
const authReady = new Promise<void>((r) => { markReady = r; });
export const markAuthReady = () => markReady();

/** True when the API answers anything (even 401) within 5 s. */
async function serverReachable(): Promise<boolean> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 5000);
  try { await fetch(`${API_URL}/api/auth/me`, { signal: controller.signal }); return true; }
  catch { return false; }
  finally { clearTimeout(timer); }
}

/**
 * Multipart uploads go through XMLHttpRequest, not fetch: Expo replaces the global fetch with expo/fetch, which
 * rejects React Native file parts ({ uri, name, type }) with "Unsupported FormDataPart implementation".
 * React Native's XMLHttpRequest still streams those files natively.
 */
function sendForm(method: string, url: string, headers: Record<string, string>, form: FormData, signal: AbortSignal) {
  return new Promise<{ ok: boolean; status: number; text: () => Promise<string> }>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open(method, url);
    Object.entries(headers).forEach(([k, v]) => xhr.setRequestHeader(k, v));
    const abort = () => xhr.abort();
    signal.addEventListener('abort', abort);
    const done = () => signal.removeEventListener('abort', abort);
    xhr.onload = () => {
      done();
      const body = xhr.responseText ?? '';
      resolve({ ok: xhr.status >= 200 && xhr.status < 300, status: xhr.status, text: async () => body });
    };
    xhr.onerror = () => { done(); reject(new Error('Network request failed')); };
    xhr.onabort = () => { done(); const e = new Error('Aborted'); e.name = 'AbortError'; reject(e); };
    xhr.send(form);
  });
}

async function request<T>(method: string, path: string, body?: Body, timeoutMs = 45000): Promise<T> {
  if (path !== '/api/auth/login') await authReady;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const isForm = typeof FormData !== 'undefined' && body instanceof FormData;
  if (body && !isForm) headers['Content-Type'] = 'application/json';
  let res: { ok: boolean; status: number; text: () => Promise<string> };
  try {
    res = isForm
      ? await sendForm(method, `${API_URL}${path}`, headers, body as FormData, controller.signal)
      : await fetch(`${API_URL}${path}`, {
        method, headers, signal: controller.signal,
        body: body === undefined ? undefined : JSON.stringify(body),
      });
  } catch (e) {
    clearTimeout(timer);
    if ((e as Error)?.name === 'AbortError') throw new ApiError(0, 'The server took too long to respond');
    if (__DEV__) console.warn(`[api] ${method} ${path} failed:`, (e as Error)?.message);
    // "Network request failed" is also what React Native reports when a file upload breaks off,
    // so check whether the server answers at all before blaming the connection.
    if (isForm && (await serverReachable()))
      throw new ApiError(0, 'The upload was interrupted. Please try again');
    throw new ApiError(0, `Cannot reach the server at ${API_URL}`);
  } finally {
    clearTimeout(timer);
  }
  const text = await res.text();
  let data: unknown = null;
  if (text) { try { data = JSON.parse(text); } catch { data = text; } }
  if (!res.ok) {
    if (res.status === 401 && path !== '/api/auth/login') onUnauthorized?.();
    const msg = (data as { error?: string } | null)?.error ?? `Request failed (${res.status})`;
    throw new ApiError(res.status, msg);
  }
  return data as T;
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: Body, timeoutMs?: number) => request<T>('POST', path, body ?? {}, timeoutMs),
  put: <T>(path: string, body?: Body) => request<T>('PUT', path, body ?? {}),
  del: <T>(path: string) => request<T>('DELETE', path),
};

/** React Native FormData file part from a local uri. */
export function filePart(uri: string, name: string, type: string): Blob {
  return { uri, name, type } as unknown as Blob;
}

/** Plain-language heading + message for a failed call, and whether it is something the user can fix (warning) or a failure (error). */
export function describeError(e: unknown): { kind: 'warning' | 'error'; title: string; message: string } {
  if (!(e instanceof ApiError)) return { kind: 'error', title: 'Something went wrong', message: errorMessage(e) };
  const m = e.message;
  switch (e.status) {
    case 0: return { kind: 'error', title: 'No connection', message: `${m}. Check Wi-Fi and that the server is running.` };
    case 400: case 422: return { kind: 'warning', title: 'Please check the details', message: m };
    case 401: return { kind: 'warning', title: 'Session expired', message: 'Please sign in again.' };
    case 403: return { kind: 'warning', title: 'Not allowed', message: m === 'Forbidden' ? 'Your role cannot do this.' : m };
    case 404: return { kind: 'warning', title: 'Not found', message: m };
    case 409: return { kind: 'error', title: m === 'Wrong patient' ? 'Wrong patient' : 'Cannot complete this', message: m === 'Wrong patient' ? 'The wristband does not match this patient. Nothing was recorded.' : m };
    case 413: return { kind: 'warning', title: 'File too large', message: m };
    case 503: return { kind: 'error', title: 'Service unavailable', message: m };
    case 504: return { kind: 'error', title: 'Timed out', message: 'The server took too long. Please try again.' };
    default:
      return { kind: 'error', title: 'Something went wrong', message: __DEV__ ? m : 'Please try again in a moment.' };
  }
}

export function errorMessage(e: unknown): string {
  if (e instanceof ApiError) return e.message;
  if (e instanceof Error) return e.message;
  return 'Something went wrong';
}
