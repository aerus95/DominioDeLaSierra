import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CookieConsentService } from '../core/cookie-consent.service';

@Component({ selector: 'ds-cookie-consent', standalone: true, imports: [RouterLink], template: `
  @if (consent.needsChoice()) {
    <section class="cookie-panel" aria-labelledby="cookie-title" aria-label="Preferencias de privacidad">
      <div class="cookie-copy"><p class="eyebrow">Tu privacidad, a tu manera</p><h2 id="cookie-title">Solo lo que tú elijas.</h2><p>Guardamos tus preferencias en este navegador. Google Maps solo se conecta si lo autorizas. No hemos incorporado analítica ni publicidad.</p><a routerLink="/cookies">Cookies y almacenamiento ↗</a></div>
      <div class="cookie-controls">
        @if (consent.settingsOpen()) {
          <p class="cookie-essential">Almacenamiento necesario · siempre activo</p><p class="cookie-help">Recuerda la mayoría de edad y esta elección.</p>
          <label class="cookie-option"><input #maps type="checkbox" [checked]="consent.mapsAllowed()"><span><strong>Google Maps</strong><small>Mapa externo: Google puede recibir tu IP y utilizar cookies.</small></span></label>
          <button type="button" class="cookie-choice" (click)="consent.save(maps.checked)">Guardar mi elección</button>
        }
        <div class="cookie-actions"><button type="button" class="cookie-choice" (click)="consent.save(false)">Rechazar opcionales</button><button type="button" class="cookie-choice" (click)="consent.save(true)">Aceptar opcionales</button></div>
        @if (!consent.settingsOpen()) { <button type="button" class="cookie-configure" (click)="consent.openSettings()">Configurar preferencias</button> }
      </div>
    </section>
  }
  @if (consent.storageFailed()) { <p class="cookie-storage-note" role="status">Tu elección se aplica ahora, pero este navegador no permite guardarla para tu próxima visita.</p> }
` })
export class CookieConsentComponent { readonly consent = inject(CookieConsentService); }
