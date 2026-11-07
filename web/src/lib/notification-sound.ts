// Audible alert for personal notifications (tasks addressed to the user).
// Web Audio only: no media file, nothing inline, so the strict CSP stays intact.
const KEY = 'gate_personal_sound';

export function personalSoundEnabled(): boolean {
  try {
    return localStorage.getItem(KEY) !== 'off';
  } catch {
    return true;
  }
}

export function setPersonalSoundEnabled(on: boolean): void {
  try {
    localStorage.setItem(KEY, on ? 'on' : 'off');
  } catch {
    // storage blocked: the choice lasts for this page only
  }
}

let context: AudioContext | null = null;

// Browsers start audio suspended until the first user gesture; resume it then.
export function unlockNotificationSound(): void {
  try {
    const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!Ctor) return;
    context ??= new Ctor();
    if (context.state === 'suspended') void context.resume();
  } catch {
    // no audio support: stay silent
  }
}

// Two short rising tones (about 0.4 s), quiet enough for an office.
export function playPersonalChime(): void {
  if (!personalSoundEnabled()) return;
  unlockNotificationSound();
  const ctx = context;
  if (!ctx || ctx.state !== 'running') return;
  const start = ctx.currentTime;
  [660, 880].forEach((frequency, i) => {
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    const at = start + i * 0.18;
    osc.type = 'sine';
    osc.frequency.value = frequency;
    gain.gain.setValueAtTime(0.0001, at);
    gain.gain.exponentialRampToValueAtTime(0.18, at + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.2);
    osc.connect(gain).connect(ctx.destination);
    osc.start(at);
    osc.stop(at + 0.22);
  });
}
