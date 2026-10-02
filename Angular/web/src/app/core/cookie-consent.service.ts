import { Injectable, computed, signal } from '@angular/core';

export const CONSENT_KEY = 'ds-privacy-preferences';
const LIFETIME = 180 * 24 * 60 * 60 * 1000;
export interface ConsentRecord { version: 1; maps: boolean; savedAt: number; }
export function parseConsent(raw: string | null, now = Date.now()): ConsentRecord | null {
  try {
    const value = JSON.parse(raw ?? 'null');
    if (value?.version !== 1 || typeof value.maps !== 'boolean' || !Number.isFinite(value.savedAt) || value.savedAt > now || now - value.savedAt >= LIFETIME) return null;
    return { version: 1, maps: value.maps, savedAt: value.savedAt };
  } catch { return null; }
}

@Injectable({ providedIn: 'root' })
export class CookieConsentService {
  readonly record = signal<ConsentRecord | null>(this.read());
  readonly settingsOpen = signal(false);
  readonly storageFailed = signal(false);
  readonly mapsAllowed = computed(() => this.record()?.maps === true);
  readonly needsChoice = computed(() => !this.record() || this.settingsOpen());
  openSettings(): void { this.settingsOpen.set(true); }
  save(maps: boolean): void {
    const value: ConsentRecord = { version: 1, maps, savedAt: Date.now() };
    this.record.set(value);
    this.settingsOpen.set(false);
    try {
      localStorage.setItem(CONSENT_KEY, JSON.stringify(value));
      this.storageFailed.set(false);
    } catch { this.storageFailed.set(true); }
  }
  private read(): ConsentRecord | null {
    try { return parseConsent(localStorage.getItem(CONSENT_KEY)); } catch { return null; }
  }
}
