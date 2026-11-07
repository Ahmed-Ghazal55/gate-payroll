import { Preferences } from '@capacitor/preferences';

export interface RiderSession {
  accessToken: string;
  iqama: string;
  name?: string;
}

const STORAGE_KEY = 'gate_rider_mob';
let inMemorySession: RiderSession | null = null;
let sessionLoaded = false;

export async function loadSession(): Promise<RiderSession | null> {
  try {
    const { value } = await Preferences.get({ key: STORAGE_KEY });
    if (value) {
      inMemorySession = JSON.parse(value);
      sessionLoaded = true;
      return inMemorySession;
    }
  } catch {}

  try {
    const raw = localStorage.getItem(STORAGE_KEY) || sessionStorage.getItem(STORAGE_KEY);
    inMemorySession = raw ? JSON.parse(raw) : null;
    sessionLoaded = true;
    return inMemorySession;
  } catch {
    inMemorySession = null;
    sessionLoaded = true;
    return null;
  }
}

export function getSession(): RiderSession | null {
  if (!sessionLoaded) {
    try {
      const raw = localStorage.getItem(STORAGE_KEY) || sessionStorage.getItem(STORAGE_KEY);
      if (raw) inMemorySession = JSON.parse(raw);
    } catch {}
  }
  return inMemorySession;
}

export async function setSession(s: RiderSession | null): Promise<void> {
  inMemorySession = s;
  sessionLoaded = true;
  try {
    if (s) {
      await Preferences.set({ key: STORAGE_KEY, value: JSON.stringify(s) });
      localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(s));
    } else {
      await Preferences.remove({ key: STORAGE_KEY });
      localStorage.removeItem(STORAGE_KEY);
      sessionStorage.removeItem(STORAGE_KEY);
    }
  } catch {
    if (s) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  }
}

let activeFcmToken: string = '';

export async function loadActiveFcmToken(): Promise<string> {
  try {
    const { value } = await Preferences.get({ key: 'gate_rider_fcm_token' });
    if (value) {
      activeFcmToken = value;
      return activeFcmToken;
    }
  } catch {}

  try {
    activeFcmToken = localStorage.getItem('gate_rider_fcm_token') || '';
  } catch {}
  return activeFcmToken;
}

export function getActiveFcmToken(): string {
  if (!activeFcmToken) {
    try {
      activeFcmToken = localStorage.getItem('gate_rider_fcm_token') || '';
    } catch {}
  }
  return activeFcmToken;
}

export async function setActiveFcmToken(token: string): Promise<void> {
  activeFcmToken = token;
  try {
    if (token) {
      await Preferences.set({ key: 'gate_rider_fcm_token', value: token });
      localStorage.setItem('gate_rider_fcm_token', token);
    } else {
      await Preferences.remove({ key: 'gate_rider_fcm_token' });
      localStorage.removeItem('gate_rider_fcm_token');
    }
  } catch {
    if (token) {
      localStorage.setItem('gate_rider_fcm_token', token);
    } else {
      localStorage.removeItem('gate_rider_fcm_token');
    }
  }
}
