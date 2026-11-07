export interface RiderSession {
  accessToken: string;
  iqama: string;
  name?: string;
}

const STORAGE_KEY = 'gate_rider_mob';

export function getSession(): RiderSession | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY) || sessionStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

export function setSession(s: RiderSession | null): void {
  try {
    if (s) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(s));
    } else {
      localStorage.removeItem(STORAGE_KEY);
      sessionStorage.removeItem(STORAGE_KEY);
    }
  } catch {}
}

let activeFcmToken: string = '';

export function getActiveFcmToken(): string {
  if (!activeFcmToken) {
    try {
      activeFcmToken = localStorage.getItem('gate_rider_fcm_token') || '';
    } catch {}
  }
  return activeFcmToken;
}

export function setActiveFcmToken(token: string): void {
  activeFcmToken = token;
  try {
    if (token) {
      localStorage.setItem('gate_rider_fcm_token', token);
    } else {
      localStorage.removeItem('gate_rider_fcm_token');
    }
  } catch {}
}
